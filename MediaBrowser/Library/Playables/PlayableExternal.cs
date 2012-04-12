using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using MediaBrowser.Library.Configuration;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Filesystem;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.Library.Threading;
using MediaBrowser.LibraryManagement;
using Microsoft.MediaCenter.Hosting;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Represents an abstract base class for all externally playable items
    /// </summary>
    public class PlayableExternal : PlayableMultiMediaVideo
    {
        #region Unmanaged methods
        //alesbal: begin
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        static extern bool SetWindowPlacement(IntPtr hWnd,
                           ref WINDOWPLACEMENT lpwndpl);
        private struct POINTAPI
        {
            public int x;
            public int y;
        }

        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINTAPI ptMinPosition;
            public POINTAPI ptMaxPosition;
            public RECT rcNormalPosition;
        }
        //alesbal: end
        #endregion

        #region CanPlay
        public override bool CanPlay(IEnumerable<string> files)
        {
            return ConfigData.CanPlay(ExternalPlayerConfiguration, files);
        }

        public override bool CanPlay(IEnumerable<Media> mediaList)
        {
            return ConfigData.CanPlay(ExternalPlayerConfiguration, mediaList);
        }

        public override bool CanPlay(Media media)
        {
            return CanPlay(new Media[] { media });
        }

        public override bool CanPlay(string path)
        {
            return CanPlay(new string[] { path });
        }
        #endregion
        
        /// <summary>
        /// If multiple files will be played we'll have to create a PLS file if they can't all be passed on the command line
        /// </summary>
        private string PlaylistFile { get; set; }

        /// <summary>
        /// Subclasses of PlayableExternal will need to override this
        /// </summary>
        protected virtual ConfigData.ExternalPlayerType ExternalPlayerType
        {
            get { return ConfigData.ExternalPlayerType.Generic; }
        }

        /// <summary>
        /// Gets the ExternalPlayer configuration for this instance
        /// </summary>
        public ConfigData.ExternalPlayer ExternalPlayerConfiguration
        {
            get;
            set;
        }

        protected override void Prepare()
        {
            base.Prepare();

            // Need to stop other players, in particular the internal 7MC player
            Application.CurrentInstance.StopAllPlayback();

            // Create a playlist if needed
            if (PlayableFiles.Count() > 1 && !ExternalPlayerConfiguration.SupportsMultiFileCommandArguments && ExternalPlayerConfiguration.SupportsPlaylists)
            {
                PlaylistFile = CreatePlaylist(Resume);
            }
        }

        protected override void SendFilesToPlayer(PlaybackArguments args)
        {
            // Two different launch methods depending on how the player is configured
            if (ExternalPlayerConfiguration.LaunchType == ConfigData.ExternalPlayerLaunchType.WMCNavigate)
            {
                PlayUsingWMCNavigation(args);

                OnExternalPlayerLaunched(args);
            }
            else
            {
                PlayUsingCommandLine(args);
            }
        }

        // Launch the external player using the command line
        private void PlayUsingCommandLine(PlaybackArguments args)
        {
            string commandArgs = GetCommandArguments(args);

            Logging.Logger.ReportInfo("Starting command line " + ExternalPlayerConfiguration.Command + " " + commandArgs);

            Process player = Process.Start(ExternalPlayerConfiguration.Command, commandArgs);
                        
            Async.Queue("Ext Player Mgmt", () => ManageExtPlayer(player, ExternalPlayerConfiguration, args));
        }

        private void ManageExtPlayer(Process player, ConfigData.ExternalPlayer configuredPlayer, PlaybackArguments playbackInfo)
        {

            //minimize MCE if indicated
            IntPtr mceWnd = FindWindow(null, "Windows Media Center");
            WINDOWPLACEMENT wp = new WINDOWPLACEMENT();
            GetWindowPlacement(mceWnd, ref wp);

            if (configuredPlayer.ShowSplashScreen)
            {
                //throw up a form to cover the desktop if we minimize and we are in the primary monitor
                if (System.Windows.Forms.Screen.FromHandle(mceWnd).Primary)
                {
                    ExternalSplashForm.Display(Application.CurrentInstance.ExtSplashBmp);
                }
            }

            if (configuredPlayer.MinimizeMCE)
            {
                wp.showCmd = 2; // 1 - Normal; 2 - Minimize; 3 - Maximize;
                SetWindowPlacement(mceWnd, ref wp);
            }
            
            //give the player focus
            Async.Queue("Ext Player Focus", () => GiveFocusToExtPlayer(player, playbackInfo));

            //and wait for it to exit
            player.WaitForExit();

            //now restore MCE 
            wp.showCmd = 1; // 1 - Normal; 2 - Minimize; 3 - Maximize;
            SetWindowPlacement(mceWnd, ref wp);
            ExternalSplashForm.Hide();
            SetForegroundWindow(mceWnd);

            OnExternalPlayerClosed();
        }

        protected void OnExternalPlayerClosed()
        {
            // Just use base method
            OnPlaybackFinished(null, GetPlaybackState(PlayableFiles));
            
            Application.CurrentInstance.RunPostPlayProcesses();
        }

        /// <summary>
        /// Play by launching another WMC app
        /// </summary>
        protected void PlayUsingWMCNavigation(PlaybackArguments args)
        {
            string commandArgs = GetCommandArguments(args);

            string url = ExternalPlayerConfiguration.Command;

            if (!string.IsNullOrEmpty(commandArgs))
            {
                url += "?" + commandArgs;
            }

            Logging.Logger.ReportInfo("Navigating within WMC to " + url);

            AddInHost.Current.MediaCenterEnvironment.NavigateToPage(Microsoft.MediaCenter.PageId.ExtensibilityUrl, url);
        }

        private IEnumerable<string> GetFilesToSendToPlayer(bool resume)
        {
            // If playback is based off a path
            if (PlayableMediaItems.Count() == 0)
            {
                return GetFilesToSendToPlayer(null, PlayableFiles, resume);
            }

            List<string> files = new List<string>();

            for (int i = 0; i < PlayableMediaItems.Count(); i++)
            {
                Media media = PlayableMediaItems.ElementAt(i);

                files.AddRange(GetFilesToSendToPlayer(media, media.Files, resume));

                // Only allow resume on first Media object
                resume = false;
            }

            return files;

        }

        protected virtual IEnumerable<string> GetFilesToSendToPlayer(Media media, IEnumerable<string> files, bool resume)
        {
            Video video = media as Video;

            if (video != null && video.MediaType == MediaType.ISO && video.MediaLocation is IFolderMediaLocation)
            {
                files = Helper.GetIsoFiles(video.Path);
            }

            PlaybackStatus playstate = null;

            if (media != null)
            {
                playstate = media.PlaybackStatus;
            }

            return resume && playstate != null ? files.Skip(playstate.PlaylistPosition) : files;

        }

        /// <summary>
        /// Gets the watched state after playback has stopped.
        /// Subclasses will need to provide their own support for this.
        /// </summary>
        protected virtual PlaybackStateEventArgs GetPlaybackState(IEnumerable<string> files)
        {
            return new PlaybackStateEventArgs() { Position = 0, PlaylistPosition = 0, PlayableItemId = PlayableItemId };
        }

        private void GiveFocusToExtPlayer(Process player, PlaybackArguments playbackInfo)
        {
            //set external player to foreground
            player.Refresh();
            player.WaitForInputIdle(5000); //give the external player 5 secs to show up and then minimize MCE
            OnExternalPlayerLaunched(playbackInfo);
            SetForegroundWindow(player.MainWindowHandle);
        }

        /// <summary>
        /// Subclasses can use this to execute code after the player has launched
        /// </summary>
        /// <param name="playbackInfo"></param>
        protected virtual void OnExternalPlayerLaunched(PlaybackArguments playbackInfo)
        {
        }

        protected override void OnProgress(object sender, PlaybackStateEventArgs e)
        {
            // Just use base method if multiple media items are not involved
            if (PlayableMediaItems.Count() < 2)
            {
                base.OnProgress(sender, e);
            }
            else
            {
                // Something else is currently playing
                if (IsPlaybackEventOnCurrentInstance(e))
                {
                    UpdateProgressForMultipleMediaItems(e);
                }

                HasUpdatedPlayState = true;
            }
        }

        /// <summary>
        /// Creates a PLS file based on the list of PlayableItems
        /// </summary>
        private string CreatePlaylist(bool resume)
        {
            string randomName = "pls_" + DateTime.Now.Ticks;
            string playListFile = Path.Combine(ApplicationPaths.AutoPlaylistPath, randomName + ".pls");

            StringBuilder contents = new StringBuilder("[playlist]\n");
            int x = 1;
            foreach (string file in GetFilesToSendToPlayer(resume))
            {
                contents.Append("File" + x + "=" + file + "\n");
                contents.Append("Title" + x + "=Part " + x + "\n\n");
                x++;
            }
            contents.Append("Version=2\n");

            File.WriteAllText(playListFile, contents.ToString());
            return playListFile;
        }

        private string GetCommandArguments(PlaybackArguments playInfo)
        {
            List<string> argsList = GetCommandArgumentsList(playInfo);

            string args = string.Join(" ", argsList.ToArray());

            args = string.Format(args, GetFilePathCommandArgument(GetFilesToSendToPlayer(playInfo.Resume)));

            return args;
        }

        protected virtual List<string> GetCommandArgumentsList(PlaybackArguments playInfo)
        {
            List<string> args = new List<string>();

            if (!string.IsNullOrEmpty(ExternalPlayerConfiguration.Args))
            {
                args.Add(ExternalPlayerConfiguration.Args);
            }

            return args;
        }

        /// <summary>
        /// Formats the path to the media based on what the external player is expecting
        /// </summary>
        protected virtual string GetFilePathCommandArgument(IEnumerable<string> filesToPlay)
        {
            if (!string.IsNullOrEmpty(PlaylistFile))
            {
                return "\"" + PlaylistFile + "\"";
            }
            
            filesToPlay = filesToPlay = filesToPlay.Select(i => "\"" + i + "\"");

            return string.Join(" ", filesToPlay.ToArray());
        }

        /// <summary>
        /// Goes through each Media object within PlayableMediaItems and updates Playstate for each individually
        /// </summary>
        private void UpdateProgressForMultipleMediaItems(PlaybackStateEventArgs state)
        {
            string currentFile = PlayableFiles.ElementAt(state.PlaylistPosition);

            int foundIndex = -1;

            // First find which media item we left off at
            for (int i = 0; i < PlayableMediaItems.Count(); i++)
            {
                if (PlayableMediaItems.ElementAt(i).Files.Contains(currentFile))
                {
                    foundIndex = i;
                }
            }

            // Go through each media item up until the current one and update playstate
            for (int i = 0; i <= foundIndex; i++)
            {
                Media media = PlayableMediaItems.ElementAt(i);

                // Perhaps not a resumable item
                if (media.PlaybackStatus == null)
                {
                    continue;
                }

                long currentPositionTicks = 0;
                int currentPlaylistPosition = 0;

                if (foundIndex == i)
                {
                    // If this is where playback is, update position and playlist
                    currentPlaylistPosition = media.Files.ToList().IndexOf(currentFile);
                    currentPositionTicks = state.Position;
                }

                Application.CurrentInstance.UpdatePlayState(media, media.PlaybackStatus, currentPlaylistPosition, currentPositionTicks, null, PlaybackStartTime);
            }

        }

        /// <summary>
        /// Gets the default configuration that will be pre-populated into the UI of the configurator.
        /// </summary>
        public virtual ConfigData.ExternalPlayer GetDefaultConfiguration()
        {
            ConfigData.ExternalPlayer config = new ConfigData.ExternalPlayer();

            config.ExternalPlayerType = ExternalPlayerType;
            config.LaunchType = ConfigData.ExternalPlayerLaunchType.CommandLine;
            config.SupportsPlaylists = true;
            config.SupportsMultiFileCommandArguments = false;
            config.ShowSplashScreen = true;
            config.MinimizeMCE = true;
            config.Args = "{0}";

            return config;
        }

    }
}
