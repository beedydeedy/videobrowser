﻿using System;
using System.Collections.Generic;
using System.Collections;
using Microsoft.MediaCenter.UI;
using System.Diagnostics;
using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Code;
using System.Threading;
using System.Reflection;
using MediaBrowser.Library.Extensions;
using MediaBrowser.Library.Threading;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Metadata;


namespace MediaBrowser.Library {

    public class FolderModel : Item {

        int jilShift = -1;
        int selectedchildIndex = -1;
        object itemLoadLock = new object();
        DisplayPreferences displayPrefs;
        SizeRef actualThumbSize = new SizeRef(new Size(1, 1));
        FolderChildren folderChildren = new FolderChildren();
        Folder folder;

        #region Folder construction 

        public FolderModel() {
        }

        internal override void Assign(BaseItem baseItem ) { 
            base.Assign(baseItem);
            folder = (MediaBrowser.Library.Entities.Folder)baseItem;
            folderChildren.Assign(this, FireChildrenChangedEvents);
        }

        #endregion

        public Folder Folder {
            get {
                return folder;
            }
        }

        public override void NavigatingInto() {
            // force display prefs to reload.
            displayPrefs = null;

            // metadata should be refreshed in a higher priority
            if (Config.Instance.AutoValidate) folderChildren.RefreshAsap();

            base.NavigatingInto();
        }


        public override int UnwatchedCount {
            get {
                if (unwatchedCountCache == -1) {
                    unwatchedCountCache = 0;
                    Async.Queue("Unwatched Counter", () =>
                    { 
                        unwatchedCountCache = folder.UnwatchedCount;
                        FireWatchedChangedEvents();
                    });
                }
                return unwatchedCountCache;
            }
        }

        public int FirstUnwatchedIndex {
            get {
                if (Config.Instance.DefaultToFirstUnwatched) {
                    lock (this.Children)
                        for (int i = 0; i < this.Children.Count; ++i)
                            if (!this.Children[i].HaveWatched)
                                return i;

                }
                return 0;
            }
        }

        public override bool ShowNewestItems {
            get {
                return string.IsNullOrEmpty(BaseItem.Overview);
            }
        }

        public override List<Item> QuickListItems {
            get {
                if (Application.CurrentInstance.RecentItemOption == "watched") {
                    return RecentItems;
                }
                else if (Application.CurrentInstance.RecentItemOption == "unwatched")
                {
                    return UnwatchedItems;
                }
                else
                {
                    return NewestItems;
                }
            }
            set
            {
                if (Application.CurrentInstance.RecentItemOption == "watched")
                {
                    recentWatchedItems = null;
                    var ignore = RecentItems;
                }
                else if (Application.CurrentInstance.RecentItemOption == "unwatched")
                {
                    recentWatchedItems = null;
                    var ignore = UnwatchedItems;
                }
                else
                {
                    newestItems = null;
                    var ignore = NewestItems;
                }

            }
        }

        public List<Item> RecentItems
        {
            get
            {
                //only want items from non-protected folders
                if (folder != null && folder.ParentalAllowed)
                {
                    return GetRecentWatchedItems(Config.Instance.RecentItemCount);
                } else {
                    return new List<Item>(); //return empty list if folder is protected
                }

            }
        }

        public List<Item> NewestItems {
            get {
                if (folder != null && folder.ParentalAllowed) {
                    return GetNewestItems(Config.Instance.RecentItemCount);
                } else {
                    return new List<Item>(); //return empty list if folder is protected
                }
            }
        }

        public List<Item> UnwatchedItems
        {
            get
            {
                //only want items from non-protected folders
                if (folder != null && folder.ParentalAllowed)
                {
                    return GetRecentUnwatchedItems(Config.Instance.RecentItemCount);
                }
                else
                {
                    return new List<Item>(); //return empty list if folder is protected
                }

            }
        }
        
        List<Item> newestItems = null; 
        public List<Item> GetNewestItems(int count) {
            if (newestItems == null) {
                newestItems = new List<Item>();
                if (folder != null) {
                    Async.Queue("Newest Item Loader", () =>
                    {
                        var items = new SortedList<DateTime, Item>();
                        FindNewestChildren(folder, items, count);
                        newestItems = items.Values.Select(i => i).Reverse().ToList();
                        Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ =>
                        {
                            FirePropertyChanged("NewestItems");
                            FirePropertyChanged("QuickListItems");
                        });
                    }, null, true);
                }
            }
            return newestItems;
        }

        List<Item> recentWatchedItems = null;
        public List<Item> GetRecentWatchedItems(int count)
        {
            if (recentWatchedItems == null)
            {
                recentWatchedItems = new List<Item>();
                if (folder != null)
                {
                    Async.Queue("Recent Watched Loader", () =>
                    {
                        var items = new SortedList<DateTime, Item>();
                        FindRecentWatchedChildren(folder, items, count);
                        recentWatchedItems = items.Values.Select(i => i).Reverse().ToList();
                        Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ =>
                        {
                            FirePropertyChanged("RecentItems");
                            FirePropertyChanged("QuickListItems");
                        });
                    },null, true);
                }
            }
            return recentWatchedItems;
        }

        List<Item> recentUnwatchedItems = null;
        public List<Item> GetRecentUnwatchedItems(int count)
        {
            if (recentUnwatchedItems == null)
            {
                recentUnwatchedItems = new List<Item>();
                if (folder != null)
                {
                    Async.Queue("Recent Watched Loader", () =>
                    {
                        var items = new SortedList<DateTime, Item>();
                        FindRecentUnwatchedChildren(folder, items, count);
                        recentUnwatchedItems = items.Values.Select(i => i).Reverse().ToList();
                        Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ =>
                        {
                            FirePropertyChanged("RecentItems");
                            FirePropertyChanged("QuickListItems");
                        });
                    }, null, true);
                }
            }
            return recentUnwatchedItems;
        }

        public void AddNewlyWatched(Item item)
        {
            //called when we watch something so add to top of list (this way we don't have to re-build whole thing)
            if (item.ParentalAllowed || !Config.Instance.HideParentalDisAllowed)
            {
                if (recentWatchedItems != null) //already have a list
                {
                    //first we need to remove ourselves if we're already in the list (can't search with item cuz we were cloned)
                    Item us = recentWatchedItems.Find(i => i.Id == item.Id);
                    if (us != null)
                    {
                        recentWatchedItems.Remove(us);
                    }
                    //then add at the top and tell the UI to update
                    recentWatchedItems.Insert(0, item);
                }
                else
                { //need to build a list - we will get added automatically
                    GetRecentWatchedItems(Config.Instance.RecentItemCount);
                }
                FirePropertyChanged("RecentItems");
                FirePropertyChanged("QuickListItems");
            }
        }

        public void RemoveNewlyWatched(Item item)
        {
            //called when we clear the watched status manually (this way we don't have to re-build whole thing)
            if (recentWatchedItems != null) // have a list
            {
                Item us = recentWatchedItems.Find(i => i.Id == item.Id);
                if (us != null)
                {
                    recentWatchedItems.Remove(us);
                    FirePropertyChanged("RecentItems");
                    FirePropertyChanged("QuickListItems");
                }
            }
            if (recentUnwatchedItems != null) // have a list
            {
                Item us = recentUnwatchedItems.Find(i => i.Id == item.Id);
                if (us != null)
                {
                    recentUnwatchedItems.Remove(us);
                    FirePropertyChanged("UnwatchedItems");
                    FirePropertyChanged("QuickListItems");
                }
            }
        }

        public void RemoveRecentlyUnwatched(Item item)
        {
            //called when watched status set manually (this way we don't have to re-build whole thing)
            if (recentUnwatchedItems != null) // have a list
            {
                Item us = recentUnwatchedItems.Find(i => i.Id == item.Id);
                if (us != null)
                {
                    recentUnwatchedItems.Remove(us);
                    FirePropertyChanged("UnwatchedItems");
                    FirePropertyChanged("QuickListItems");
                }
            }
        }

        string folderOverviewCache = null;
        public override string Overview {
            get {
                var overview = base.Overview;
                if (String.IsNullOrEmpty(overview)) {

                    if (folderOverviewCache != null) {
                        return folderOverviewCache;
                    }

                    folderOverviewCache = "";

                    Async.Queue("Overview Loader", () =>
                    {
                        RefreshFolderOverviewCache();
                        Microsoft.MediaCenter.UI.Application.DeferredInvoke( _ => {
                            FirePropertyChanged("Overview");
                        });
                    },null, true);
                  
                }
                return overview;
            }
        }

        private void RefreshFolderOverviewCache() {
            //produce list sorted by episode number if we are a TV season
            if (this.BaseItem is Season)
            {
                int unknown = 9999; //use this for episodes that don't have episode number
                var items = new SortedList<int, BaseItem>();
                foreach (BaseItem i in this.Folder.Children) {
                    Episode ep = i as Episode;
                    if (ep != null)
                    {
                        int epNum;
                        try
                        {
                            epNum = Convert.ToInt32(ep.EpisodeNumber);
                        }
                        catch { epNum = unknown++; }
                        try
                        {
                            items.Add(epNum, ep);
                        } catch {
                            //probably more than one episode coming up as "0"
                            items.Add(unknown++, ep);
                        }
                    }
                }
                folderOverviewCache = string.Join("\n", items.Select(i => (i.Value.Name)).ToArray());
            }
            else // normal folder
            {
                var items = new SortedList<DateTime, Item>();
                FindNewestChildren(folder, items, 20, -1);
                folderOverviewCache = string.Join("\n", items.Reverse()
                    .Select(i => (this.BaseItem.GetType() == typeof(Season) ? i.Value.Name : i.Value.LongName))
                    .ToArray());
            }
        }

        public void FindNewestChildren(Folder folder, SortedList<DateTime, Item> foundNames, int maxSize)
        {
            FindNewestChildren(folder, foundNames, maxSize, Config.Instance.RecentItemDays);
        }

        public void FindNewestChildren(Folder folder, SortedList<DateTime, Item> foundNames, int maxSize, int maxDays) 
        {
            //using (new MediaBrowser.Util.Profiler("=== Recently Added for "+this.Name))
            {
                DateTime daysAgo = DateTime.Now.Subtract(DateTime.Now.Subtract(DateTime.Now.AddDays(-maxDays)));
                HashSet<Guid> foundIds = new HashSet<Guid>();
                foreach (var item in folder.Children)
                {
                    // recurse folders
                    if (item is Folder)
                    {
                        //don't return items inside protected folders
                        if (item.ParentalAllowed)
                        {
                            if (item is IContainer && !(item is Season) && !foundIds.Contains((folder.Name + item.Name).GetMD5()))
                            {
                                //collapse series in the list
                                bool containsSeasons = false;
                                SortedList<DateTime, Item> subItems = new SortedList<DateTime, Item>();
                                FindNewestChildren(item as Folder, subItems, maxSize);
                                if (item is Series)
                                {
                                    //we need to go another level into series to get actual items
                                    foreach (var seriesChild in (item as Folder).Children)
                                    {
                                        if (seriesChild is Season)
                                        {
                                            SortedList<DateTime, Item> episodes = new SortedList<DateTime, Item>();
                                            FindNewestChildren(seriesChild as Folder, episodes, maxSize);
                                            if (episodes.Count >= Config.Instance.RecentItemCollapseThresh)
                                            {
                                                //collapse into a season
                                                var thisContainer = seriesChild as Season;
                                                var ignore = seriesChild.BackdropImages; //force these to load so they will inherit
                                                DateTime createdTime = episodes.Keys.Max();
                                                var container = new IndexFolder()
                                                {
                                                    Id = (item.Name + thisContainer.Name).GetMD5(),
                                                    DateCreated = createdTime,
                                                    Name = thisContainer.Name + " (" + episodes.Count + " items)",
                                                    Overview = thisContainer.Overview,
                                                    MpaaRating = thisContainer.MpaaRating,
                                                    Genres = thisContainer.Genres,
                                                    ImdbRating = thisContainer.ImdbRating,
                                                    Studios = thisContainer.Studios,
                                                    PrimaryImagePath = thisContainer.PrimaryImagePath,
                                                    SecondaryImagePath = thisContainer.SecondaryImagePath,
                                                    BannerImagePath = thisContainer.BannerImagePath,
                                                    BackdropImagePaths = thisContainer.BackdropImagePaths,
                                                    DisplayMediaType = thisContainer.DisplayMediaType,
                                                    Parent = folder
                                                };

                                                foreach (var pair in episodes)
                                                {
                                                    container.AddChild(pair.Value.BaseItem);
                                                }
                                                var containerModel = ItemFactory.Instance.Create(container);
                                                containerModel.PhysicalParent = this;
                                                while (subItems.ContainsKey(createdTime))
                                                {
                                                    // break ties 
                                                    createdTime = createdTime.AddMilliseconds(1);
                                                }
                                                subItems.Add(createdTime, containerModel);
                                                containsSeasons = true;
                                            }
                                            else
                                            {
                                                foreach (var pair in episodes)
                                                {
                                                    subItems.Add(pair.Key, pair.Value);
                                                }
                                            }
                                        }
                                    }
                                }
                                if (subItems.Count >= Config.Instance.RecentItemCollapseThresh || (containsSeasons && subItems.Count > 0)) // always roll series so seasons don't end up on main list
                                {
                                    //collapse into a container
                                    var thisContainer = item as IContainer;
                                    var ignore = item.BackdropImages; //force these to load so they will inherit
                                    DateTime createdTime = subItems.Keys.Max();
                                    var container = new IndexFolder()
                                    {
                                        Id = (folder.Name + thisContainer.Name).GetMD5(),
                                        DateCreated = createdTime,
                                        Name = thisContainer.Name + " (" + subItems.Count + " items)",
                                        Overview = thisContainer.Overview,
                                        MpaaRating = thisContainer.MpaaRating,
                                        Genres = thisContainer.Genres,
                                        ImdbRating = thisContainer.ImdbRating,
                                        Studios = thisContainer.Studios,
                                        PrimaryImagePath = thisContainer.PrimaryImagePath,
                                        SecondaryImagePath = thisContainer.SecondaryImagePath,
                                        BannerImagePath = thisContainer.BannerImagePath,
                                        BackdropImagePaths = thisContainer.BackdropImagePaths,
                                        DisplayMediaType = thisContainer.DisplayMediaType,
                                        Parent = folder
                                    };

                                    foreach (var pair in subItems)
                                    {
                                        container.AddChild(pair.Value.BaseItem);
                                    }
                                    var containerModel = ItemFactory.Instance.Create(container);
                                    containerModel.PhysicalParent = this;
                                    while (foundNames.ContainsKey(createdTime))
                                    {
                                        // break ties 
                                        createdTime = createdTime.AddMilliseconds(1);
                                    }
                                    foundNames.Add(createdTime, containerModel);
                                    foundIds.Add(container.Id);
                                }
                                else
                                {
                                    foreach (var pair in subItems)
                                    {
                                        if (!foundIds.Contains(pair.Value.Id))
                                        {
                                            var key = pair.Key;
                                            while (foundNames.ContainsKey(key))
                                            {
                                                // break ties 
                                                key = key.AddMilliseconds(1);
                                            }
                                            foundNames.Add(key, pair.Value);
                                            foundIds.Add(pair.Value.Id);
                                            if (foundNames.Count >= maxSize)
                                            {
                                                foundNames.RemoveAt(0);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (!(item is Series))
                                {
                                    //regular folder or box set - go into it
                                    SortedList<DateTime, Item> subItems = new SortedList<DateTime, Item>();
                                    FindNewestChildren(item as Folder, subItems, maxSize);
                                    foreach (var pair in subItems)
                                    {
                                        if (!foundIds.Contains(pair.Value.Id)) //avoid dups
                                        {
                                            var key = pair.Key;
                                            while (foundNames.ContainsKey(key))
                                            {
                                                // break ties 
                                                key = key.AddMilliseconds(1);
                                            }
                                            foundNames.Add(key, pair.Value);
                                            foundIds.Add(pair.Value.Id);
                                            if (foundNames.Count >= maxSize)
                                            {
                                                foundNames.RemoveAt(0);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (!foundIds.Contains(item.Id)) //avoid dups
                        {
                            DateTime creationTime = item.DateCreated;
                            //only if added less than specified ago
                            if (maxDays == -1 || DateTime.Compare(creationTime, daysAgo) > 0)
                            {
                                while (foundNames.ContainsKey(creationTime))
                                {
                                    // break ties 
                                    creationTime = creationTime.AddMilliseconds(1);
                                }
                                Item modelItem = ItemFactory.Instance.Create(item);
                                modelItem.PhysicalParent = this;
                                var ignore = item.Parent != null ? item.Parent.BackdropImages : null; //force these to load so they will inherit
                                ignore = item.BackdropImages;
                                foundNames.Add(creationTime, modelItem);
                                foundIds.Add(item.Id);
                                if (foundNames.Count >= maxSize)
                                {
                                    foundNames.RemoveAt(0);
                                }
                            }
                        }
                    }
                }
            }
        }

        public void FindRecentWatchedChildren(Folder folder, SortedList<DateTime, Item> foundNames, int maxSize)
        {
            //using (new MediaBrowser.Util.Profiler("=== Recently Watched for "+this.Name))
            {
                DateTime daysAgo = DateTime.Now.Subtract(DateTime.Now.Subtract(DateTime.Now.AddDays(-Config.Instance.RecentItemDays)));
                HashSet<Guid> foundIds = new HashSet<Guid>();
                foreach (var item in folder.Children)
                {
                    // recurse folders
                    if (item is Folder)
                    {
                        //don't return items inside protected folders
                        if (item.ParentalAllowed)
                        {
                            if (item is IContainer && !(item is Season) && !foundIds.Contains((folder.Name + item.Name).GetMD5()))
                            {
                                //collapse series in the list
                                bool containsSeasons = false;
                                SortedList<DateTime, Item> subItems = new SortedList<DateTime, Item>();
                                FindRecentWatchedChildren(item as Folder, subItems, maxSize);
                                if (item is Series)
                                {
                                    //we need to go another level into series to get actual items
                                    foreach (var seriesChild in (item as Folder).Children)
                                    {
                                        if (seriesChild is Season)
                                        {
                                            SortedList<DateTime, Item> episodes = new SortedList<DateTime, Item>();
                                            FindRecentWatchedChildren(seriesChild as Folder, episodes, maxSize);
                                            if (episodes.Count >= Config.Instance.RecentItemCollapseThresh)
                                            {
                                                //collapse into a season
                                                var thisContainer = seriesChild as Season;
                                                var ignore = seriesChild.BackdropImages; //force these to load so they will inherit
                                                DateTime createdTime = episodes.Keys.Max();
                                                var container = new IndexFolder()
                                                {
                                                    Id = (item.Name + thisContainer.Name).GetMD5(),
                                                    DateCreated = createdTime,
                                                    Name = thisContainer.Name + " (" + episodes.Count + " items)",
                                                    Overview = thisContainer.Overview,
                                                    MpaaRating = thisContainer.MpaaRating,
                                                    Genres = thisContainer.Genres,
                                                    ImdbRating = thisContainer.ImdbRating,
                                                    Studios = thisContainer.Studios,
                                                    PrimaryImagePath = thisContainer.PrimaryImagePath,
                                                    SecondaryImagePath = thisContainer.SecondaryImagePath,
                                                    BannerImagePath = thisContainer.BannerImagePath,
                                                    BackdropImagePaths = thisContainer.BackdropImagePaths,
                                                    DisplayMediaType = thisContainer.DisplayMediaType,
                                                    Parent = folder
                                                };

                                                foreach (var pair in episodes)
                                                {
                                                    container.AddChild(pair.Value.BaseItem);
                                                }
                                                var containerModel = ItemFactory.Instance.Create(container);
                                                containerModel.PhysicalParent = this;
                                                while (subItems.ContainsKey(createdTime))
                                                {
                                                    // break ties 
                                                    createdTime = createdTime.AddMilliseconds(1);
                                                }
                                                subItems.Add(createdTime, containerModel);
                                                containsSeasons = true;
                                            }
                                            else
                                            {
                                                foreach (var pair in episodes)
                                                {
                                                    var key = pair.Key;
                                                    while (subItems.ContainsKey(key))
                                                    {
                                                        // break ties 
                                                        key = key.AddMilliseconds(1);
                                                    }
                                                    subItems.Add(key, pair.Value);
                                                }
                                            }
                                        }

                                    }
                                }
                                if (subItems.Count >= Config.Instance.RecentItemCollapseThresh || (containsSeasons && subItems.Count > 0)) // always roll series so seasons don't end up on main list
                                {
                                    //collapse into a container folder
                                    var thisContainer = item as IContainer;
                                    var ignore = item.BackdropImages; //force these to load so they will inherit
                                    DateTime watchedTime = subItems.Keys.Max();
                                    var container = new IndexFolder()
                                    {
                                        Id = (folder.Name + thisContainer.Name).GetMD5(),
                                        Name = thisContainer.Name + " (" + subItems.Count + " items)",
                                        Overview = thisContainer.Overview,
                                        MpaaRating = thisContainer.MpaaRating,
                                        Genres = thisContainer.Genres,
                                        ImdbRating = thisContainer.ImdbRating,
                                        Studios = thisContainer.Studios,
                                        PrimaryImagePath = thisContainer.PrimaryImagePath,
                                        SecondaryImagePath = thisContainer.SecondaryImagePath,
                                        BannerImagePath = thisContainer.BannerImagePath,
                                        BackdropImagePaths = thisContainer.BackdropImagePaths,
                                        DisplayMediaType = thisContainer.DisplayMediaType,
                                        Parent = folder
                                    };

                                    foreach (var pair in subItems)
                                    {
                                        container.AddChild(pair.Value.BaseItem);
                                    }
                                    var containerModel = ItemFactory.Instance.Create(container);
                                    containerModel.PhysicalParent = this;
                                    while (foundNames.ContainsKey(watchedTime))
                                    {
                                        // break ties 
                                        watchedTime = watchedTime.AddMilliseconds(1);
                                    }
                                    foundNames.Add(watchedTime, containerModel);
                                    foundIds.Add(container.Id);
                                }
                                else
                                {
                                    foreach (var pair in subItems)
                                    {
                                        if (!foundIds.Contains(pair.Value.Id))
                                        {
                                            var key = pair.Key;
                                            while (foundNames.ContainsKey(key))
                                            {
                                                // break ties 
                                                key = key.AddMilliseconds(1);
                                            }
                                            foundNames.Add(key, pair.Value);
                                            foundIds.Add(pair.Value.Id);
                                            if (foundNames.Count >= maxSize)
                                            {
                                                foundNames.RemoveAt(0);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (!(item is Series))
                                {
                                    //regular folder or box set - go into it
                                    SortedList<DateTime, Item> subItems = new SortedList<DateTime, Item>();
                                    FindRecentWatchedChildren(item as Folder, subItems, maxSize);
                                    foreach (var pair in subItems)
                                    {
                                        if (!foundIds.Contains(pair.Value.Id))
                                        {
                                            var key = pair.Key;
                                            while (foundNames.ContainsKey(key))
                                            {
                                                // break ties 
                                                key = key.AddMilliseconds(1);
                                            }
                                            foundNames.Add(key, pair.Value);
                                            foundIds.Add(pair.Value.Id);
                                            if (foundNames.Count >= maxSize)
                                            {
                                                foundNames.RemoveAt(0);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (item is Video && !foundIds.Contains(item.Id))
                        {
                            Video i = item as Video;
                            DateTime watchedTime = i.PlaybackStatus.LastPlayed;
                            if (i.PlaybackStatus.PlayCount > 0 && DateTime.Compare(watchedTime, daysAgo) > 0)
                            {
                                //only get ones watched within last 60 days
                                while (foundNames.ContainsKey(watchedTime))
                                {
                                    // break ties 
                                    watchedTime = watchedTime.AddMilliseconds(1);
                                }
                                Item modelItem = ItemFactory.Instance.Create(item);
                                modelItem.PhysicalParent = this;
                                //item.Parent = folder;
                                var ignore = item.Parent != null ? item.Parent.BackdropImages : null; //force these to load so they will inherit
                                ignore = item.BackdropImages;
                                foundNames.Add(watchedTime, modelItem);
                                if (foundNames.Count > maxSize)
                                {
                                    foundNames.RemoveAt(0);
                                }
                            }
                        }

                    }
                }

            }
        }

        public void FindRecentUnwatchedChildren(Folder folder, SortedList<DateTime, Item> foundNames, int maxSize)
        {
            DateTime daysAgo = DateTime.Now.Subtract(DateTime.Now.Subtract(DateTime.Now.AddDays(-Config.Instance.RecentItemDays)));
            HashSet<Guid> foundIds = new HashSet<Guid>();
            foreach (var item in folder.Children)
            {
                // skip folders
                if (item is Folder)
                {
                    //don't return items inside protected folders
                    if (item.ParentalAllowed)
                    {
                        if (item is IContainer && !(item is Season) && !foundIds.Contains((folder.Name + item.Name).GetMD5()))
                        {
                            //collapse series in the list
                            bool containsSeasons = false;
                            SortedList<DateTime, Item> subItems = new SortedList<DateTime, Item>();
                            FindRecentUnwatchedChildren(item as Folder, subItems, maxSize);
                            if (item is Series)
                            {
                                //we need to go another level into series to get actual items
                                foreach (var seriesChild in (item as Folder).Children)
                                {
                                    if (seriesChild is Season)
                                    {
                                        SortedList<DateTime, Item> episodes = new SortedList<DateTime, Item>();
                                        FindRecentUnwatchedChildren(seriesChild as Folder, episodes, maxSize);
                                        if (episodes.Count >= Config.Instance.RecentItemCollapseThresh)
                                        {
                                            //collapse into a season
                                            var thisContainer = seriesChild as Season;
                                            var ignore = seriesChild.BackdropImages; //force these to load so they will inherit
                                            DateTime createdTime = episodes.Keys.Max();
                                            var container = new IndexFolder()
                                            {
                                                Id = (item.Name + thisContainer.Name).GetMD5(),
                                                DateCreated = createdTime,
                                                Name = thisContainer.Name + " (" + episodes.Count + " items)",
                                                Overview = thisContainer.Overview,
                                                MpaaRating = thisContainer.MpaaRating,
                                                Genres = thisContainer.Genres,
                                                ImdbRating = thisContainer.ImdbRating,
                                                Studios = thisContainer.Studios,
                                                PrimaryImagePath = thisContainer.PrimaryImagePath,
                                                SecondaryImagePath = thisContainer.SecondaryImagePath,
                                                BannerImagePath = thisContainer.BannerImagePath,
                                                BackdropImagePaths = thisContainer.BackdropImagePaths,
                                                DisplayMediaType = thisContainer.DisplayMediaType,
                                                Parent = folder
                                            };

                                            foreach (var pair in episodes)
                                            {
                                                container.AddChild(pair.Value.BaseItem);
                                            }
                                            var containerModel = ItemFactory.Instance.Create(container);
                                            containerModel.PhysicalParent = this;
                                            while (subItems.ContainsKey(createdTime))
                                            {
                                                // break ties 
                                                createdTime = createdTime.AddMilliseconds(1);
                                            }
                                            subItems.Add(createdTime, containerModel);
                                            containsSeasons = true;
                                        }
                                        else
                                        {
                                            foreach (var pair in episodes)
                                            {
                                                var key = pair.Key;
                                                while (subItems.ContainsKey(key))
                                                {
                                                    // break ties 
                                                    key = key.AddMilliseconds(1);
                                                }
                                                subItems.Add(key, pair.Value);
                                            }
                                        }
                                    }

                                }
                            }
                            if (subItems.Count >= Config.Instance.RecentItemCollapseThresh || (containsSeasons && subItems.Count > 0)) // always roll series so seasons don't end up on main list
                            {
                                //collapse into a series folder
                                var thisContainer = item as IContainer;
                                var ignore = item.BackdropImages; //force these to load so they will inherit
                                DateTime createdTime = subItems.Keys.Max();
                                var container = new IndexFolder()
                                {
                                    Id = (folder.Name + thisContainer.Name).GetMD5(),
                                    DateCreated = createdTime,
                                    Name = thisContainer.Name + " (" + subItems.Count + " items)",
                                    Overview = thisContainer.Overview,
                                    MpaaRating = thisContainer.MpaaRating,
                                    Genres = thisContainer.Genres,
                                    ImdbRating = thisContainer.ImdbRating,
                                    Studios = thisContainer.Studios,
                                    PrimaryImagePath = thisContainer.PrimaryImagePath,
                                    SecondaryImagePath = thisContainer.SecondaryImagePath,
                                    BannerImagePath = thisContainer.BannerImagePath,
                                    BackdropImagePaths = thisContainer.BackdropImagePaths,
                                    DisplayMediaType = thisContainer.DisplayMediaType,
                                    Parent = folder
                                };

                                foreach (var pair in subItems)
                                {
                                    container.AddChild(pair.Value.BaseItem);
                                }
                                var containerModel = ItemFactory.Instance.Create(container);
                                containerModel.PhysicalParent = this;
                                while (foundNames.ContainsKey(createdTime))
                                {
                                    createdTime = createdTime.AddMilliseconds(1);
                                }
                                foundNames.Add(createdTime, containerModel);
                                foundIds.Add(container.Id);
                            }
                            else
                            {
                                foreach (var pair in subItems)
                                {
                                    if (!foundIds.Contains(pair.Value.Id))
                                    {
                                        var key = pair.Key;
                                        while (foundNames.ContainsKey(key))
                                        {
                                            // break ties 
                                            key = key.AddMilliseconds(1);
                                        }
                                        foundNames.Add(key, pair.Value);
                                        foundIds.Add(pair.Value.Id);
                                        if (foundNames.Count >= maxSize)
                                        {
                                            foundNames.RemoveAt(0);
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (!(item is Series))
                            {
                                //regular folder or box set - go into it
                                SortedList<DateTime, Item> subItems = new SortedList<DateTime, Item>();
                                FindRecentUnwatchedChildren(item as Folder, subItems, maxSize);
                                foreach (var pair in subItems)
                                {
                                    if (!foundIds.Contains(pair.Value.Id))
                                    {
                                        var key = pair.Key;
                                        while (foundNames.ContainsKey(key))
                                        {
                                            // break ties 
                                            key = key.AddMilliseconds(1);
                                        }
                                        foundNames.Add(key, pair.Value);
                                        foundIds.Add(pair.Value.Id);
                                        if (foundNames.Count >= maxSize)
                                        {
                                            foundNames.RemoveAt(0);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (item is Video && !foundIds.Contains(item.Id))
                    {
                        Video i = item as Video;
                        if (i.PlaybackStatus.WasPlayed == false && DateTime.Compare(item.DateCreated,daysAgo) > 0)
                        {
                            DateTime creationTime = item.DateCreated;
                            Item modelItem = ItemFactory.Instance.Create(item);
                            modelItem.PhysicalParent = this;
                            //item.Parent = folder;
                            var ignore = item.Parent != null ? item.Parent.BackdropImages : null; //force to load so they will inherit
                            ignore = item.BackdropImages;
                            while (foundNames.ContainsKey(creationTime))
                            {
                                creationTime = creationTime.AddMilliseconds(1);
                            }
                            foundNames.Add(creationTime, modelItem);
                            foundIds.Add(item.Id);
                            if (foundNames.Count > maxSize)
                            {
                                foundNames.RemoveAt(0);
                            }
                        }
                    }

                }

            }
        }
        public override void RefreshMetadata() {
            this.RefreshMetadata(true);
        }


        public override void RefreshMetadata(bool displayMsg)
        {
            bool includeChildren = folder.DefaultIncludeChildrenRefresh;
            if (folder.PromptForChildRefresh)
                includeChildren = Application.DisplayDialog(Localization.LocalizedStrings.Instance.GetString("RefreshFolderDial"), Localization.LocalizedStrings.Instance.GetString("RefreshFolderCapDial"), Microsoft.MediaCenter.DialogButtons.Yes | Microsoft.MediaCenter.DialogButtons.No, 10000) == Microsoft.MediaCenter.DialogResult.Yes;

            //first do us
            base.RefreshMetadata(false);
            string msg = includeChildren ? "RefreshFolderProf" : "RefreshProf";
            if (displayMsg) Application.CurrentInstance.Information.AddInformationString(Application.CurrentInstance.StringData(msg) + " " + this.Name);
            Async.Queue("UI Forced Folder Metadata Loader", () =>
            {
                using (new MediaBrowser.Util.Profiler("Refresh " + this.Name))
                {
                    if (!Config.Instance.AutoValidate)
                    {
                        this.folder.ValidateChildren(); //need to look for new/deleted items if not auto
                    }
                    this.folder.ReCacheAllImages();
                    if (includeChildren)
                    {
                        //and now all our children
                        foreach (BaseItem item in this.folder.RecursiveChildren)
                        {
                            Logger.ReportInfo("refreshing " + item.Name);
                            item.RefreshMetadata(MetadataRefreshOptions.Force);
                            item.ReCacheAllImages();
                        }
                    }
                }
            });

        }

        public void RefreshChildren()
        {
            Async.Queue("Child Refresh", () =>
            {
                this.folder.ValidateChildren();
                this.folderChildren.RefreshChildren();
                this.folderChildren.Sort();
                this.RefreshUI();
            });
        }

        public void RefreshUI()
        {
            Logger.ReportVerbose("Forced Refresh of UI on "+this.Name+" called from: "+new StackTrace().GetFrame(1).GetMethod().Name);


            //this could take a bit so kick this off in the background
            Async.Queue("Refresh UI", () =>
            {

                if (this.IsRoot)
                {
                    //if this is the root page - also the recent items
                    try
                    {
                        foreach (FolderModel folder in this.Children)
                        {
                            folder.newestItems = null; //force it to go get the real items
                            folder.GetNewestItems(Config.Instance.RecentItemCount);
                            folder.recentUnwatchedItems = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.ReportException("Invalid root folder type", ex);
                    }
                    //this.SelectedChildChanged(); //make sure recent list changes
                }
                this.FireChildrenChangedEvents();
            }, null, true);
        }

        protected void FireChildrenChangedEvents() {
            if (!Microsoft.MediaCenter.UI.Application.IsApplicationThread) {
                Microsoft.MediaCenter.UI.Application.DeferredInvoke( _ => FireChildrenChangedEvents());
                return;
            }


            //   the only way to get the binder to update the underlying children is to 
            //   change the refrence to the property bound, otherwise the binder thinks 
            //   its all fine a dandy and will not update the children 
            folderChildren.StopListeningForChanges();
            folderChildren = folderChildren.Clone();
            folderChildren.ListenForChanges();


            FirePropertyChanged("Children");
            FirePropertyChanged("SelectedChildIndex");
            lock (watchLock)
                unwatchedCountCache = -1;
            FireWatchedChangedEvents();
            if (this.displayPrefs != null)
                UpdateActualThumbSize();
        }

        private void FireWatchedChangedEvents() {
            if (!Microsoft.MediaCenter.UI.Application.IsApplicationThread) {
                Microsoft.MediaCenter.UI.Application.DeferredInvoke( _ => FireWatchedChangedEvents());
                return;
            }

            FirePropertyChanged("HaveWatched");
            FirePropertyChanged("UnwatchedCount");
            FirePropertyChanged("ShowUnwatched");
            FirePropertyChanged("UnwatchedCountString");
        }

        void ChildMetadataPropertyChanged(IPropertyObject sender, string property) {
            if (this.displayPrefs != null) {
                switch (this.displayPrefs.SortOrder) {
                    case "Year":
                        if (property != "ProductionYear")
                            return;
                        break;
                    case "Name":
                        if (property != "Name")
                            return;
                        break;
                    case "Rating":
                        if (property != "ImdbRating")
                            return;
                        break;
                    case "Runtime":
                        if (property != "RunningTime")
                            return;
                        break;

                    case "Date":
                        // date sorting is not affected by metadata
                        return;
                    case "Unwatched":
                        if (property != "Name")
                            return;
                        break;

                }
            }
            this.FirePropertyChanged("Children");
        }

        void ChildPropertyChanged(IPropertyObject sender, string property) {
            if (property == "UnwatchedCount") {
                lock (watchLock)
                    unwatchedCountCache = -1;
                FirePropertyChanged("HaveWatched");
                FirePropertyChanged("UnwatchedCount");
                FirePropertyChanged("ShowUnwatched");
                FirePropertyChanged("UnwatchedCountString");
                // note: need to be careful this doesn't trigger the load of the prefs 
                // that can then trigger a cascade that loads metadata, prefs should only be loaded by 
                // functions that are required when the item is the current item displayed
                if ((this.displayPrefs != null) && (this.DisplayPrefs.SortOrder == "Unwatched")) {
                    FirePropertyChanged("Children");
                }
            } else if (property == "ThumbAspectRatio")
                UpdateActualThumbSize();
        }

        public FolderChildren Children {
            get{
                return folderChildren;
            }
        }

        public int SelectedChildIndex {
            get {
                if (selectedchildIndex > Children.Count)
                    selectedchildIndex = -1;
                return selectedchildIndex;
            }
            set {

                if (selectedchildIndex != value) {
                    selectedchildIndex = value;
                    SelectedChildChanged();
                }
            }
        }

        public Item NextChild
        {
            get
            {
                if (selectedchildIndex < 0) {
                    return Application.CurrentInstance.CurrentItem; //we have no selected child
                }
                //we don't use the public property because we want to roll around the list instead of going to unselected
                selectedchildIndex++;
                if (selectedchildIndex >= Children.Count)
                {
                    selectedchildIndex = 0;
                }
                SelectedChildChanged();
                return SelectedChild;
            }
        }

        public Item PrevChild
        {
            get
            {
                if (selectedchildIndex < 0) {
                    return Application.CurrentInstance.CurrentItem; //we have no selected child
                }
                //we don't use the public property because we want to roll around the list instead of going to unselected
                selectedchildIndex--;
                if (selectedchildIndex < 0)
                {
                    selectedchildIndex = Children.Count - 1;
                }
                SelectedChildChanged();
                return SelectedChild;
            }
        }

        public Item FirstChild
        {
            get
            {
                if (Children.Count > 0)
                {
                    SelectedChildIndex = 0;
                    return SelectedChild;
                }
                else
                {
                    return Item.BlankItem;
                }
            }
        }

        public Item LastChild
        {
            get
            {
                if (Children.Count > 0)
                {
                    SelectedChildIndex = Children.Count - 1;
                    return SelectedChild;
                }
                else
                {
                    return Item.BlankItem;
                }
            }
        }
                    
        private void SelectedChildChanged() {
            FirePropertyChanged("SelectedChildIndex");
            FirePropertyChanged("SelectedChild");
            Application.CurrentInstance.CurrentItemChanged();
        }

        internal override void SetWatched(bool value) {
            folder.Watched = value;
        }

        public Item SelectedChild {
            get {
                if ((SelectedChildIndex < 0) || (selectedchildIndex >= Children.Count))
                    return Item.BlankItem;
                return Children[SelectedChildIndex];
            }
        }

        protected void IndexByChoice_ChosenChanged(object sender, EventArgs e) {

            folderChildren.IndexBy(displayPrefs.IndexBy);
            selectedchildIndex = -1;
            if (folderChildren.Count > 0)
                SelectedChildIndex = 0;
        }

        public int JILShift {
            get {
                return jilShift;
            }
            set {
                jilShift = value;
                FirePropertyChanged("JILShift");
            }
        }

        public string TripleTapSelect {
            set {

                if (!String.IsNullOrEmpty(value) && (MediaBrowser.LibraryManagement.Helper.IsAlphaNumeric(value))) {
                    BaseItemComparer comparer = new BaseItemComparer(SortOrder.Name);
                    BaseItem tempItem = Activator.CreateInstance(this.folder.ChildType) as BaseItem;
                    if (this.displayPrefs.SortOrder == Localization.LocalizedStrings.Instance.GetString("NameDispPref"))
                    {
                            tempItem.Name = value;
                    } else
                        if (this.displayPrefs.SortOrder == Localization.LocalizedStrings.Instance.GetString("DateDispPref"))
                        {
                            //no good way to do this
                            return;
                        } else
                            if (this.displayPrefs.SortOrder == Localization.LocalizedStrings.Instance.GetString("RatingDispPref"))
                            {
                            try
                            {
                                if (tempItem is IShow)
                                {
                                    comparer = new BaseItemComparer(SortOrder.Rating);
                                    (tempItem as IShow).ImdbRating = Convert.ToSingle(value);
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.ReportException("Error in custom JIL selection", e);
                            }
                            } else
                                if (this.displayPrefs.SortOrder == Localization.LocalizedStrings.Instance.GetString("RuntimeDispPref"))
                                {
                            try
                            {
                                if (tempItem is IShow)
                                {
                                    comparer = new BaseItemComparer(SortOrder.Runtime);
                                    (tempItem as IShow).RunningTime = Convert.ToInt32(value);
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.ReportException("Error in custom JIL selection", e);
                            }
                                } else
                                    if (this.displayPrefs.SortOrder == Localization.LocalizedStrings.Instance.GetString("YearDispPref"))
                                    {
                                        try
                                        {
                                            if (tempItem is IShow)
                                            {
                                                comparer = new BaseItemComparer(SortOrder.Year);
                                                (tempItem as IShow).ProductionYear = Convert.ToInt32(value);
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            Logger.ReportException("Error in custom JIL selection", e);
                                        }
                                    }
                                    else
                                    {
                                        try
                                        {
                                            comparer = new BaseItemComparer(this.displayPrefs.SortOrder); //this won't work if these have been localized...no way around it now
                                            tempItem.GetType().GetProperty(this.displayPrefs.SortOrder).SetValue(tempItem, value, null);
                                        }
                                        catch (Exception e)
                                        {
                                            Logger.ReportException("Error in custom JIL selection", e);
                                        }
                                    }
                

                    int i = 0; 
                    foreach (var child in Children) {
                        if (comparer.Compare(tempItem, child.BaseItem) <= 0) break;
                        i++; 
                    }

                    JILShift = i - SelectedChildIndex;
                }
                 
            }
        }

        protected virtual void SortOrders_ChosenChanged(object sender, EventArgs e) {
            folderChildren.Sort(this.displayPrefs.SortFunction);
        }

        public virtual DisplayPreferences DisplayPrefs {
            get {
                if (this.displayPrefs == null)
                    LoadDisplayPreferences();
                return this.displayPrefs;
            }
            protected set {
                if (this.displayPrefs != null)
                    throw new NotSupportedException("Attempt to set displayPrefs twice");
                this.displayPrefs = value;
                if (this.displayPrefs != null) {
                    this.displayPrefs.ThumbConstraint.PropertyChanged += new PropertyChangedEventHandler(ThumbConstraint_PropertyChanged);
                    this.displayPrefs.ShowLabels.PropertyChanged += new PropertyChangedEventHandler(ShowLabels_PropertyChanged);
                    this.displayPrefs.SortOrders.ChosenChanged += new EventHandler(SortOrders_ChosenChanged);
                    this.displayPrefs.IndexByChoice.ChosenChanged += new EventHandler(IndexByChoice_ChosenChanged);
                    this.displayPrefs.ViewType.ChosenChanged += new EventHandler(ViewType_ChosenChanged);
                    this.displayPrefs.UseBanner.ChosenChanged += new EventHandler(UseBanner_ChosenChanged);
                    SortOrders_ChosenChanged(null, null);
                    ShowLabels_PropertyChanged(null, null);
                    if (this.actualThumbSize.Value.Height != 1)
                        ThumbConstraint_PropertyChanged(null, null);

                    if (displayPrefs.IndexBy != "None" && displayPrefs.IndexBy != "") {
                        IndexByChoice_ChosenChanged(this, null);
                    }
                }
                FirePropertyChanged("DisplayPrefs");
            }
        }

        void ViewType_ChosenChanged(object sender, EventArgs e)
        {
            var ignore = ShowNowPlayingInText;
        }

        void UseBanner_ChosenChanged(object sender, EventArgs e) {
            UpdateActualThumbSize();
        }


        protected virtual void LoadDisplayPreferences() {
            Logger.ReportVerbose("Loading display prefs for " + this.Path);

            Guid id = Id;

            if (Config.Instance.EnableSyncViews) {
                if (baseItem is Folder && baseItem.GetType() != typeof(Folder)) {
                    id = baseItem.GetType().FullName.GetMD5();
                }
            }

            DisplayPreferences dp = new DisplayPreferences(id, this.Folder);
            dp = Kernel.Instance.ItemRepository.RetrieveDisplayPreferences(dp);
            if (dp == null) {
                LoadDefaultDisplayPreferences(ref id, ref dp);
            }

            this.DisplayPrefs = dp;
        }

        protected void LoadDefaultDisplayPreferences(ref Guid id, ref DisplayPreferences dp)
        {
            dp = new DisplayPreferences(id, this.Folder);
            dp.LoadDefaults();
            if ((this.PhysicalParent != null) && (Config.Instance.InheritDefaultView))
            {
                // inherit some of the display properties from our parent the first time we are visited
                DisplayPreferences pt = this.PhysicalParent.DisplayPrefs;
                dp.ViewType.Chosen = pt.ViewType.Chosen;
                dp.ShowLabels.Value = pt.ShowLabels.Value;
                // after some use, carrying the sort order forward doesn;t feel right - for seasons especially it can be confusing
                // dp.SortOrder = pt.SortOrder;
                dp.VerticalScroll.Value = pt.VerticalScroll.Value;
            }
        }


        protected void UpdateActualThumbSize() {

            if (!Microsoft.MediaCenter.UI.Application.IsApplicationThread) {
                Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => UpdateActualThumbSize());
                return;
            }

            bool useBanner = this.displayPrefs.UseBanner.Value;

            float f = folderChildren.GetChildAspect(useBanner);

            Size s = this.DisplayPrefs.ThumbConstraint.Value;
            if (f == 0)
                f = 1;
            float maxAspect = s.Height / s.Width;
            if (f > maxAspect)
                s.Width = (int)(s.Height / f);
            else
                s.Height = (int)(s.Width * f);

            if (this.actualThumbSize.Value != s) {
                this.actualThumbSize.Value = s;
                FirePropertyChanged("ReferenceSize");
                FirePropertyChanged("PosterZoom");
            }
        }

        /// <summary>
        /// Determines the size the grid layout gives to each item, without this it bases it off the first item.
        /// We need this as without it under some circustance when labels are showing and the first item is in 
        /// focus things get upset and all the other posters dissappear
        /// It seems to be something todo with what happens when the text box gets scaled
        /// </summary>
        public Size ReferenceSize {
            get {
                Size s = this.ActualThumbSize.Value;
                if (DisplayPrefs.ShowLabels.Value)
                    s.Height += 40;
                return s;
            }
        }

        public SizeRef ActualThumbSize {
            get {

                if (this.actualThumbSize.Value.Height == 1)
                    UpdateActualThumbSize();
                return actualThumbSize;
            }
        }

        public Vector3 PosterZoom {
            get {
                Size s = this.ReferenceSize;
                float x = Math.Max(s.Height, s.Width);
                if (x == 1)
                    return new Vector3(1.15F, 1.15F, 1); // default if we haven't be set yet
                float z = (float)((-0.007 * x) + 2.5);
                if (z < 1.15)
                    z = 1.15F;
                if (z > 1.9F)
                    z = 1.9F; // above this the navigation arrows start going in strange directions!
                return new Vector3(z, z, 1);
            }
        }


        BooleanChoice showNowPlayingInText;
        public BooleanChoice ShowNowPlayingInText
        {
            get {
                if (showNowPlayingInText == null)
                {
                    showNowPlayingInText = new BooleanChoice();
                }
                var enableText = new string[] {"Detail", "ThumbStrip", "CoverFlow"};
                if (Kernel.Instance.ConfigData.ShowNowPlayingInText && enableText.Contains(DisplayPrefs.ViewTypeString))
                {
                    showNowPlayingInText.Value = true;
                }
                else
                {
                    showNowPlayingInText.Value = false;
                }
                return showNowPlayingInText;
            }
        }

        void ShowLabels_PropertyChanged(IPropertyObject sender, string property) {
            FirePropertyChanged("ReferenceSize");
            FirePropertyChanged("PosterZoom");
        }

        void ThumbConstraint_PropertyChanged(IPropertyObject sender, string property) {
            UpdateActualThumbSize();
            FirePropertyChanged("ReferenceSize");
            FirePropertyChanged("PosterZoom");
        }

        protected override void Dispose(bool disposing) {

            if (folderChildren != null)
                folderChildren.Dispose();
              

            if (this.displayPrefs != null)
                this.displayPrefs.Dispose();

            base.Dispose(disposing);
        }
        
    }
}