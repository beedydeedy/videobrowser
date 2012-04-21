using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using MediaBrowser.Library.Configuration;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.Library.Threading;
using Microsoft.MediaCenter.Hosting;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Represents an abstract base class for all externally playable items
    /// </summary>
    public class PlayableExternal : PlayableItem
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
                PlaylistFile = CreatePlaylist();
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
            if (PlayableMediaItems.Count > 0)
            {
                OnPlaybackFinished(null, GetPlaybackState());
            }
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

        private IEnumerable<string> GetFilesToSendToPlayer()
        {
            IEnumerable<string> files = PlayableFiles;

            if (Resume)
            {
                Media media = PlayableMediaItems.FirstOrDefault();

                if (media != null && media.PlaybackStatus != null)
                {
                    files = files.Skip(media.PlaybackStatus.PlaylistPosition);
                }
            }

            return files;

        }

        /// <summary>
        /// Gets the watched state after playback has stopped.
        /// Subclasses will need to provide their own support for this.
        /// </summary>
        protected virtual PlaybackStateEventArgs GetPlaybackState()
        {
            return new PlaybackStateEventArgs() { PlayableItemId = PlayableItemId };
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

        /// <summary>
        /// Creates a PLS file based on the list of PlayableItems
        /// </summary>
        private string CreatePlaylist()
        {
            string randomName = "pls_" + DateTime.Now.Ticks;
            string playListFile = Path.Combine(ApplicationPaths.AutoPlaylistPath, randomName + ".pls");

            StringBuilder contents = new StringBuilder("[playlist]\n");
            int x = 1;
            foreach (string file in GetFilesToSendToPlayer())
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

            args = string.Format(args, GetFilePathCommandArgument(GetFilesToSendToPlayer()));

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
