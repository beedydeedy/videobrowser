﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Library.Configuration;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.Library.Threading;
using Microsoft.MediaCenter.Hosting;

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

        protected override void PlayMediaInternal(PlayableItem playable)
        {
            // Two different launch methods depending on how the player is configured
            if (LaunchType == ConfigData.ExternalPlayerLaunchType.WMCNavigate)
            {
                PlayUsingWMCNavigation(playable);

                OnExternalPlayerLaunched(playable);
            }
            else
            {
                PlayUsingCommandLine(playable);
            }           
        }

        // Launch the external player using the command line
        private void PlayUsingCommandLine(PlayableItem playable)
        {
            string commandPath = GetCommandPath(playable);
            string commandArgs = GetCommandArguments(playable);

            Logging.Logger.ReportInfo("Starting command line " + commandPath + " " + commandArgs);

            Process player = Process.Start(commandPath, commandArgs);

            Async.Queue("Ext Player Mgmt", () => ManageExtPlayer(player, playable));
        }

        private void ManageExtPlayer(Process player, PlayableItem playable)
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
            Async.Queue("Ext Player Focus", () => GiveFocusToExtPlayer(player, playable));

            //and wait for it to exit
            player.WaitForExit();

            //now restore MCE 
            wp.showCmd = 1; // 1 - Normal; 2 - Minimize; 3 - Maximize;
            SetWindowPlacement(mceWnd, ref wp);
            ExternalSplashForm.Hide();
            SetForegroundWindow(mceWnd);

            OnExternalPlayerClosed();
        }

        private void GiveFocusToExtPlayer(Process player, PlayableItem playable)
        {
            //set external player to foreground
            Logger.ReportVerbose("Giving focus to external player window");
            player.Refresh();
            player.WaitForInputIdle(5000); //give the external player 5 secs to show up and then minimize MCE
            OnExternalPlayerLaunched(playable);
            SetForegroundWindow(player.MainWindowHandle);
        }
        
        /// <summary>
        /// Play by launching another WMC app
        /// </summary>
        protected void PlayUsingWMCNavigation(PlayableItem playable)
        {
            string commandArgs = GetCommandArguments(playable);

            string url = GetCommandPath(playable);

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
        protected virtual void OnExternalPlayerLaunched(PlayableItem playable)
        {
        }

        protected virtual void OnExternalPlayerClosed()
        {
            // Just use base method
            OnPlaybackFinished(GetPlaybackState());
        }

        private string GetCommandArguments(PlayableItem playable)
        {
            List<string> argsList = GetCommandArgumentsList(playable);

            string args = string.Join(" ", argsList.ToArray());

            args = string.Format(args, GetFilePathCommandArgument(GetFilesToSendToPlayer(playable)));

            return args;
        }

        private IEnumerable<string> GetFilesToSendToPlayer(PlayableItem playable)
        {
            IEnumerable<string> files = playable.FilesFormattedForPlayer;

            if (playable.Resume)
            {
                files = files.Skip(playable.MediaItems.First().PlaybackStatus.PlaylistPosition);
            }

            if (files.Count() > 1)
            {
                if (!SupportsMultiFileCommandArguments && SupportsPlaylists)
                {
                    return new string[] { CreatePlaylistFile(files) };
                }
            }

            return files;

        }

        private string CreatePlaylistFile(IEnumerable<string> files)
        {
            string randomName = "pls_" + DateTime.Now.Ticks;
            string playListFile = Path.Combine(ApplicationPaths.AutoPlaylistPath, randomName + ".pls");

            StringBuilder contents = new StringBuilder("[playlist]\n");
            int x = 1;
            foreach (string file in files)
            {
                contents.Append("File" + x + "=" + file + "\n");
                contents.Append("Title" + x + "=Part " + x + "\n\n");
                x++;
            }
            contents.Append("Version=2\n");

            File.WriteAllText(playListFile, contents.ToString());
            return playListFile;
        }
        
        /// <summary>
        /// Formats the path to the media based on what the external player is expecting
        /// </summary>
        protected virtual string GetFilePathCommandArgument(IEnumerable<string> filesToPlay)
        {
            filesToPlay = filesToPlay = filesToPlay.Select(i => "\"" + i + "\"");

            return string.Join(" ", filesToPlay.ToArray());
        }

        /// <summary>
        /// Gets the watched state after playback has stopped.
        /// Subclasses will need to provide their own support for this.
        /// </summary>
        protected virtual PlaybackStateEventArgs GetPlaybackState()
        {
            return new PlaybackStateEventArgs() { Item = GetCurrentPlayableItem() };
        }

        protected virtual bool SupportsMultiFileCommandArguments
        {
            get
            {
                return false;
            }
        }

        protected virtual bool SupportsPlaylists
        {
            get
            {
                return true;
            }
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

        public override void GoToFullScreen()
        {
            
        }

        public override void Pause()
        {
            
        }

        public override void Seek(long position)
        {
            
        }

        protected abstract string GetCommandPath(PlayableItem playable);
        protected abstract List<string> GetCommandArgumentsList(PlayableItem playable);
    }
}
