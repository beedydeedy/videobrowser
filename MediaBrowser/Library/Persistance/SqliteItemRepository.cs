﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using MediaBrowser.Library.Interfaces;
using MediaBrowser.Library.Entities;
using System.Data.SQLite;
using MediaBrowser.Library.Configuration;
using System.IO;
using MediaBrowser.Library.Logging;
using System.Reflection;
using System.Threading;
using MediaBrowser.Library.Threading;
using MediaBrowser.Library.Extensions;


namespace MediaBrowser.Library.Persistance {


    public class SqliteItemRepository : SQLiteRepository, IItemRepository {

        private static Guid MIGRATE_MARKER = new Guid("0FD78B4C-B9DA-4249-B880-3696761AE3B4");
        private Dictionary<Type, SQLInfo> ItemSQL = new Dictionary<Type, SQLInfo>();

        protected static class SQLizer
        {
            private static System.Reflection.Assembly serviceStackAssembly;
            private static System.Reflection.Assembly ServiceStackResolver(object sender, ResolveEventArgs args)
            {
                Logger.ReportInfo(args.Name + " is being resolved!");
                if (args.Name.StartsWith("ServiceStack.Text,"))
                {
                    return serviceStackAssembly;
                }
                return null;
            }

            public static void Init(string path)
            {
                //if (serviceStackAssembly == null)
                //{
                //    serviceStackAssembly = System.Reflection.Assembly.LoadFile(path);
                //    AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(ServiceStackResolver);
                //}
            }

            public static object Extract(SQLiteDataReader reader, SQLInfo.ColDef col) 
            {
                var data = reader[col.ColName];
                var typ = data.GetType();
                if (data is DBNull || data == null) return null;

                //Logger.ReportVerbose("Extracting: " + col.ColName + " Defined Type: "+col.ColType+"/"+col.NullableType + " Actual Type: "+typ.Name+" Value: "+data);

                if (typ == typeof(string))
                {
                    if (col.ColType == typeof(MediaType))
                        return Enum.Parse(typeof(MediaType), (string)data);
                    else
                        if (col.ColType == typeof(Guid))
                        {
                            return new Guid((string)data);
                        }
                        else
                            return data;
                } else
                    if (typ == typeof(Int64)) {
                        if (col.ColType == typeof(DateTime))
                            return new DateTime((Int64)data);
                        else if (col.InternalType == typeof(int) || col.ColType == typeof(int))
                            return Convert.ToInt32(data);
                        else
                            return data;
                    } else
                        if (typ == typeof(Double)) {
                            if (col.ColType == typeof(Single) || col.InternalType == typeof(Single))
                                return Convert.ToSingle(data);
                            else
                                return data;
                        } else
                        {
                            var ms = new MemoryStream((byte[])data);
                            return Serializer.Deserialize<object>(ms);
                            //return JsonSerializer.DeserializeFromString((string)reader[col.ColName], col.ColType);
                        }

                            
            }

            public static object Encode(SQLInfo.ColDef col, object data) 
            {
                if (data == null) return null;

                //Logger.ReportVerbose("Encoding " + col.ColName + " as " + col.ColType.Name.ToLower());
                switch (col.ColType.Name.ToLower())
                {
                    case "guid":
                        return data.ToString();
                    case "string":
                        return data;

                    case "datetime":
                        return ((DateTime)data).Ticks;

                    case "mediatype":
                        return ((MediaType)data).ToString();

                    case "int":
                    case "int16":
                    case "int32":
                    case "int64":
                    case "long":
                    case "double":
                    case "nullable`1":
                        return data;
                    default:
                        var ms = new MemoryStream();
                        Serializer.Serialize<object>(ms,data);
                        ms.Seek(0,0);
                        return ms.ReadAllBytes();

                }
            }
        }


        protected class SQLInfo
        {

            public struct ColDef
            {
                public string ColName;
                public Type ColType;
                public Type InternalType;
                public bool ListType;
                public MemberTypes MemberType;
                public PropertyInfo PropertyInfo;
                public FieldInfo FieldInfo;

            }

            protected string ObjType;
            public List<ColDef> Columns = new List<ColDef>();

            public void FixUpSchema(SQLiteConnection connection)
            {
                //make sure all our columns are in the db
                var cmd = connection.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(items)";
                List<string> dbCols = new List<string>();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        dbCols.Add(reader[1].ToString());
                    }
                }
                if (!dbCols.Contains("obj_type")) connection.Exec("Alter table items add column obj_type");
                foreach (var col in this.AtomicColumns)
                {
                    if (!dbCols.Contains(col.ColName))
                    {
                        connection.Exec("Alter table items add column "+col.ColName);
                    }
                }
            }
            
            public SQLInfo(BaseItem item) {
                this.ObjType = item.GetType().FullName;
                foreach (var property in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.GetCustomAttributes(typeof(PersistAttribute), true).Length > 0)
                .Where(p => p.GetGetMethod(true) != null && p.GetSetMethod(true) != null)) {
                    Type internalType = null;
                    if (property.PropertyType.Name == "Nullable`1")
                    {
                        var fullName = property.PropertyType.FullName;
                        if (fullName.Contains("Int32"))
                            internalType = typeof(int);
                        else if (fullName.Contains("Int64"))
                            internalType = typeof(Int64);
                        else if (fullName.Contains("Single"))
                            internalType = typeof(Single);
                        else if (fullName.Contains("Double"))
                            internalType = typeof(Double);
                        else internalType = property.PropertyType;
                    }
                    bool listType = IsListType(property.PropertyType);
                    if (listType)
                    {
                        internalType = property.PropertyType.GetGenericArguments()[0];
                    }
                    Columns.Add(new ColDef() { ColName = property.Name, ColType = property.PropertyType, InternalType = internalType, ListType = listType, MemberType = property.MemberType, PropertyInfo = property });
                }
                //Properties report all inherited ones but fields do not so we must iterate up the object tree to get them all...
                var type = item.GetType();
                Columns.AddRange(GetFields(type));
                type = type.BaseType;
                while (type != typeof(object) && type != null)
                {
                    Columns.AddRange(GetFields(type));
                    type = type.BaseType;
                }

            }

            private IEnumerable<ColDef> GetFields(Type t) {
                foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.GetCustomAttributes(typeof(PersistAttribute), true).Length > 0)) {
                    Type internalType = null;
                    if (field.FieldType.Name == "Nullable`1")
                    {
                        var fullName = field.FieldType.FullName;
                        if (fullName.Contains("Int32"))
                            internalType = typeof(int);
                        else if (fullName.Contains("Int64"))
                            internalType = typeof(Int64);
                        else if (fullName.Contains("Single"))
                            internalType = typeof(Single);
                        else if (fullName.Contains("Double"))
                            internalType = typeof(Double);
                        else internalType = field.FieldType;
                    }
                    bool listType = IsListType(field.FieldType);
                    if (listType)
                    {
                        internalType = field.FieldType.GetGenericArguments()[0];
                    }
                    yield return new ColDef() {ColName = field.Name, ColType = field.FieldType, InternalType = internalType, ListType = IsListType(field.FieldType), MemberType = field.MemberType, FieldInfo = field};
                }
            }

            private List<ColDef> _atomicColumns;
            public List<ColDef> AtomicColumns
            {
                get
                {
                    if (_atomicColumns == null) {
                        _atomicColumns = this.Columns.Where(c => !c.ListType).ToList();
                    }
                    return _atomicColumns;
                }
            }

            public List<ColDef> ListColumns
            {
                get
                {
                    return this.Columns.Where(c => c.ListType).ToList();
                }
            }

            private bool IsListType(Type t)
            {
                //return (t.Name.StartsWith("List") || t.Name.StartsWith("IList") || t.Name.StartsWith("IEnum"));
                return t.GetInterface("ICollection`1") != null;
            }

            private string _select;
            public string SelectStmt
            {
                get
                {
                    if (_select == null)
                    {
                        var stmt = new StringBuilder();
                        stmt.Append("select guid, ");
                        foreach (var col in AtomicColumns)
                        {
                            stmt.Append(col.ColName + ", ");
                        }
                        stmt.Remove(stmt.Length-2, 2); //remove last comma
                        stmt.Append(" from item ");
                        _select = stmt.ToString();
                    }
                    return _select;
                }
            }

            private string _update;
            public string UpdateStmt
            {
                get
                {
                    if (_update == null)
                    {
                        var stmt = new StringBuilder();
                        stmt.Append("replace into items (guid, obj_type, ");
                        int numCols = 2;
                        foreach (var col in Columns.Where(p => !p.ListType))
                        {
                            stmt.Append(col.ColName + ", ");
                            numCols++;
                        }
                        stmt.Remove(stmt.Length-2, 2); //remove last comma
                        //now values clause
                        stmt.Append(") values(");
                        for (int i = 0; i < numCols; i++)
                        {
                            stmt.Append("@"+i+", ");
                        }
                        stmt.Remove(stmt.Length - 2, 2);
                        stmt.Append(")");
                        _update = stmt.ToString();
                    }
                    return _update;
                }
            }
        }

        public static SqliteItemRepository GetRepository(string dbPath, string sqlitePath) {
            if (sqliteAssembly == null) {
                sqliteAssembly = System.Reflection.Assembly.LoadFile(sqlitePath);
                AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(SqliteResolver);
            }

            //SQLizer.Init(Path.Combine(ApplicationPaths.AppConfigPath, "ServiceStack.Text.dll"));

            return new SqliteItemRepository(dbPath);

        }


        // Used to save playstate 
        ItemRepository itemRepo; 

        private SqliteItemRepository(string dbPath) {

            SQLiteConnectionStringBuilder connectionstr = new SQLiteConnectionStringBuilder();
            connectionstr.PageSize = 4096;
            connectionstr.CacheSize = 4096;
            connectionstr.SyncMode = SynchronizationModes.Normal;
            connectionstr.DataSource = dbPath;
            connectionstr.JournalMode = SQLiteJournalModeEnum.Persist; //maybe better performance...?
            connection = new SQLiteConnection(connectionstr.ConnectionString);
            connection.Open();

            itemRepo = new ItemRepository();

            string playStateDBPath = Path.Combine(ApplicationPaths.AppUserSettingsPath, "playstate.db");

            string[] queries = {"create table if not exists provider_data (guid, full_name, data)",
                                "create unique index if not exists idx_provider on provider_data(guid, full_name)",
                                "create table if not exists items (guid primary key, data)",
                                "create table if not exists children (guid, child)", 
                                "create unique index if not exists idx_children on children(guid, child)",
                                "create table if not exists list_items(guid, property, value)",
                                "create index if not exists idx_list on list_items(guid, property)",
                                //"create table if not exists recent_list(top_parent, child, date_added)",
                                //"create index if not exists idx_recent on recent_list(top_parent, child)",
                                "attach database '"+playStateDBPath+"' as playstate_db",
                                "create table if not exists playstate_db.play_states (guid primary key, play_count, position_ticks, playlist_position, last_played)",
                                "pragma temp_store = memory"
                               // @"create table display_prefs (guid primary key, view_type, show_labels, vertical_scroll 
                               //        sort_order, index_by, use_banner, thumb_constraint_width, thumb_constraint_height, use_coverflow, use_backdrop )" 
                                //,   "create table play_states (guid primary key, play_count, position_ticks, playlist_position, last_played)"
                               };


            foreach (var query in queries) {
                try {

                    connection.Exec(query);
                } catch (Exception e) {
                    Logger.ReportInfo(e.ToString());
                }
            }


            alive = true; // tell writer to keep going
            Async.Queue("Sqlite Writer", DelayedWriter);

            if (Kernel.LoadContext == MBLoadContext.Service) //don't want to be migrating simultaneously in service and core...
            {
                MigratePlayState();
            }
        }

        private string GetPath(string type, string root) {
            string path = Path.Combine(root, type);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        private void MigrateData()
        {
            var guids = new List<Guid>();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "select guid from items";

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    guids.Add(reader.GetGuid(0));
                }
            }
            foreach (var id in guids)
            {
                var item = RetrieveItem(id);
                Logger.ReportVerbose("Migrating " + item.Name);
                SaveItem(item);
            }
        }


        private void MigratePlayState() {
            if (RetrievePlayState(MIGRATE_MARKER).Id != MIGRATE_MARKER) { //haven't migrated
                //delay this to allow playstate dictionary to load in old repo
                Async.Queue("Playstate Migration", () =>
                //using (ItemRepository repo = new ItemRepository())
                {
                    Logger.ReportInfo("Attempting to migrate playstates to SQL");
                    int cnt = 0;
                    foreach (PlaybackStatus ps in itemRepo.AllPlayStates)
                    {
                        //Logger.ReportVerbose("Saving playstate for " + ps.Id);
                        SavePlayState(ps);
                        cnt++;
                    }
                    Logger.ReportInfo("Successfully migrated " + cnt + " playstate items.");
                    var migrateMarker = new PlaybackStatus();
                    migrateMarker.Id = MIGRATE_MARKER;
                    SavePlayState(migrateMarker);
                }, 5000);
            }
        }

        public PlaybackStatus RetrievePlayState(Guid id) {
            var cmd = connection.CreateCommand();
            cmd.CommandText = "select guid, play_count, position_ticks, playlist_position, last_played from playstate_db.play_states where guid = @guid";
            cmd.AddParam("@guid", id);

            var state = new PlaybackStatus();
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    state.Id = reader.GetGuid(0);
                    state.PlayCount = reader.GetInt32(1);
                    state.PositionTicks = reader.GetInt64(2);
                    state.PlaylistPosition = reader.GetInt32(3);
                    state.LastPlayed = reader.GetDateTime(4);
                }
            }

            return state;
        }

        public ThumbSize RetrieveThumbSize(Guid id)
        {
            return itemRepo.RetrieveThumbSize(id);
        }

        public void SavePlayState(PlaybackStatus playState) {
            var cmd = connection.CreateCommand();
            cmd.CommandText = "replace into playstate_db.play_states(guid, play_count, position_ticks, playlist_position, last_played) values(@guid, @playCount, @positionTicks, @playlistPosition, @lastPlayed)";
            cmd.AddParam("@guid", playState.Id);
            cmd.AddParam("@playCount", playState.PlayCount);
            cmd.AddParam("@positionTicks", playState.PositionTicks);
            cmd.AddParam("@playlistPosition", playState.PlaylistPosition);
            cmd.AddParam("@lastPlayed", playState.LastPlayed);

            QueueCommand(cmd);
        }


        public void SaveChildren(Guid id, IEnumerable<Guid> children) {

            Guid[] childrenCopy;
            lock (children) {
                childrenCopy = children.ToArray();
            }

            var cmd = connection.CreateCommand();

            cmd.CommandText = "delete from children where guid = @guid";
            cmd.AddParam("@guid", id);

            QueueCommand(cmd);

            foreach (var guid in children) {
                cmd = connection.CreateCommand();
                cmd.AddParam("@guid", id);
                cmd.CommandText = "insert into children (guid, child) values (@guid, @child)";
                var childParam = cmd.Parameters.Add("@child", System.Data.DbType.Guid);

                childParam.Value = guid;
                QueueCommand(cmd);
            }
        }

        public IEnumerable<Guid> RetrieveChildrenOld(Guid id) {

            List<Guid> children = new List<Guid>();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "select child from children where guid = @guid";
            var guidParam = cmd.Parameters.Add("@guid", System.Data.DbType.Guid);
            guidParam.Value = id;

            using (var reader = cmd.ExecuteReader()) {
                while (reader.Read()) {
                    children.Add(reader.GetGuid(0));
                }
            }

            return children.Count == 0 ? null : children;
        }

        const string UNKNOWN = "<Unknown>";

        public List<BaseItem> RetrieveIndex(Folder folder, string property, Func<string, BaseItem> constructor)
        {
            List<BaseItem> index = new List<BaseItem>();
            var cmd = connection.CreateCommand();
            //we'll build the unknown items as we go through the children the first time
            List<BaseItem> unknownItems = new List<BaseItem>();

            //create a temporary table of this folder's recursive children to use in the retrievals
            string tableName = "[" + folder.Id.ToString().Replace("-", "") + "_" + property + "]";
            if (connection.TableExists(tableName))
            {
                connection.Exec("delete from " + tableName);
            }
            else
            {
                connection.Exec("create temporary table if not exists " + tableName + "(child)");
            }

            bool allowEpisodes = property == "Year"; //only go to episode level for year index

            cmd.CommandText = "Insert into " + tableName + " (child) values(@1)";
            var childParam = cmd.Parameters.Add("@1", DbType.Guid);
            SQLInfo.ColDef col = new SQLInfo.ColDef();
            Type currentType = null;

            lock (connection)
            {
                var tran = connection.BeginTransaction();

                foreach (var child in folder.RecursiveChildren)
                {
                    if (child is IShow && !(child is Season)) // && (allowEpisodes && !(child is Series)) || (!allowEpisodes && !(child is Episode)))
                    {
                        //determine if property has any value
                        if (col.ColType != child.GetType()) {
                            currentType = child.GetType();
                            col = ItemSQL[child.GetType()].Columns.Find(c => c.ColName == property);
                        }
                        object data = null;
                        if (col.MemberType == MemberTypes.Property)
                        {
                            data = col.PropertyInfo.GetValue(child, null);
                        }
                        else if (col.MemberType == MemberTypes.Field)
                        {
                            data = col.FieldInfo.GetValue(child);
                        }
                        if (data != null) //only save children with data
                        {
                            //Logger.ReportInfo("Adding child " + child.Name + " to temp table");
                            childParam.Value = child.Id;
                            cmd.ExecuteNonQuery();
                        }
                        else
                        {
                            //just as fast to do this now...
                            unknownItems.Add(child);
                        }
                    }
                }
                tran.Commit();
            }


            //create our Unknown Index
            index.Add(new Index(constructor(UNKNOWN), unknownItems));

            //now retrieve the values for the main indicies
            cmd = connection.CreateCommand(); //new command
            property = property == "Actors" ? "ActorName" : property; //re-map to our name entry

            if (col.ListType)
            {
                //need to get values from list table
                cmd.CommandText = "select distinct value from list_items where property = '" + property + "' and guid in (select child from " + tableName + ") order by value";
            }
            else
            {
                cmd.CommandText = "select distinct " + property + " where guid in (select child from " + tableName + ") order by " + property;
            }

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    //Logger.ReportInfo("Creating index " + reader[0].ToString() + " on " + folder.Name);
                    index.Add(new Index(constructor(reader[0].ToString()), new List<BaseItem>()));
                }
            }
            return index;
        }

        public void FillSubIndexes(Folder folder, IList<BaseItem> children, string property)
        {
            //convert list to dictionary for easy access
            Dictionary<string, IList<BaseItem>> index = new Dictionary<string, IList<BaseItem>>();
            foreach (var item in children)
            {
                index[item.Name] = (item as Index).Children;
                item.ReCacheAllImages(null);
            }

            SQLInfo.ColDef col = new SQLInfo.ColDef();
            Type currentType = null;

                foreach (var child in folder.RecursiveChildren)
                {
                    if (child is IShow && !(child is Season)) // && (allowEpisodes && !(child is Series)) || (!allowEpisodes && !(child is Episode)))
                    {
                        //get the value of the property we are indexing
                        if (col.ColType != child.GetType()) {
                            currentType = child.GetType();
                            col = ItemSQL[child.GetType()].Columns.Find(c => c.ColName == property);
                        }
                        object data = null;
                        //Logger.ReportInfo("Column is: "+col.ColName+"/"+col.ColType);
                        //Logger.ReportInfo("Child is: " + child.Name);
                        if (col.MemberType == MemberTypes.Property)
                        {
                            data = col.PropertyInfo.GetValue(child, null);
                        }
                        else if (col.MemberType == MemberTypes.Field)
                        {
                            data = col.FieldInfo.GetValue(child);
                        }
                        if (data != null)
                        {
                            if (col.ListType)
                            {
                                //go through the list
                                System.Collections.ICollection list = data as System.Collections.ICollection;
                                foreach (var item in list)
                                {
                                    if (item is Actor)
                                    {
                                        //need special handling here - all others should be "stringable"
                                        AddItemToIndex(index, (item as Actor).Name, child);
                                    }
                                    else
                                    {
                                        AddItemToIndex(index, item.ToString(), child);
                                    }
                                }
                            }
                            else
                            {
                                //not list
                                AddItemToIndex(index, data.ToString(), child);
                            }
                        }
                        else
                        {
                            //add to Unknown
                            AddItemToIndex(index, UNKNOWN, child);
                        }
                    }
            }
        }

        private void AddItemToIndex(Dictionary<string, IList<BaseItem>> index, string value, BaseItem child)
        {
            IList<BaseItem> subItems;
            if (!index.TryGetValue(value, out subItems))
            {
                subItems = new List<BaseItem>();
                index[value] = subItems;
            }
            if (child is Episode)
            {
                //we want to group these by series - find or create a series head
                Episode episode = child as Episode;
                Folder currentSeries = episode.Parent is IndexFolder ? episode.Parent : episode.Series; //may already be indexed
                if (currentSeries == null)
                {
                    //bad tv format - no series associated create one
                    currentSeries = new Series()
                    {
                        Name = "<Unknown>"
                    };
                }
                IndexFolder series = null; // (IndexFolder)index[value].Find(i => i.Id == (value + currentSeries.Name).GetMD5());
                if (series == null)
                {
                    series = new IndexFolder()
                    {
                        Id = (value + currentSeries.Name).GetMD5(),
                        Name = currentSeries.Name,
                        Overview = currentSeries.Overview,
                        PrimaryImagePath = currentSeries.PrimaryImagePath,
                        SecondaryImagePath = currentSeries.SecondaryImagePath,
                        BannerImagePath = currentSeries.BannerImagePath,
                        BackdropImagePaths = currentSeries.BackdropImagePaths
                    };
                    index[value].Add(series);
                }
                series.AddChild(episode);
            }
            else
            {
                subItems.Add(child);
            }
        }

        public IEnumerable<BaseItem> RetrieveChildren(Guid id) {

            List<BaseItem> children = new List<BaseItem>();

            if (!Kernel.UseNewSQLRepo)
            {
                var cached = RetrieveChildrenOld(id);
                if (cached != null)
                {
                    foreach (var guid in cached)
                    {
                        var item = RetrieveItem(guid);
                        if (item != null)
                        {
                            children.Add(item);
                        }
                    }
                }
            }
            else
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = "select * from items where guid in (select child from children where guid = @guid)";
                var guidParam = cmd.Parameters.Add("@guid", System.Data.DbType.Guid);
                guidParam.Value = id;

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        children.Add(GetItem(reader, (string)reader["obj_type"]));
                    }
                }
            }
            return children.Count == 0 ? null : children;
        }

        public DisplayPreferences RetrieveDisplayPreferences(DisplayPreferences dp) {
            return itemRepo.RetrieveDisplayPreferences(dp);
        }


        public void SaveDisplayPreferences(DisplayPreferences prefs) {
            itemRepo.SaveDisplayPreferences(prefs);
        }

        public BaseItem RetrieveItem(Guid id) {

            BaseItem item = null;
            //using (new MediaBrowser.Util.Profiler("===========RetrieveItem============="))
            {
                if (!Kernel.UseNewSQLRepo)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "select data from items where guid = @guid";
                    cmd.AddParam("@guid", id);


                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var data = reader.GetBytes(0);
                            using (var stream = new MemoryStream(data))
                            {
                                item = Serializer.Deserialize<BaseItem>(stream);
                            }
                        }
                    }
                }
                else
                {
                    //test
                    var cmd2 = connection.CreateCommand();
                    cmd2.CommandText = "select * from items where guid = @guid";
                    cmd2.AddParam("@guid", id);
                    using (var reader = cmd2.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string itemType = reader["obj_type"].ToString();

                            if (!string.IsNullOrEmpty(itemType))
                            {
                                item = GetItem(reader, itemType);
                            }
                        }
                    }
                }
            }
            
            //
            //Logger.ReportInfo("Item: " + item.Name);
            return item;
        }

        protected BaseItem GetItem(SQLiteDataReader reader, string itemType)
        {
            BaseItem item = null;
            try
            {
                item = Serializer.Instantiate<BaseItem>(itemType);
            }
            catch (Exception e)
            {
                Logger.ReportException("Error trying to create instance of type: " + itemType, e);
                return null;
            }
            SQLInfo itemSQL;
            if (!ItemSQL.TryGetValue(item.GetType(), out itemSQL))
            {
                itemSQL = new SQLInfo(item);
                ItemSQL.Add(item.GetType(), itemSQL);
                //make sure our schema matches
                itemSQL.FixUpSchema(connection);
            }
            foreach (var col in itemSQL.AtomicColumns)
            {
                var data = SQLizer.Extract(reader, col);
                if (data != null)
                    if (col.MemberType == MemberTypes.Property)
                        col.PropertyInfo.SetValue(item, data, null);
                    else
                        col.FieldInfo.SetValue(item, data);

            }
            // and our list columns
            //this is an optimization - we go get all the list values for this item in one statement
            var listCmd = connection.CreateCommand();
            listCmd.CommandText = "select property, value from list_items where guid = @guid and property != 'ActorName' order by property";
            listCmd.AddParam("@guid", item.Id);
            string currentProperty = "";
            System.Collections.IList list = null;
            SQLInfo.ColDef column = new SQLInfo.ColDef();
            using (var listReader = listCmd.ExecuteReader())
            {
                while (listReader.Read())
                {
                    string property = listReader.GetString(0);
                    if (property != currentProperty)
                    {
                        //new column...
                        if (list != null)
                        {
                            //fill in the last one
                            if (column.MemberType == MemberTypes.Property)
                                column.PropertyInfo.SetValue(item, list, null);
                            else
                                column.FieldInfo.SetValue(item, list);
                        }
                        currentProperty = property;
                        column = itemSQL.Columns.Find(c => c.ColName == property);
                        list = (System.Collections.IList)column.ColType.GetConstructor(new Type[] { }).Invoke(null);
                        //Logger.ReportVerbose("Added list item '" + listReader[0] + "' to " + col.ColName);
                    }
                    list.Add(SQLizer.Extract(listReader, new SQLInfo.ColDef() { ColName = "value", ColType = column.InternalType }));
                }
                if (list != null)
                {
                    //fill in the last one
                    if (column.MemberType == MemberTypes.Property)
                        column.PropertyInfo.SetValue(item, list, null);
                    else
                        column.FieldInfo.SetValue(item, list);
                }
            }
            return item;
        }

        public void SaveItem(BaseItem item)
        {
            if (item == null) return;

            //using (new MediaBrowser.Util.Profiler("==== Save Item: " + item.Name))
            {
                if (!Kernel.UseNewSQLRepo)
                {
                    using (var fs = new MemoryStream())
                    {
                        BinaryWriter bw = new BinaryWriter(fs);
                        Serializer.Serialize(bw.BaseStream, item);

                        var cmd = connection.CreateCommand();
                        cmd.CommandText = "replace into items(guid, data) values (@guid, @data)";

                        SQLiteParameter guidParam = new SQLiteParameter("@guid");
                        SQLiteParameter dataParam = new SQLiteParameter("@data");

                        cmd.Parameters.Add(guidParam);
                        cmd.Parameters.Add(dataParam);

                        guidParam.Value = item.Id;
                        dataParam.Value = fs.ToArray();

                        QueueCommand(cmd);
                    }
                }
                else
                {
                    //test
                    if (!ItemSQL.ContainsKey(item.GetType()))
                    {
                        ItemSQL.Add(item.GetType(), new SQLInfo(item));
                        //make sure our schema matches
                        ItemSQL[item.GetType()].FixUpSchema(connection);
                    }
                    var cmd2 = connection.CreateCommand();
                    cmd2.CommandText = ItemSQL[item.GetType()].UpdateStmt;


                    cmd2.AddParam("@0", item.Id);
                    cmd2.AddParam("@1", item.GetType().FullName);
                    int colNo = 2; //id was 0 type was 1...
                    foreach (var col in ItemSQL[item.GetType()].AtomicColumns)
                    {
                        if (col.MemberType == MemberTypes.Property)
                            cmd2.AddParam("@" + colNo, SQLizer.Encode(col, col.PropertyInfo.GetValue(item, null)));
                        else
                            cmd2.AddParam("@" + colNo, SQLizer.Encode(col, col.FieldInfo.GetValue(item)));
                        colNo++;
                    }
                    QueueCommand(cmd2);

                    //and now each of our list members
                    //lock (connection)
                    {
                        //var tran = connection.BeginTransaction(); //more for performance than consistency...
                        var delCmd = connection.CreateCommand();
                        delCmd.CommandText = "delete from list_items where guid = @guid";
                        delCmd.AddParam("@guid", item.Id);
                        delCmd.ExecuteNonQuery();
                        foreach (var col in ItemSQL[item.GetType()].ListColumns)
                        {
                            System.Collections.IEnumerable list = null;

                            if (col.MemberType == MemberTypes.Property)
                            {
                                //var it = col.PropertyInfo.GetValue(item, null);
                                //Type ittype = it.GetType();
                                list = col.PropertyInfo.GetValue(item, null) as System.Collections.IEnumerable;
                            }
                            else
                                list = col.FieldInfo.GetValue(item) as System.Collections.IEnumerable;

                            if (list != null)
                            {
                                var insCmd = connection.CreateCommand();
                                insCmd.CommandText = "insert into list_items(guid, property, value) values(@guid, @property, @value)";
                                insCmd.AddParam("@guid", item.Id);
                                insCmd.AddParam("@property", col.ColName);
                                SQLiteParameter val = new SQLiteParameter("@value");
                                insCmd.Parameters.Add(val);

                                //special handling for actors because they are saved serialized - we also need to save them in a query-able form...
                                var insActorCmd = connection.CreateCommand();
                                bool isActor = col.InternalType == typeof(Actor);
                                SQLiteParameter val2 = new SQLiteParameter("@value2");
                                if (isActor)
                                {
                                    insActorCmd.CommandText = "insert into list_items(guid, property, value) values(@guid, 'ActorName', @value2)";
                                    insActorCmd.AddParam("@guid", item.Id);
                                    insActorCmd.Parameters.Add(val2);
                                }

                                foreach (var listItem in list)
                                {
                                    val.Value = SQLizer.Encode(new SQLInfo.ColDef() { ColType = col.InternalType, InternalType = listItem.GetType() }, listItem);
                                    insCmd.ExecuteNonQuery();
                                    if (isActor)
                                    {
                                        val2.Value = (listItem as Actor).Name.Trim();
                                        insActorCmd.ExecuteNonQuery();
                                    }
                                }
                            }
                        }
                        //tran.Commit();
                    }
                    //finally, update the recent list
                    //if (item is Media) //don't need to track non-media items
                    //{
                    //    var recCmd = connection.CreateCommand();
                    //    recCmd.CommandText = "replace into recent_list(top_parent, child, date_added) values(@top, @child, @date)";
                    //    recCmd.AddParam("@top", item.TopParent);
                    //    recCmd.AddParam("@child", item.Id);
                    //    recCmd.AddParam("@date", item.DateCreated);
                    //    recCmd.ExecuteNonQuery();
                    //}
                }

                //
            }
        }

        /// <summary>
        /// Generic routine to retrieve a list of items
        /// </summary>
        /// <param name="selectStmt"></param>
        /// <param name="parms"></param>
        /// <returns></returns>
        private IEnumerable<BaseItem> GetItems(string selectStmt, SQLiteParameter[] parms)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = selectStmt;
            cmd.Parameters.AddRange(parms);
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    string itemType = reader["obj_type"].ToString();

                    if (!string.IsNullOrEmpty(itemType))
                    {
                        yield return GetItem(reader, itemType);
                    }
                }
            }
        }

        public IEnumerable<IMetadataProvider> RetrieveProviders(Guid guid) {
            var providers = new List<IMetadataProvider>();

            var cmd = connection.CreateCommand();
            cmd.CommandText = "select data from provider_data where guid = @guid";
            var guidParam = cmd.Parameters.Add("@guid", System.Data.DbType.Guid);
            guidParam.Value = guid;

            using (var reader = cmd.ExecuteReader()) {
                while (reader.Read()) {
                    using (var ms = new MemoryStream(reader.GetBytes(0))) {

                        var data = (IMetadataProvider)Serializer.Deserialize<object>(ms);
                        providers.Add(data);
                    }
                }
            }

            return providers.Count == 0 ? null : providers;
        }

        public void SaveProviders(Guid guid, IEnumerable<IMetadataProvider> providers) {

            IMetadataProvider[] providerCopy;
            lock (providers) {
                providerCopy = providers.ToArray();
            }
            lock (delayedCommands) {
                var cmd = connection.CreateCommand();

                cmd.CommandText = "delete from provider_data where guid = @guid";
                cmd.AddParam("@guid", guid);
                QueueCommand(cmd);

                foreach (var provider in providerCopy) {
                    cmd = connection.CreateCommand();
                    cmd.CommandText = "insert into provider_data (guid, full_name, data) values (@guid, @full_name, @data)";
                    cmd.AddParam("@guid", guid);
                    cmd.AddParam("@full_name", provider.GetType().FullName);
                    var dataParam = cmd.AddParam("@data");


                    using (var ms = new MemoryStream()) {
                        Serializer.Serialize(ms, (object)provider);
                        dataParam.Value = ms.ToArray();
                        QueueCommand(cmd);
                    }
                }
            }
        }

        public bool ClearEntireCache() {
            lock (connection) {
                var tran = connection.BeginTransaction();
                connection.Exec("delete from provider_data"); 
                connection.Exec("delete from items");
                connection.Exec("delete from children");
                connection.Exec("delete from list_items");
                //connection.Exec("delete from display_prefs");
                // People will get annoyed if this is lost
                // connection.Exec("delete from play_states");
                tran.Commit(); 
                connection.Exec("vacuum");
            }

            return true;
        }

    }
}
