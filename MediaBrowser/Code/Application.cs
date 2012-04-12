using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Code;
using MediaBrowser.Library.Events;
using MediaBrowser.Library;
using MediaBrowser.Library.Configuration;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Factories;
using MediaBrowser.Library.Input;
using MediaBrowser.Library.Localization;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Metadata;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.Library.Threading;
using MediaBrowser.Library.UI;
using MediaBrowser.LibraryManagement;
using MediaBrowser.Util;
using Microsoft.MediaCenter;
using Microsoft.MediaCenter.AddIn;
using Microsoft.MediaCenter.UI;

namespace MediaBrowser
{

    public class Application : ModelItem, IDisposable
    {
        public Config Config
        {
            get
            {
                return Config.Instance;
            }
        }

        public static Application CurrentInstance
        {
            get { return singleApplicationInstance; }
        }

        public string StringData(string name)
        {
            return Kernel.Instance.GetString(name);
        }

        public MBPropertySet LocalStrings //used to access our localized strings from mcml
        {
            get
            {
                return Kernel.Instance.LocalStrings;
            }
        }

        private static Application singleApplicationInstance;
        private MyHistoryOrientedPageSession session;
        private static object syncObj = new object();
        private bool navigatingForward;
        private IPlaybackController currentPlaybackController = null;
        private static string _background;
        private static Timer ScreenSaverTimer;
        //tracks whether to show recently added or watched items
        public string RecentItemOption { get { return Config.Instance.RecentItemOption; } set { Config.Instance.RecentItemOption = value; Kernel.Instance.ConfigData.RecentItemOption = value; } }
        private bool pluginUpdatesAvailable = false;
        public System.Drawing.Bitmap ExtSplashBmp;
        private Item lastPlayed;

        #region NavigatedInto EventHandler
        volatile EventHandler<GenericEventArgs<Item>> _NavigationInto;
        /// <summary>
        /// Fires whenever an Item is navigated into
        /// </summary>
        public event EventHandler<GenericEventArgs<Item>> NavigationInto
        {
            add
            {
                _NavigationInto += value;
            }
            remove
            {
                _NavigationInto -= value;
            }
        }

        internal void OnNavigationInto(Item item)
        {
            if (_NavigationInto != null)
            {
                _NavigationInto(this, new GenericEventArgs<Item>() { Item = item });
            }
        }
        #endregion

        public bool PluginUpdatesAvailable
        {
            get
            {
                return pluginUpdatesAvailable;
            }
            set
            {
                pluginUpdatesAvailable = value;
                FirePropertyChanged("PluginUpdatesAvailable");
            }
        }

        private bool _ScreenSaverActive = false;

        public bool ScreenSaverActive
        {
            get { return _ScreenSaverActive; }
            set { if (_ScreenSaverActive != value) { _ScreenSaverActive = value; FirePropertyChanged("ScreenSaverActive"); } }
        }

        public string CurrentScreenSaver
        {
            get { return Kernel.Instance.ScreenSaverUI; }
        }

        public List<string> ConfigPanelNames
        {
            get
            {
                return Kernel.Instance.ConfigPanels.Keys.ToList();
            }
        }

        public string ConfigPanel(string name)
        {
            if (Kernel.Instance.ConfigPanels.ContainsKey(name))
            {
                return Kernel.Instance.ConfigPanels[name].Resource;
            }
            else
            {
                return "me:AddinPanel"; //return the embedded empty UI if not found
            }
        }

        public Choice ConfigModel { get; set; }

        public string CurrentConfigPanel
        {
            get
            {
                return Kernel.Instance.ConfigPanels[ConfigModel.Chosen.ToString()].Resource;
            }
        }

        public ModelItem CurrentConfigObject
        {
            get
            {
                if (Kernel.Instance.ConfigPanels[ConfigModel.Chosen.ToString()] != null)
                {
                    return Kernel.Instance.ConfigPanels[ConfigModel.Chosen.ToString()].ConfigObject;
                }
                else return null;
            }
        }

        public Dictionary<string, ViewTheme> AvailableThemes { get { return Kernel.Instance.AvailableThemes; } }

        public List<string> AvailableThemeNames
        {
            get
            {
                return AvailableThemes.Keys.ToList();
            }
        }

        public ViewTheme CurrentTheme
        {
            get
            {
                if (AvailableThemes.ContainsKey(Config.Instance.ViewTheme))
                {
                    return AvailableThemes[Config.Instance.ViewTheme];
                }
                else
                { //old or bogus theme - return default so we don't crash
                    //and set the config so config page doesn't crash
                    Config.Instance.ViewTheme = "Default";
                    return AvailableThemes["Default"];
                }
            }
        }

        public bool SetThemeStatus(string theme, string status)
        {
            if (AvailableThemes.ContainsKey(theme))
            {
                AvailableThemes[theme].Status = status;
                FirePropertyChanged("CurrentThemeStatus");
                return true;
            }
            else
            {
                return false;
            }
        }

        public string CurrentThemeStatus
        {
            get
            {
                return CurrentTheme.Status;
            }
        }

        private Item currentItem;

        public Item CurrentItem
        {
            get
            {
                if (currentItem != null)
                {
                    return currentItem;
                }
                else
                {
                    if (Application.CurrentInstance.CurrentFolder.SelectedChild != null)
                    {
                        return Application.CurrentInstance.CurrentFolder.SelectedChild;
                    }
                    else return Item.BlankItem;
                }
            }
            set
            {
                if (currentItem != value)
                {
                    currentItem = value;
                    CurrentItemChanged();
                }
            }
        }

        public void CurrentItemChanged()
        {
            FirePropertyChanged("CurrentItem");
        }

        private List<MenuItem> currentContextMenu;

        public List<MenuItem> ContextMenu
        {
            get
            {
                if (currentContextMenu == null) currentContextMenu = Kernel.Instance.ContextMenuItems;
                return currentContextMenu;
            }
            set
            {
                currentContextMenu = value;
                //Logger.ReportVerbose("Context Menu Changed.  Items: " + currentContextMenu.Count);
                FirePropertyChanged("ContextMenu");
            }
        }

        public void ResetContextMenu()
        {
            ContextMenu = Kernel.Instance.ContextMenuItems;
        }

        public List<MenuItem> PlayMenu
        {
            get
            {
                return Kernel.Instance.PlayMenuItems;
            }
        }

        public List<MenuItem> DetailMenu
        {
            get
            {
                return Kernel.Instance.DetailMenuItems;
            }
        }

        private MenuManager menuManager;

        public bool NavigatingForward
        {
            get { return navigatingForward; }
            set { navigatingForward = value; }
        }


        private string entryPointPath = string.Empty;

        public string EntryPointPath
        {
            get
            {
                return this.entryPointPath.ToLower();
            }
        }

        public const string CONFIG_ENTRY_POINT = "configmb";

        public string ConfigEntryPointVal
        {
            get
            {
                return CONFIG_ENTRY_POINT.ToLower();
            }
        }

        public Item LastPlayedItem
        {
            get
            {
                return lastPlayed ?? Item.BlankItem;
            }
        }

        static Application()
        {

        }

        public Application()
            : this(null, null)
        {

        }

        public Application(MyHistoryOrientedPageSession session, Microsoft.MediaCenter.Hosting.AddInHost host)
        {

            this.session = session;
            if (session != null)
            {
                this.session.Application = this;
            }
            singleApplicationInstance = this;
            //wire up our mouseActiveHooker if enabled so we can know if the mouse is active over us
            if (Config.Instance.EnableMouseHook)
            {
                Kernel.Instance.MouseActiveHooker.MouseActive += new IsMouseActiveHooker.MouseActiveHandler(mouseActiveHooker_MouseActive);
            }
            //populate the config model choice
            ConfigModel = new Choice();
            ConfigModel.Options = ConfigPanelNames;

            //initialize our menu manager
            menuManager = new MenuManager();

            //initialize screen saver
            ScreenSaverTimer = new Timer() { AutoRepeat = true, Enabled = true, Interval = 60000 };
            ScreenSaverTimer.Tick += new EventHandler(ScreenSaverTimer_Tick);

        }

        void ScreenSaverTimer_Tick(object sender, EventArgs e)
        {
            if (Config.EnableScreenSaver) 
            {
                if (!this.PlaybackController.IsPlayingVideo)
                {
                    if (Helper.SystemIdleTime > Config.ScreenSaverTimeOut * 60000)
                    {
                        this.ScreenSaverActive = true;
                        //increase the frequency of this tick so we will turn off quickly
                        ScreenSaverTimer.Interval = 500;
                    }
                    else
                    {
                        this.ScreenSaverActive = false;
                        ScreenSaverTimer.Interval = 60000; //move back to every minute
                    }
                }
                else
                {
                    if (!this.ScreenSaverActive)
                    {
                        //something playing - be sure we don't kick off right after it ends
                        ScreenSaverTimer.Interval = Config.ScreenSaverTimeOut * 60000;
                    }
                }
            }
        }


        /// <summary>
        /// This is an oddity under TVPack, sometimes the MediaCenterEnvironemt and MediaExperience objects go bad and become
        /// disconnected from their host in the main application. Typically this is after 5 minutes of leaving the application idle (but noot always).
        /// What is odd is that using reflection under these circumstances seems to work - even though it is only doing the same as Reflector shoulds the real 
        /// methods do. As I said it's odd but this at least lets us get a warning on the screen before the application crashes out!
        /// </summary>
        /// <param name="message"></param>
        public static void DialogBoxViaReflection(string message)
        {
            MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
            FieldInfo fi = ev.GetType().GetField("_legacyAddInHost", BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance);
            if (fi != null)
            {
                AddInHost2 ah2 = (AddInHost2)fi.GetValue(ev);
                if (ah2 != null)
                {
                    Type t = ah2.GetType();
                    PropertyInfo pi = t.GetProperty("HostControl", BindingFlags.Instance | BindingFlags.GetProperty | BindingFlags.NonPublic | BindingFlags.Public);
                    if (pi != null)
                    {
                        HostControl hc = (HostControl)pi.GetValue(ah2, null);
                        hc.Dialog(message, "Media Browser", 1, 120, true);
                    }
                }
            }
        }

        public static bool RunningOnExtender
        {
            get
            {
                try
                {
                    Dictionary<string,object> capabilities = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment.Capabilities;

                    bool isLocal = capabilities.ContainsKey("Console") && (bool)capabilities["Console"];
                    return !isLocal;
                }
                catch (Exception ex)
                {
                    Logger.ReportException("Error in RunningOnExtender.", ex);
                    Logger.ReportError("Application has broken MediaCenterEnvironment.  Hopefully, this is a temporary situation...");
                    //don't crash - just assume we are on a regular install and something went wrong momentarily - it'll have a problem later if it is real
                    return false;
                    //Application.ReportBrokenEnvironment();
                    //throw;
                }
            }
        }


        /// <summary>
        /// Unfortunately TVPack has some issues at the moment where the MedaCenterEnvironment stops working, we catch these errors and rport them then close.
        /// In the future this method and all references should be able to be removed, once MS fix the bugs
        /// </summary>
        internal static void ReportBrokenEnvironment()
        {
            Logger.ReportInfo("Application has broken MediaCenterEnvironment, possibly due to 5 minutes of idle while running under system with TVPack installed.\n Application will now close.");
            Logger.ReportInfo("Attempting to use reflection that sometimes works to show a dialog box");
            // for some reason using reflection still works
            Application.DialogBoxViaReflection(CurrentInstance.StringData("BrokenEnvironmentDial"));
            Microsoft.MediaCenter.Hosting.AddInHost.Current.ApplicationContext.CloseApplication();
        }

        public void FixRepeatRate(object scroller, int val)
        {
            try
            {
                PropertyInfo pi = scroller.GetType().GetProperty("View", BindingFlags.Public | BindingFlags.Instance);
                object view = pi.GetValue(scroller, null);
                pi = view.GetType().GetProperty("Control", BindingFlags.Public | BindingFlags.Instance);
                object control = pi.GetValue(view, null);

                pi = control.GetType().GetProperty("KeyRepeatThreshold", BindingFlags.NonPublic | BindingFlags.Instance);
                pi.SetValue(control, (UInt32)val, null);
            }
            catch
            {
                // thats it, I give up, Microsoft went and changed interfaces internally 
            }

        }

        public static MediaCenterEnvironment MediaCenterEnvironment
        {
            get
            {
                return Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
            }
        }

        public IPlaybackController PlaybackController
        {
            get
            {
                if (currentPlaybackController != null)
                    return currentPlaybackController;
                return Kernel.Instance.PlaybackControllers[0];
            }
        }


        public AggregateFolder RootFolder
        {
            get
            {
                return Kernel.Instance.RootFolder;
            }
        }

        public void Close()
        {
            Microsoft.MediaCenter.Hosting.AddInHost.Current.ApplicationContext.CloseApplication();
        }

        public void BackOut()
        {
            //back up and close the app if that fails
            if (!session.BackPage())
                Close();
        }

        public void Back()
        {
            session.BackPage();
        }

        public void FinishInitialConfig()
        {
            MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
            ev.Dialog(CurrentInstance.StringData("InitialConfigDial"), CurrentInstance.StringData("Restartstr"), DialogButtons.Ok, 60, true);
            Microsoft.MediaCenter.Hosting.AddInHost.Current.ApplicationContext.CloseApplication();

        }

        public void DeleteMediaItem(Item Item)
        {
            // Setup variables
            MediaCenterEnvironment mce = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
            var msg = CurrentInstance.StringData("DeleteMediaDial");
            var caption = CurrentInstance.StringData("DeleteMediaCapDial");

            // Present dialog
            DialogResult dr = mce.Dialog(msg, caption, DialogButtons.No | DialogButtons.Yes, 0, true);

            if (dr == DialogResult.No)
            {
                mce.Dialog(CurrentInstance.StringData("NotDeletedDial"), CurrentInstance.StringData("NotDeletedCapDial"), DialogButtons.Ok, 0, true);
                return;
            }

            if (dr == DialogResult.Yes && this.Config.Advanced_EnableDelete == true
                && this.Config.EnableAdvancedCmds == true)
            {
                Item parent = Item.PhysicalParent;
                string path = Item.Path;
                string name = Item.Name;

                try
                {
                    //play something innocuous to be sure the file we are trying to delete is not in the now playing window
                    string DingFile = System.Environment.ExpandEnvironmentVariables("%WinDir%") + "\\Media\\Windows Recycle.wav";

                    PlaybackArguments playInfo = new PlaybackArguments();
                    playInfo.Files = new string[] { DingFile };

                    // try and run the file regardless whether it exists or not.  Ideally we want it to play but if we can't find it, it will still put MC in a state that allows
                    // us to delete the file we are trying to delete
                    PlaybackController.PlayMedia(playInfo);

                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                    else if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                    mce.Dialog(CurrentInstance.StringData("NotDelInvalidPathDial"), CurrentInstance.StringData("DelFailedDial"), DialogButtons.Ok, 0, true);
                }
                catch (Exception)
                {
                    mce.Dialog(CurrentInstance.StringData("NotDelUnknownDial"), CurrentInstance.StringData("DelFailedDial"), DialogButtons.Ok, 0, true);
                }
                DeleteNavigationHelper(parent);
                this.Information.AddInformation(new InfomationItem("Deleted media item: " + name, 2));
            }
            else
                mce.Dialog(CurrentInstance.StringData("NotDelTypeDial"), CurrentInstance.StringData("DelFailedDial"), DialogButtons.Ok, 0, true);
        }


        private void DeleteNavigationHelper(Item Parent)
        {
            Back(); // Back to the Parent Item; This parent still contains old data.
            if (Parent != null) //if we came from a recent list parent may not be valid
            {
                if (Parent is FolderModel)
                {
                    Async.Queue("Post delete validate", () => (Parent as FolderModel).Folder.ValidateChildren()); //update parent info
                }
            }
        }

        // Entry point for the app
        public void GoToMenu()
        {
            Logger.ReportInfo("Media Browser (version " + AppVersion + ") Starting up.");
            //let's put some useful info in here for diagnostics
            if (!Config.AutoValidate)
                Logger.ReportWarning("*** AutoValidate is OFF.");
            if (Config.ParentalControlEnabled)
                Logger.ReportInfo("*** Parental Controls are ON with a max rating of "+Config.ParentalMaxAllowedString+".  Block Unrated is "+Config.ParentalBlockUnrated+" and Hide Content is "+Config.HideParentalDisAllowed);
            Logger.ReportInfo("*** Internet Providers are "+(Config.AllowInternetMetadataProviders ? "ON." : "OFF."));
            try
            {
                if (Config.IsFirstRun)
                {
                    OpenConfiguration(false);
                    MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                    ev.Dialog(CurrentInstance.StringData("FirstTimeDial"), CurrentInstance.StringData("FirstTimeCapDial"), DialogButtons.Ok, 60, true);
                }
                else
                {
                    //if the service is currently re-building our library - warn them
                    if (Kernel.Instance.ServiceConfigData.ForceRebuildInProgress)
                    {
                        MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                        ev.Dialog(CurrentInstance.StringData("ForcedRebuildDial"), CurrentInstance.StringData("ForcedRebuildCapDial"), DialogButtons.Ok, 15, true);
                        session.Close();
                    }
                    //Check to see if this is the first time this version is run
                    string currentVersion = Kernel.Instance.Version.ToString();
                    if (Config.MBVersion != currentVersion)
                    {
                        //first time with this version - run routine
                        Logger.ReportInfo("First run for version " + currentVersion);
                        bool okToRun = FirstRunForVersion(currentVersion);
                        //and update
                        Config.MBVersion = currentVersion;
                        if (!okToRun)
                        {
                            Logger.ReportInfo("Closing MB to allow new version migration...");
                            this.Close();
                        }
                    }
                    //if the service refresh failed - notify them
                    if (Kernel.Instance.ServiceConfigData.RefreshFailed)
                    {
                        MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                        ev.Dialog(CurrentInstance.StringData("RefreshFailedDial"), CurrentInstance.StringData("RefreshFailedCapDial"), DialogButtons.Ok, 15, true);
                        
                    }
                    // We check config here instead of in the Updater class because the Config class 
                    // CANNOT be instantiated outside of the application thread.
                    if (Config.EnableUpdates)
                    {
                        Updater update = new Updater(this);
                        
                        Async.Queue(Async.STARTUP_QUEUE, () =>
                        {
                            update.CheckForUpdate();
                        }, 40000);
                        Async.Queue(Async.STARTUP_QUEUE, () =>
                        {
                            PluginUpdatesAvailable = update.PluginUpdatesAvailable();
                        }, 60000);
                    }

                    Microsoft.MediaCenter.MediaExperience exp = MediaCenterEnvironment.MediaExperience;

                    // set npv visibility according to current state
                    if (exp != null)
                    {
                        Microsoft.MediaCenter.MediaTransport transport = exp.Transport;

                        if (transport != null)
                        {
                            ShowNowPlaying = transport.PlayState == Microsoft.MediaCenter.PlayState.Playing;
                        }
                    }

                    // setup image to use in external splash screen
                    string splashFilename = Path.Combine(Path.Combine(ApplicationPaths.AppIBNPath,"General"),"splash.png");
                    if (File.Exists(splashFilename))
                    {
                        ExtSplashBmp = new System.Drawing.Bitmap(splashFilename);
                    }
                    else
                    {
                        ExtSplashBmp = new System.Drawing.Bitmap(Resources.mblogo1000);
                    }

                    //validate that everything at the root level is actually a folder - the UI will blow chow with items
                    foreach (var item in RootFolder.Children)
                    {
                        if (!(item is Folder))
                        {
                            string msg = "W A R N I N G: Item " + item.Name + " is resolving to a " + item.GetType().Name + ". All root level items must be folders.\n  Check that this item isn't being mistaken because it is a folder with only a couple items and you have the playlist functionality enabled.";
                            Logger.ReportError(msg);
                            DisplayDialog(msg, "Invalid Root Item");
                        }
                    }

                    // we need to validate the library so that changes in the RAL will get picked up without having to navigate
                    if (Config.AutoValidate)
                    {
                        Async.Queue("Startup Library Validator", () =>
                        {
                            using (new Profiler("Startup Validation"))
                            {
                                Kernel.Instance.MajorActivity = true;
                                RootFolder.RefreshMetadata();

                                //These validations have been moved to the recent list builds since they are needed there anyway

                                //foreach (Folder folder in RootFolder.RecursiveFolders)
                                //{
                                //    folder.ValidateChildren();
                                //}
                                //foreach (BaseItem item in RootFolder.RecursiveChildren)
                                //{
                                //    //refresh new items
                                //    if ((DateTime.Now - item.DateModified).TotalDays < 2)
                                //        item.RefreshMetadata();
                                //}
                                ////finally - we need to invalidate the recent lists to pick up any new items we just found
                                //foreach (Item item in RootFolderModel.Children)
                                //{
                                //    (item as FolderModel).QuickListItems = null; //force to re-load
                                //}
                                Kernel.Instance.MajorActivity = false;
                            }
                        });
                    }
                    //Launch into our entrypoint
                    LaunchEntryPoint(EntryPointResolver.EntryPointPath);
                }
            }
            catch (Exception e)
            {
                Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment.Dialog(CurrentInstance.StringData("CriticalErrorDial") + e.ToString() + " " + e.StackTrace.ToString(), CurrentInstance.StringData("CriticalErrorCapDial"), DialogButtons.Ok, 60, true);
                Microsoft.MediaCenter.Hosting.AddInHost.Current.ApplicationContext.CloseApplication();
            }
        }

        public void LaunchEntryPoint(string entryPointPath)
        {
            this.entryPointPath = entryPointPath;

            if (IsInEntryPoint)
            {
                //add in a fake breadcrumb so they will show properly
                session.AddBreadcrumb("DIRECTENTRY");
            }

            if (this.EntryPointPath.ToLower() == ConfigEntryPointVal) //specialized case for config page
            {
                OpenConfiguration(true);
            }
            else
            {
                try
                {
                    this.RootFolderModel = (MediaBrowser.Library.FolderModel)ItemFactory.Instance.Create(EntryPointResolver.EntryPoint(this.EntryPointPath));
                    if (!IsInEntryPoint)
                    {
                        Async.Queue("Top Level Refresher", () =>
                        {
                            foreach (var item in RootFolderModel.Children)
                            {
                                if (item.BaseItem.RefreshMetadata(MetadataRefreshOptions.FastOnly))
                                    item.ClearImages(); // refresh all the top-level folders to pick up any changes
                            }
                            RootFolderModel.Children.Sort(); //make sure sort is right
                        }, 2000);
                    }

                    Navigate(this.RootFolderModel);
                }
                catch (Exception ex)
                {
                    Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment.Dialog(CurrentInstance.StringData("EntryPointErrorDial") + this.EntryPointPath + ". " + ex.ToString() + " " + ex.StackTrace.ToString(), CurrentInstance.StringData("EntryPointErrorCapDial"), DialogButtons.Ok, 30, true);
                    Close();
                }
            }
        }

        bool FirstRunForVersion(string thisVersion)
        {
            var oldVersion = new System.Version(Config.MBVersion);
            if (oldVersion < new System.Version(2, 0, 0, 0))
            {
                Logger.ReportInfo("First run of Media Browser.  Initiating a full refresh of the library in service.");
                if (MBServiceController.SendCommandToService(IPCCommands.ForceRebuild))
                {
                    MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                    ev.Dialog(CurrentInstance.StringData("RebuildNecDial"), CurrentInstance.StringData("ForcedRebuildCapDial"), DialogButtons.Ok, 30, true);
                }
                else
                {
                    MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                    ev.Dialog(CurrentInstance.StringData("RebuildFailedDial"), CurrentInstance.StringData("ForcedRebuildCapDial"), DialogButtons.Ok, 30, true);
                }
                return true;
            }
            switch (thisVersion)
            {
                case "2.2.4.0":
                    //set cacheAllImages to "false" - user can change it back if they wish or are directed to
                    Config.CacheAllImagesInMemory = false;
                    //anything else...?
                    break;
                case "2.2.6.0":
                case "2.2.7.0":
                case "2.2.8.0":
                case "2.2.9.0":
                    //set validationDelay to "0" - user can change it back if they wish or are directed to
                    //Config.ValidationDelay = 0; removed in future version
                    break;
                case "2.3.0.0":
                    //re-set plugin source if not already done by configurator...
                    MigratePluginSource();
                    break;
                case "2.3.1.0":
                case "2.3.2.0":
                case "2.5.0.0":
                case "2.5.1.0":
                case "2.5.2.0":
                case "2.5.3.0":
                    if (oldVersion <= new System.Version(2, 3, 0, 0))
                    {
                        MigratePluginSource(); //still may need to do this (if we came from earlier version than 2.3
                    }
                    if (oldVersion <= new System.Version(2, 3, 1, 0))
                    {
                        Config.EnableTraceLogging = true; //turn this on by default since we now have levels and retention/clearing
                        if (Config.MetadataCheckForUpdateAge < 30) Config.MetadataCheckForUpdateAge = 30; //bump this up
                        //we need to do a cache clear and full re-build (item guids may have changed)
                        if (MBServiceController.SendCommandToService(IPCCommands.ForceRebuild))
                        {
                            MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                            ev.Dialog(CurrentInstance.StringData("RebuildNecDial"), CurrentInstance.StringData("ForcedRebuildCapDial"), DialogButtons.Ok, 30, true);
                        }
                        else
                        {
                            MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                            ev.Dialog(CurrentInstance.StringData("RebuildFailedDial"), CurrentInstance.StringData("ForcedRebuildCapDial"), DialogButtons.Ok, 30, true);
                        }
                    }
                    else
                        if (oldVersion < new System.Version(2,5,0,0))
                    {
                        //upgrading from 2.3.2 - item migration should have already occurred...
                        Config.EnableTraceLogging = true; //turn this on by default since we now have levels and retention/clearing
                        var oldRepo = new ItemRepository();
                        Kernel.Instance.ItemRepository.MigrateDisplayPrefs(oldRepo);
                        //Async.Queue("Playstate Migration",() => Kernel.Instance.ItemRepository.MigratePlayState(oldRepo),15000); //delay to allow repo to load
                    }
                    break;
            }
            return true;
        }

        private void MigratePluginSource()
        {
                    try
                    {
                        Config.PluginSources.RemoveAt(Config.PluginSources.FindIndex(s => s.ToLower() == "http://www.mediabrowser.tv/plugins/plugin_info.xml"));
                    }
                    catch
                    {
                        //wasn't there - no biggie
                    }
                    if (Config.PluginSources.Find(s => s == "http://www.mediabrowser.tv/plugins/multi/plugin_info.xml") == null)
                    {
                        Config.PluginSources.Add("http://www.mediabrowser.tv/plugins/multi/plugin_info.xml");
                        Logger.ReportInfo("Plug-in Source migrated to multi-version source");
                    }
        }

        public bool IsInEntryPoint
        {
            get
            {
                return !String.IsNullOrEmpty(this.EntryPointPath);
            }
        }

        public void ReLoad()
        {
            //force a re-load of all our data
            this.RootFolderModel.RefreshChildren();
        }
           

        public void FullRefresh()
        {
            Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment.Dialog(CurrentInstance.StringData("ManualRefreshDial"),"", DialogButtons.Ok, 7, false);
            Async.Queue(CurrentInstance.StringData("Manual Full Refresh"), () => FullRefresh(RootFolder, MetadataRefreshOptions.Force));
        }

        void FullRefresh(Folder folder, MetadataRefreshOptions options)
        {
            Kernel.Instance.MajorActivity = true;
            Information.AddInformationString(CurrentInstance.StringData("FullRefreshMsg"));
            folder.RefreshMetadata(options);

            using (new Profiler(CurrentInstance.StringData("FullValidationProf")))
            {
                RunActionRecursively(folder, item =>
                {
                    Folder f = item as Folder;
                    if (f != null) f.ValidateChildren();
                });
            }

            using (new Profiler(CurrentInstance.StringData("FastRefreshProf")))
            {
                RunActionRecursively(folder, item => item.RefreshMetadata(MetadataRefreshOptions.FastOnly));
            }

            using (new Profiler(CurrentInstance.StringData("SlowRefresh")))
            {
                RunActionRecursively(folder, item => item.RefreshMetadata(MetadataRefreshOptions.Default));
            }

            Information.AddInformationString(CurrentInstance.StringData("FullRefreshFinishedMsg"));
            Kernel.Instance.MajorActivity = false;
        }

        void RunActionRecursively(Folder folder, Action<BaseItem> action)
        {
            action(folder);
            foreach (var item in folder.RecursiveChildren.OrderByDescending(i => i.DateModified))
            {
                action(item);
            }
        }

        Boolean PlayStartupAnimation = true;

        public Boolean CanPlayStartup()
        {
            if (PlayStartupAnimation)
            {
                PlayStartupAnimation = false;
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool displayPopupPlay = false;
        public bool DisplayPopupPlay
        {
            get
            {
                return this.displayPopupPlay;
            }
            set
            {
                this.displayPopupPlay = value;
                FirePropertyChanged("DisplayPopupPlay");
            }
        }

        private bool showNowPlaying = false;
        public bool ShowNowPlaying
        {
            get { return this.showNowPlaying && (MediaCenterEnvironment.MediaExperience != null); }
            set { if (showNowPlaying != value) { showNowPlaying = value; FirePropertyChanged("ShowNowPlaying"); } }
        }


        public string NowPlayingText
        {
            get
            {
                string showName = string.Empty;

                try
                {
                    MediaCenterEnvironment env = MediaCenterEnvironment;
                    Microsoft.MediaCenter.MediaExperience exp = env.MediaExperience;

                    if (exp == null)
                    {
                        return "Unknown";
                    }

                    MediaMetadata metadata = exp.MediaMetadata;

                    if (metadata == null)
                    {
                        return "Unknown";
                    }

                    string name = null;

                    // the API works in win7 and is borked on Vista.
                    if (metadata.ContainsKey("Name"))
                    {
                        name = metadata["Name"] as string;
                        if (name != null && name.Contains(".wpl"))
                        {
                            int start = name.LastIndexOf('/') + 1;
                            if (start < 0) start = 0;
                            int finish = name.LastIndexOf(".wpl");
                            name = name.Substring(start, finish - start);
                        }
                        else
                        {
                            if (name.StartsWith("dvd"))
                            {
                                int start = name.LastIndexOf('/') + 1;
                                if (start < 0) start = 0;
                                name = name.Substring(start);
                            }
                            else
                            {
                                name = null;
                            }
                        }
                    }

                    showName = name ?? metadata["Title"] as string;

                    // playlist fix {filename without extension)({playlist name})
                    int lastParan = showName.LastIndexOf('(');
                    if (lastParan > 0)
                    {
                        showName = showName.Substring(lastParan + 1, showName.Length - (lastParan + 2)).Trim();
                    }

                }
                catch (Exception e)
                {
                    showName = "Unknown";
                    Logger.ReportException("Something strange happend while getting media name, please report to community.mediabrowser.tv", e);
                    // never crash here

                }
                return showName;
            }
        }

        private Boolean isMouseActive = false;
        public Boolean IsMouseActive
        {
            get { return isMouseActive; }
            set
            {
                if (isMouseActive != value)
                {
                    isMouseActive = value;
                    FirePropertyChanged("IsMouseActive");
                }
            }
        }

        void mouseActiveHooker_MouseActive(IsMouseActiveHooker m, MouseActiveEventArgs e)
        {
            this.IsMouseActive = e.MouseActive;
        }

        public string BreadCrumbs
        {
            get
            {
                return session.Breadcrumbs;
            }
        }

        public void ClearCache()
        {
            MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
            DialogResult r = ev.Dialog(CurrentInstance.StringData("ClearCacheDial"), CurrentInstance.StringData("ClearCacheCapDial"), DialogButtons.Yes | DialogButtons.No, 60, true);
            if (r == DialogResult.Yes)
            {
                bool ok = Kernel.Instance.ItemRepository.ClearEntireCache();
                if (!ok)
                {
                    ev.Dialog(string.Format(CurrentInstance.StringData("ClearCacheErrorDial"), ApplicationPaths.AppCachePath), CurrentInstance.StringData("Errorstr"), DialogButtons.Ok, 60, true);
                }
                else
                {
                    ev.Dialog(CurrentInstance.StringData("RestartMBDial"), CurrentInstance.StringData("CacheClearedDial"), DialogButtons.Ok, 60, true);
                }
                Microsoft.MediaCenter.Hosting.AddInHost.Current.ApplicationContext.CloseApplication();
            }
        }

        public void ResetConfig()
        {
            MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
            DialogResult r = ev.Dialog(CurrentInstance.StringData("ResetConfigDial"), CurrentInstance.StringData("ResetConfigCapDial"), DialogButtons.Yes | DialogButtons.No, 60, true);
            if (r == DialogResult.Yes)
            {
                Config.Instance.Reset();
                ev.Dialog(CurrentInstance.StringData("RestartMBDial"), CurrentInstance.StringData("ConfigResetDial"), DialogButtons.Ok, 60, true);
                Microsoft.MediaCenter.Hosting.AddInHost.Current.ApplicationContext.CloseApplication();
            }
        }

        public void OpenConfiguration(bool showFullOptions)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["Application"] = this;
            properties["ShowFull"] = showFullOptions;

            if (session != null)
            {
                session.GoToPage("resx://MediaBrowser/MediaBrowser.Resources/ConfigPage", properties);
            }
            else
            {
                Logger.ReportError("Session is null in OpenPage");
            }
        }


        // accessed from Item
        internal void OpenExternalPlaybackPage(Item item)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["Application"] = this;
            properties["Item"] = item;

            if (session != null)
            {
                session.GoToPage("resx://MediaBrowser/MediaBrowser.Resources/ExternalPlayback", properties);
            }
            else
            {
                Logger.ReportError("Session is null in OpenExternalPlaybackPage");
            }
        }

        public FolderModel CurrentFolder; //used to keep track of the current folder so we can update the UI if needed
        public FolderModel RootFolderModel; //used to keep track of root folder as foldermodel for same reason

        public FolderModel CurrentFolderModel
        {
            get { return CurrentFolder; }
        }

        private void OpenFolderPage(FolderModel folder)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["Application"] = this;
            properties["Folder"] = folder;
            properties["ThemeConfig"] = CurrentTheme.Config;
            CurrentFolder = folder; //store our current folder
            CurrentItem = null; //blank this out in case it was messed with in the last screen

            if (folder.IsRoot)
                RootFolderModel = folder; //store the root as well

            if (session != null)
            {
                folder.NavigatingInto();

                session.GoToPage(folder.Folder.CustomUI ?? CurrentTheme.FolderPage, properties);
            }
            else
            {
                Logger.ReportError("Session is null in OpenPage");
            }
        }

        private Folder GetStartingFolder(BaseItem item)
        {
            Index currentIndex = item as Index;
            return currentIndex ?? (Folder)RootFolder;
        }

        void NavigateToActor(Item item)
        {
            var person = item.BaseItem as Person;
            Folder searchStart = GetStartingFolder(item.BaseItem.Parent);

            var index = searchStart.Search(
                ShowFinder(show => show.Actors == null ? false :
                    show.Actors.Exists(a => a.Name == person.Name)),
                    person.Name);

            index.Name = item.Name;

            Navigate(ItemFactory.Instance.Create(index));
        }


        public void NavigateToGenre(string genre, Item currentMovie)
        {
            var searchStart = GetStartingFolder(currentMovie.BaseItem.Parent);

            var index = searchStart.Search(
                ShowFinder(show => show.Genres == null ? false : show.Genres.Contains(genre)),
                genre);

            index.Name = genre;

            Navigate(ItemFactory.Instance.Create(index));
        }


        public void NavigateToDirector(string director, Item currentMovie)
        {

            var searchStart = GetStartingFolder(currentMovie.BaseItem.Parent);

            var index = searchStart.Search(
                ShowFinder(show => show.Directors == null ? false : show.Directors.Contains(director)),
                director);

            index.Name = director;

            Navigate(ItemFactory.Instance.Create(index));
        }



        Func<BaseItem, bool> ShowFinder(Func<IShow, bool> func)
        {
            return i => i is IShow ? func((i as IShow)) : false;
        }

        public void Navigate(Item item)
        {
            currentContextMenu = null; //any sort of navigation should reset our context menu so it will properly re-evaluate on next ref
            
            if (item.BaseItem is Person)
            {
                NavigateToActor(item);
                return;
            }

            if (item.BaseItem is Show)
            {
                if ((item.HasDataForDetailPage && item.BaseItem is Movie) ||
                    this.Config.AlwaysShowDetailsPage)
                {
                    // go to details screen 
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties["Application"] = this;
                    properties["Item"] = item;
                    properties["ThemeConfig"] = CurrentTheme.Config;
                    
                    session.GoToPage(item.BaseItem.CustomUI ?? CurrentTheme.DetailPage, properties);

                    return;
                }
            }


            MediaBrowser.Library.FolderModel folder = item as MediaBrowser.Library.FolderModel;
            if (folder != null)
            {
                if (!Config.Instance.RememberIndexing)
                {
                    folder.DisplayPrefs.IndexBy = MediaBrowser.Library.Localization.LocalizedStrings.Instance.GetString("NoneDispPref");
                }
                if (Config.Instance.AutoEnterSingleDirs && (folder.Folder.Children.Count == 1))
                {
                    if (folder.IsRoot) //special breadcrumb if we are going from a single item root
                        session.AddBreadcrumb("DIRECTENTRY");
                    else
                        session.AddBreadcrumb(folder.Name);
                    folder.NavigatingInto(); //make sure we validate
                    Navigate(folder.Children[0]);
                }
                else
                {
                    //call secured method if folder is protected
                    if (!folder.ParentalAllowed)
                        NavigateSecure(folder);
                    else
                        OpenFolderPage(folder);
                }
            }
            else
            {
                Resume(item);
            }
        }


        public void NavigateSecure(FolderModel folder)
        {
            //just call method on parentalControls - it will callback if secure
            Kernel.Instance.ParentalControls.NavigateProtected(folder);
        }

        public void OpenSecure(FolderModel folder)
        {
            //called if passed security
            OpenFolderPage(folder);
        }

        public void OpenSecurityPage(object prompt)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>();
            properties["Application"] = this;
            properties["PromptString"] = prompt;
            this.RequestingPIN = true; //tell page we are calling it (not a back action)
            session.GoToPage("resx://MediaBrowser/MediaBrowser.Resources/ParentalPINEntry", properties);
        }

        public void OpenMCMLPage(string page, Dictionary<string, object> properties)
        {
            currentContextMenu = null; //good chance this has happened as a result of a menu item selection so be sure this is reset
            Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => session.GoToPage(page, properties));
        }

        /// <summary>
        /// Takes an item and plays all items within the same folder, starting with the supplied Item
        /// </summary>
        public void PlayFolderBeginningWithItem(Item item)
        {
            Folder folder = item.PhysicalParent.BaseItem as Folder;

            IEnumerable<BaseItem> items = folder.RecursiveMedia.SkipWhile(v => v.Id != item.Id).Select(v => v as BaseItem);
            Play(items, false, false, false, false);

        }

        /// <summary>
        /// Plays an item while shuffling it's contents
        /// </summary>
        public void Shuffle(Item item)
        {
            Play(item, false, false, true, true);
        }

        /// <summary>
        /// Plays the first unwatched item within a Folder
        /// </summary>
        public void Unwatched(Item item)
        {
            Folder folder = item.BaseItem as Folder;

            Media firstUnwatched = folder.RecursiveMedia.Where(v => v != null && v.ParentalAllowed && !v.PlaybackStatus.WasPlayed).OrderBy(v => v.Path).FirstOrDefault();

            if (firstUnwatched != null)
            {
                Item unwatchedItem = (item as FolderModel).Children.Where(v => v.BaseItem == firstUnwatched).FirstOrDefault();

                Play(unwatchedItem, false, false, true, false);
            }
        }

        /// <summary>
        /// Queues an item for playback
        /// </summary>
        public void AddToQueue(Item item)
        {
            Play(item, true);
        }

        /// <summary>
        /// Plays an Item
        /// </summary>
        public void Play(Item item)
        {
            Play(item, false);
        }

        /// <summary>
        /// Plays all trailers for an Item
        /// </summary>
        public void PlayLocalTrailer(Item item)
        {
            var movie = item.BaseItem as Movie;
            if (movie.ContainsTrailers)
            {
                var trailerFiles = movie.TrailerFiles.ToArray();

                PlayableItem playable = PlayableItemFactory.Instance.Create(trailerFiles);

                Play(playable, false);
            }
        }

        /// <summary>
        /// Plays an Item
        /// </summary>
        /// <param name="queue">Whether the item should be queued</param>
        public void Play(Item item, bool queue)
        {
            Play(item, false, queue, true, false);
        }

        public void Play(Item item, bool resume, bool queue, bool intros, bool shuffle)
        {
            if (item.IsFolder)
            {
                Folder folder = item.BaseItem as Folder;

                Play(folder, resume, queue, intros, shuffle);
            }

            else if (item.IsPlayable)
            {
                Play(new BaseItem[] { item.BaseItem }, resume, queue, intros, shuffle);
            }
        }

        /// <summary>
        /// Resumes an Item
        /// </summary>
        public void Resume(Item item)
        {
            Play(item, true, false, false, false);
        }

        public void Play(Folder folder, bool resume, bool queue, bool intros, bool shuffle)
        {
            PlayableItem playable = PlayableItemFactory.Instance.Create(folder);
        
            playable.QueueItem = queue;
            playable.Shuffle = shuffle;
            playable.Resume = resume;

            Play(playable, intros);
        }

        public void Play(IEnumerable<BaseItem> items, bool resume, bool queue, bool intros, bool shuffle)
        {
            PlayableItem playable = PlayableItemFactory.Instance.Create(items.Select(i => i as Media));
           
            playable.QueueItem = queue;
            playable.Shuffle = shuffle;
            playable.Resume = resume;
          
            Play(playable, intros);
        }

        public void Play(PlayableItem playable, bool intros)
        {
            if (Config.Instance.ParentalControlEnabled && !playable.ParentalAllowed)
            {
                Application.CurrentInstance.DisplayPopupPlay = false; //PIN screen mucks with turning this off
                Kernel.Instance.ParentalControls.PlayProtected(playable, intros);
            }
            else
            {
                PlaySecure(playable, intros);
            }
        }

        internal void PlaySecure(PlayableItem playable, bool intros)
        {
            IEnumerable<Media> itemsToPlay = playable.GetPlayableMediaItems();
            BaseItem originalBaseItem = playable.PrimaryBaseItem;
            Item originalItem = ItemFactory.Instance.Create(originalBaseItem);

            //put this on a thread so that we can run it sychronously, but not tie up the UI
            MediaBrowser.Library.Threading.Async.Queue("Play Action", () =>
            {
                // Run all pre-play processes
                if (!RunPrePlayProcesses(originalItem, intros))
                {
                    // Abort playback if one of them returns false
                    return;
                }

                if (!playable.QueueItem)
                {
                    if (originalItem != null && PlaybackController != playable.PlaybackController && playable.PlaybackController.RequiresExternalPage)
                    {
                        OpenExternalPlaybackPage(originalItem);
                    }

                    //async this so it doesn't slow us down if the service isn't responding for some reason
                    MediaBrowser.Library.Threading.Async.Queue("Cancel Svc Refresh", () =>
                    {
                        MBServiceController.SendCommandToService(IPCCommands.CancelRefresh); //tell service to stop
                    });
                }

                currentPlaybackController = playable.PlaybackController;

                playable.Play();

                // Add each item to the newly watched list
                foreach (BaseItem baseItem in itemsToPlay)
                {
                    Logger.ReportVerbose("Adding newly watched for: " + baseItem.Name);
                    ItemFactory.Instance.Create(baseItem).AddNewlyWatched();
                }

                // Set last played if not null, which could happen if playback is path-based (e.g. trailers)
                if (originalItem != null)
                {
                    Logger.ReportVerbose("Setting last played to: " + originalItem.Name);
                    this.lastPlayed = originalItem;
                }
            });
        }

        public bool RunPrePlayProcesses(Item item, bool intros)
        {
            Logger.ReportInfo("Running pre-play processes for: " + item.Name);

            foreach (Kernel.PrePlayProcess process in Kernel.Instance.PrePlayProcesses)
            {
                if (!process(item, intros)) return false;
            }

            return true;
        }

        public void RunPostPlayProcesses()
        {
            //Logger.ReportInfo("Running post-play processes");
            foreach (Kernel.PostPlayProcess process in Kernel.Instance.PostPlayProcesses)
            {
                process();
            }
        }

        public void UnlockPC()
        {
            Kernel.Instance.ParentalControls.Unlock();
        }
        public void RelockPC()
        {
            Kernel.Instance.ParentalControls.Relock();
        }

        public bool RequestingPIN { get; set; } //used to signal the app that we are asking for PIN entry

        public void EnterNewParentalPIN()
        {
            Kernel.Instance.ParentalControls.EnterNewPIN();
        }
        public string CustomPINEntry { get; set; } //holds the entry for a custom pin (entered by user to compare to pin)

        public void ParentalPINEntered()
        {
            RequestingPIN = false;
            Kernel.Instance.ParentalControls.CustomPINEntered(CustomPINEntry);
        }
        public void BackToRoot()
        {
            //be sure we are on the app thread for session access
            if (Microsoft.MediaCenter.UI.Application.ApplicationThread != System.Threading.Thread.CurrentThread)
            {
                Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => BackToRoot());
                return;
            }
            //back up the app to the root page - used when library re-locks itself
            while (session.BackPage()) { };
        }

        public string DescString(string name)
        {
            //get the description string for "name" out of our string data object
            //we need to translate the content of our item to the field name so that the
            //description field name can be the same across languages
            string key = Kernel.Instance.StringData.GetKey(name.Trim());
            if (string.IsNullOrEmpty(key))
            {
                //probably a string file that has not been updated
                key = name;
                key = key.Replace(" ", "");
                key = key.Replace("*", "");
                key = key.Replace(")", "");
                key = key.Replace(")", "");
                key = key.Replace("-", "");
            }
            return Kernel.Instance.StringData.GetString(key.Trim() + "Desc");
        }

        public static void DisplayDialog(string message, string caption)
        {
            DisplayDialog(message, caption, DialogButtons.Ok, 10);
        }


        public static DialogResult DisplayDialog(string message, string caption, DialogButtons buttons, int timeout)
        {
            // We won't be able to take this during a page transition.  This is good!
            // Conversly, no new pages can be navigated while this is present.
            lock (syncObj)
            {
                DialogResult result = MediaCenterEnvironment.Dialog(message, caption, buttons, timeout, true);
                return result;
            }
        }

        public string AppVersion
        {
            get { return Kernel.Instance.VersionStr; }
        }

        private Information _information = new Information();
        public Information Information
        {
            get
            {
                return _information;
            }
            set
            {
                _information = value;
            }
        }


        public string MainBackdrop
        {
            get
            {
                string pngImage = this.Config.InitialFolder + "\\backdrop.png";
                string jpgImage = this.Config.InitialFolder + "\\backdrop.jpg";

                if (!string.IsNullOrEmpty(_background))
                {
                    return _background;
                }
                else
                {
                    if (File.Exists(pngImage))
                    {
                        _background = "file://" + pngImage;
                        return _background;
                    }
                    else if (File.Exists(jpgImage))
                    {
                        _background = "file://" + jpgImage;
                        return _background;
                    }
                    else
                        return null;
                }
            }
        }

        /// <summary>
        /// Mounts an iso and returns a string containing the mounted path
        /// </summary>
        public string MountISO(string path)
        {
            try
            {
                string command = Config.Instance.DaemonToolsLocation;

                // vcdmount simply calls Daemon.exe and exits immediately, which tricks this method into thinking the mount has completed.
                // Since VirtualCloneDrive installs Daemon.exe in the same directory, use that if it exists.
                if (command.ToLower().EndsWith("vcdmount.exe"))
                {
                    string daemonPath = Path.Combine(Path.GetDirectoryName(command), "Daemon.exe");

                    if (File.Exists(daemonPath))
                    {
                        Logger.ReportVerbose("Using Daemon.exe instead of vcdmount.exe");
                        command = daemonPath;
                    }
                }

                // Create the process start information.
                Process process = new Process();
                //virtualclonedrive
                if (command.ToLower().EndsWith("vcdmount.exe"))
                    process.StartInfo.Arguments = "-mount \"" + path + "\"";
                //alcohol120 or alcohol52
                else if (command.ToLower().EndsWith("axcmd.exe"))
                    process.StartInfo.Arguments = Config.Instance.DaemonToolsDrive + ":\\ /M:\"" + path + "\"";
                //deamontools
                else
                    process.StartInfo.Arguments = "-mount 0,\"" + path + "\"";
                process.StartInfo.FileName = command;
                process.StartInfo.ErrorDialog = false;
                process.StartInfo.CreateNoWindow = true;

                // We wait for exit to ensure the iso is completely loaded.
                process.Start();
                process.WaitForExit();

                // Play the DVD video that was mounted.
                return Config.Instance.DaemonToolsDrive + ":\\";
            }
            catch (Exception)
            {
                // Display the error in this case, they might wonder why it didn't work.
                Application.DisplayDialog("ISO Mounter is not correctly configured.", "Could not load ISO");
                throw (new Exception("ISO Mounter is not configured correctly"));
            }
        }

        /// <summary>
        /// Use this to return to media browser after launching another wmc application
        /// Example: Internal WMC dvd player
        /// </summary>
        public void ReturnToApplication()
        {
            Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ =>
            {
                Microsoft.MediaCenter.Hosting.ApplicationContext context = Microsoft.MediaCenter.Hosting.AddInHost.Current.ApplicationContext;

                if (!context.IsForegroundApplication)
                {
                    Logger.ReportVerbose("Ensuring MB is front-most app");
                    context.ReturnToApplication();
                }

            });
        }

        /// <summary>
        /// Tells all registered PlayBackControllers to stop playback
        /// </summary>
        public void StopAllPlayback()
        {
            foreach (IPlaybackController controller in Kernel.Instance.PlaybackControllers)
            {
                if (controller.IsPlaying)
                {
                    controller.Stop();
                }
            }
        }

        /// <summary>
        /// This is a helper to update Playstate for an item.
        /// It honors all of the various resume options within configuration.
        /// Play count will be incremented if the last played date doesn't match what's currently in the object
        /// </summary>
        public void UpdatePlayState(Media media, PlaybackStatus playstate, int playlistPosition, long positionTicks, long? duration, DateTime datePlayed)
        {
            // Increment play count if dates don't match
            bool incrementPlayCount = !playstate.LastPlayed.Equals(datePlayed);

            // The player didn't report the duration, see if we have it in metadata
            if (!duration.HasValue || duration == 0 && media != null)
            {
                duration = TimeSpan.FromMinutes(media.RunTime).Ticks;
            }

            // If we know the duration then enforce MinResumePct/MaxResumePct
            if (duration.HasValue && duration > 0 && positionTicks > 0)
            {
                decimal pctIn = Decimal.Divide(positionTicks, duration.Value) * 100;

                // Don't track in very beginning
                if (pctIn < Config.Instance.MinResumePct)
                {
                    positionTicks = 0;

                    if (playlistPosition == 0)
                    {
                        incrementPlayCount = false;
                    }
                }

                // If we're at the end, assume completed
                if (pctIn > Config.Instance.MaxResumePct || positionTicks >= duration)
                {
                    positionTicks = 0;
                }
            }

            // If resume is disabled reset positions to 0
            if (!MediaBrowser.Library.Kernel.Instance.ConfigData.EnableResumeSupport)
            {
                positionTicks = 0;
                playlistPosition = 0;
            }
            
            playstate.PositionTicks = positionTicks;
            playstate.PlaylistPosition = playlistPosition;

            if (incrementPlayCount)
            {
                playstate.LastPlayed = datePlayed;
                playstate.PlayCount++;
            }

            if (duration == 0 || (duration / TimeSpan.TicksPerMinute) >= Config.Instance.MinResumeDuration)
            {
                Logger.ReportVerbose("Playstate saved at position: " + new TimeSpan(positionTicks).ToString() + " with playlist position: " + playlistPosition);
                Kernel.Instance.SavePlayState(media, playstate);
            }
        }
    }
}
