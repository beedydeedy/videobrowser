using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.Library.Threading;
using Microsoft.MediaCenter.Hosting;
using MediaBrowser.Library.Logging;

namespace MediaBrowser.Library.Playables.ExternalPlayer
{
    public abstract class ExternalPlaybackController : BasePlaybackController
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

        protected override void PlayMediaInternal(PlaybackArguments args)
        {
            // Two different launch methods depending on how the player is configured
            if (LaunchType == ConfigData.ExternalPlayerLaunchType.WMCNavigate)
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
            string commandPath = GetCommandPath(args);
            string commandArgs = GetCommandArguments(args);

            Logging.Logger.ReportInfo("Starting command line " + commandPath + " " + commandArgs);

            Process player = Process.Start(commandPath, commandArgs);

            Async.Queue("Ext Player Mgmt", () => ManageExtPlayer(player, args));
        }

        private void ManageExtPlayer(Process player, PlaybackArguments playbackInfo)
        {
            //minimize MCE if indicated
            IntPtr mceWnd = FindWindow(null, "Windows Media Center");
            WINDOWPLACEMENT wp = new WINDOWPLACEMENT();
            GetWindowPlacement(mceWnd, ref wp);

            if (ShowSplashScreen)
            {
                //throw up a form to cover the desktop if we minimize and we are in the primary monitor
                if (System.Windows.Forms.Screen.FromHandle(mceWnd).Primary)
                {
                    ExternalSplashForm.Display(Application.CurrentInstance.ExtSplashBmp);
                }
            }

            if (MinimizeMCE)
            {
                Logger.ReportVerbose("Minimizing Windows Media Center");
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

        private void GiveFocusToExtPlayer(Process player, PlaybackArguments playbackInfo)
        {
            //set external player to foreground
            Logger.ReportVerbose("Giving focus to external player window");
            player.Refresh();
            player.WaitForInputIdle(5000); //give the external player 5 secs to show up and then minimize MCE
            OnExternalPlayerLaunched(playbackInfo);
            SetForegroundWindow(player.MainWindowHandle);
        }
        
        /// <summary>
        /// Play by launching another WMC app
        /// </summary>
        protected void PlayUsingWMCNavigation(PlaybackArguments args)
        {
            string commandArgs = GetCommandArguments(args);

            string url = GetCommandPath(args);

            if (!string.IsNullOrEmpty(commandArgs))
            {
                url += "?" + commandArgs;
            }

            Logging.Logger.ReportInfo("Navigating within WMC to " + url);

            AddInHost.Current.MediaCenterEnvironment.NavigateToPage(Microsoft.MediaCenter.PageId.ExtensibilityUrl, url);
        }

        /// <summary>
        /// Subclasses can use this to execute code after the player has launched
        /// </summary>
        protected virtual void OnExternalPlayerLaunched(PlaybackArguments playbackInfo)
        {
        }

        protected virtual void OnExternalPlayerClosed()
        {
            // Just use base method
            OnPlaybackFinished(GetPlaybackState());
        }

        private string GetCommandArguments(PlaybackArguments playInfo)
        {
            List<string> argsList = GetCommandArgumentsList(playInfo);

            string args = string.Join(" ", argsList.ToArray());

            args = string.Format(args, GetFilePathCommandArgument(GetFilesToSendToPlayer(playInfo)));

            return args;
        }

        protected virtual IEnumerable<string> GetFilesToSendToPlayer(PlaybackArguments playInfo)
        {
            IEnumerable<string> files = playInfo.Files;

            if (playInfo.Resume)
            {
                files = files.Skip(playInfo.PlaylistPosition);
            }

            return files;

        }
        
        /// <summary>
        /// Formats the path to the media based on what the external player is expecting
        /// </summary>
        protected virtual string GetFilePathCommandArgument(IEnumerable<string> filesToPlay)
        {
            /*if (!string.IsNullOrEmpty(PlaylistFile))
            {
                return "\"" + PlaylistFile + "\"";
            }*/

            filesToPlay = filesToPlay = filesToPlay.Select(i => "\"" + i + "\"");

            return string.Join(" ", filesToPlay.ToArray());
        }

        /// <summary>
        /// Gets the watched state after playback has stopped.
        /// Subclasses will need to provide their own support for this.
        /// </summary>
        protected virtual PlaybackStateEventArgs GetPlaybackState()
        {
            Guid playableItemId = Guid.Empty;

            PlaybackArguments playItem = GetCurrentPlaybackItem();

            if (playItem != null)
            {
                playableItemId = playItem.PlayableItemId;
            }

            return new PlaybackStateEventArgs() { PlayableItemId = playableItemId };
        }

        protected virtual bool ShowSplashScreen
        {
            get
            {
                return true;
            }
        }

        protected virtual bool MinimizeMCE
        {
            get
            {
                return true;
            }
        }

        protected virtual ConfigData.ExternalPlayerLaunchType LaunchType
        {
            get
            {
                return ConfigData.ExternalPlayerLaunchType.CommandLine;
            }
        }

        protected override void StopInternal()
        {
            
        }

        public override bool CanPlay(IEnumerable<string> files)
        {
            return false;
        }

        public override bool CanPlay(string filename)
        {
            return false;
        }

        public override void GoToFullScreen()
        {
            
        }

        public override void Pause()
        {
            
        }

        public override void Seek(long position)
        {
            
        }

        protected abstract string GetCommandPath(PlaybackArguments args);
        protected abstract List<string> GetCommandArgumentsList(PlaybackArguments playInfo);
    }
}
