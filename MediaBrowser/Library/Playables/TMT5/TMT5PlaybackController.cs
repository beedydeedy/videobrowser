using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Library.Playables.ExternalPlayer;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;

namespace MediaBrowser.Library.Playables.TMT5
{
    public class TMT5PlaybackController : ConfigurableExternalPlaybackController
    {
        // All of these hold state about what's being played. They're all reset when playback starts
        private bool _HasStartedPlaying = false;
        private FileSystemWatcher _StatusFileWatcher;
        private long _CurrentPositionTicks = 0;
        private long _CurrentDurationTicks = 0;
        private string _CurrentPlayState = string.Empty;

        // Protect against really aggressive event handling
        private DateTime _LastFileSystemUpdate = DateTime.Now;

        /// <summary>
        /// Gets the watched state after playback has stopped.
        /// </summary>
        protected override PlaybackStateEventArgs GetPlaybackState()
        {
            PlaybackStateEventArgs state = base.GetPlaybackState();

            state.DurationFromPlayer = _CurrentDurationTicks;
            state.Position = _CurrentPositionTicks;

            state.CurrentFileIndex = 0;

            return state;
        }

        /// <summary>
        /// Gets arguments to be passed to the command line.
        /// </summary>
        protected override List<string> GetCommandArgumentsList(PlayableItem playbackInfo)
        {
            List<string> args = new List<string>();

            args.Add("{0}");

            return args;
        }

        protected override void ResetPlaybackProperties()
        {
            base.ResetPlaybackProperties();

            _HasStartedPlaying = false;
            _CurrentPositionTicks = 0;
            _CurrentDurationTicks = 0;
            _CurrentPlayState = string.Empty;

            DisposeFileSystemWatcher();
        }

        protected override void OnExternalPlayerLaunched(PlayableItem playbackInfo)
        {
            base.OnExternalPlayerLaunched(playbackInfo);

            // If the playstate directory exists, start watching it
            if (Directory.Exists(PlayStateDirectory))
            {
                StartWatchingStatusFile();
            }
        }

        private void StartWatchingStatusFile()
        {
            Logging.Logger.ReportVerbose("Watching TMT folder: " + PlayStateDirectory);
            _StatusFileWatcher = new FileSystemWatcher(PlayStateDirectory, "*.set");

            // Need to include subdirectories since there are subfolders undearneath this with the TMT version #.
            _StatusFileWatcher.IncludeSubdirectories = true;

            _StatusFileWatcher.Changed += _StatusFileWatcher_Changed;
            _StatusFileWatcher.EnableRaisingEvents = true;
        }

        void _StatusFileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            NameValueCollection values;

            try
            {
                values = Helper.ParseIniFile(e.FullPath);
            }
            catch (IOException)
            {
                // This can happen if the file is being written to at the exact moment we're trying to access it
                // Unfortunately we kind of have to just eat it
                return;
            }

            string tmtPlayState = values["State"].ToLower();

            _CurrentPlayState = tmtPlayState;

            if (tmtPlayState == "play")
            {
                // Playback just started
                if (!_HasStartedPlaying)
                {
                    PlayableItem playItem = GetCurrentPlayableItem();

                    if (playItem.StartPositionTicks > 0 || playItem.StartPlaylistPosition > 0)
                    {
                        ExecuteResumeCommand(playItem.StartPlaylistPosition, playItem.StartPositionTicks);
                    }
                }

                _HasStartedPlaying = true;

                // Protect against really agressive calls
                var diff = (DateTime.Now - _LastFileSystemUpdate).TotalMilliseconds;

                if (diff < 1000 && diff >= 0)
                {
                    return;
                }

                _LastFileSystemUpdate = DateTime.Now;
            }

            // If playback has previously started...
            // First notify the Progress event handler
            // Then check if playback has stopped
            if (_HasStartedPlaying)
            {
                _CurrentDurationTicks = TimeSpan.Parse(values["TotalTime"]).Ticks;
                _CurrentPositionTicks = TimeSpan.Parse(values["CurTime"]).Ticks;

                OnProgress(GetPlaybackState());

                // Playback has stopped
                if (tmtPlayState == "stop")
                {
                    DisposeFileSystemWatcher();

                    // If using the command line player, send a command to the MMC console to close the player
                    if (ExternalPlayerConfiguration.LaunchType == ConfigData.ExternalPlayerLaunchType.CommandLine)
                    {
                        ClosePlayer();
                    }
                    else
                    {
                        // But we can't do that with the internal TMT player since it will shut down WMC
                        // So just notify the base class that playback stopped
                        OnExternalPlayerClosed();
                    }
                }
            }
        }

        private void DisposeFileSystemWatcher()
        {
            if (_StatusFileWatcher != null)
            {
                _StatusFileWatcher.EnableRaisingEvents = false;
                _StatusFileWatcher.Changed -= _StatusFileWatcher_Changed;
                _StatusFileWatcher.Dispose();
                _StatusFileWatcher = null;
            }
        }

        /// <summary>
        /// Tells the MMC console to resume playback where last left off for the current file
        /// </summary>
        private void ExecuteResumeCommand(int playlistPosition, long positionTicks)
        {
        }

        /// <summary>
        /// Sends an arbitrary command to the TMT MMC console
        /// </summary>
        protected void SendCommandToMMC(string command)
        {
            string directory = new FileInfo(ExternalPlayerConfiguration.Command).DirectoryName;
            string exe = Path.Combine(directory, "MMCEDT5.exe");

            // Best we can do for now
            ProcessStartInfo processInfo = new ProcessStartInfo(exe, command);
            processInfo.CreateNoWindow = true;

            Process.Start(processInfo);
        }

        private string PlayStateDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArcSoft");
            }
        }

        public override void Pause()
        {
            SendCommandToMMC("-pause");
        }

        protected override void StopInternal()
        {
            SendCommandToMMC("-stop");
        }

        public override void UnPause()
        {
            SendCommandToMMC("-play");
        }

        /// <summary>
        /// Sends a command to the MMC console to close the player.
        /// Do not use this for the WMC add-in because it will close WMC
        /// </summary>
        private void ClosePlayer()
        {
            SendCommandToMMC("-close");
        }

        public override bool IsPaused
        {
            get
            {
                return _CurrentPlayState == "pause";
            }
        }
    }
}
