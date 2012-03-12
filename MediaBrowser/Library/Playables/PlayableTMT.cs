using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;
using System.Diagnostics;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Represents an external player that uses the standalone TMT application
    /// </summary>
    class PlayableTMT : PlayableExternal
    {
        protected override ConfigData.ExternalPlayerType ExternalPlayerType
        {
            get { return ConfigData.ExternalPlayerType.TMT; }
        }

        /// <summary>
        /// Gets the watched state after playback has stopped.
        /// </summary>
        protected override PlaybackStateEventArgs GetPlaybackState(IEnumerable<string> files)
        {
            NameValueCollection infoValues = GetTMTInfoFileValues();

            return GetPlaybackState(infoValues);
        }

        protected override void OnCommandLinePlayerLaunched(PlaybackArguments playbackInfo)
        {
            base.OnCommandLinePlayerLaunched(playbackInfo);

            WaitForPlaybackToStartThenStop(playbackInfo);

            // Close the player
            ClosePlayer(ExternalPlayerConfiguration);
        }

        /// <summary>
        /// Waits for playback to begin, then executes a resume command, then waits for it to finally stop
        /// </summary>
        protected void WaitForPlaybackToStartThenStop(PlaybackArguments playbackInfo)
        {
            // Wait for playback to begin
            WaitForPlayState("play", false);

            if (playbackInfo.Resume)
            {
                ExecuteResumeCommand(playbackInfo.PlaylistPosition, playbackInfo.PositionTicks);
            }

            // Wait for it to stop, and start reporting progress
            WaitForPlayState("stop", true);
        }

        /// <summary>
        /// Waits until the TMTInfo.set file reports a certain play state
        /// </summary>
        private void WaitForPlayState(string stateToWaitFor, bool updateProgress)
        {
            NameValueCollection info = GetTMTInfoFileValues();

            if (info["State"] != stateToWaitFor)
            {
                Thread.Sleep(1000);

                if (updateProgress)
                {
                    OnProgress(null, GetPlaybackState(info));
                }

                WaitForPlayState(stateToWaitFor, updateProgress);
            }
        }
        
        /// <summary>
        /// Sends a command to the MMC console to close the player.
        /// Do not use this for the WMC add-in because it will close WMC
        /// </summary>
        private void ClosePlayer(ConfigData.ExternalPlayer externalPlayerConfiguration)
        {
            SendCommandToMMC("-close");
        }

        /// <summary>
        /// Tells the MMC console to resume playback where last left off for the current file
        /// </summary>
        private void ExecuteResumeCommand(int playlistPosition, long positionTicks)
        {
            // This doesn't actually work right now, but who knows. Perhaps TMT will fix it and this will start working.
            SendCommandToMMC("-resume");
        }

        /// <summary>
        /// Sends an arbitrary command to the TMT MMC console
        /// </summary>
        private void SendCommandToMMC(string command)
        {
            string directory = new FileInfo(ExternalPlayerConfiguration.Command).DirectoryName;
            string exe = Path.Combine(directory, "MMCEDT5.exe");

            // Best we can do for now
            ProcessStartInfo processInfo = new ProcessStartInfo(exe, command);
            processInfo.CreateNoWindow = true;

            Process.Start(processInfo);
        }

        /// <summary>
        /// Reads the TMTInfo.set file and returns the current play state
        /// </summary>
        private PlaybackStateEventArgs GetPlaybackState(NameValueCollection state)
        {
            return new PlaybackStateEventArgs()
            {
                Position = TimeSpan.Parse(state["CurTime"]).Ticks,
                PlaylistPosition = int.Parse(state["CurTitle"]),
                DurationFromPlayer = TimeSpan.Parse(state["TotalTime"]).Ticks,
                PlayableItemId = PlayableItemId
            };
        }

        /// <summary>
        /// Reads the TMTInfo.set file and returns the current play state
        /// </summary>
        private NameValueCollection GetTMTInfoFileValues()
        {
            string filePath = Path.Combine(ExternalPlayerConfiguration.DataFolderPath, "TMTInfo.set");

            return Helper.ParseIniFile(filePath);
        }

        /// <summary>
        /// Gets the default configuration that will be pre-populated into the UI of the configurator.
        /// </summary>
        public override ConfigData.ExternalPlayer GetDefaultConfiguration()
        {
            ConfigData.ExternalPlayer config = base.GetDefaultConfiguration();

            config.SupportsPlaylists = false;
            config.SupportsMultiFileCommandArguments = false;

            return config;
        }
    }
}
