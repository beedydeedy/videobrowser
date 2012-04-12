using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Represents an external player that uses the standalone TMT application
    /// </summary>
    public class PlayableTMT : PlayableExternal
    {
        // All of these hold state about what's being played. They're all reset when playback starts
        private bool _HasStartedPlaying = false;
        private PlaybackStateEventArgs _LastPlaybackState;
        private FileSystemWatcher _TMTInfoFileWatcher;

        private PlaybackArguments _PlaybackArguments;

        // Protect against really aggressive event handling
        private DateTime _LastFileSystemUpdate = DateTime.Now;

        protected override ConfigData.ExternalPlayerType ExternalPlayerType
        {
            get { return ConfigData.ExternalPlayerType.TMT; }
        }

        /// <summary>
        /// Gets the watched state after playback has stopped.
        /// </summary>
        protected override PlaybackStateEventArgs GetPlaybackState(IEnumerable<string> files)
        {
            return _LastPlaybackState ?? base.GetPlaybackState(files);
        }

        /// <summary>
        /// Gets arguments to be passed to the command line.
        /// </summary>
        protected override List<string> GetCommandArgumentsList(PlaybackArguments playbackInfo)
        {
            List<string> args = new List<string>();

            args.Add("{0}");

            return args;
        }
        
        protected override void OnExternalPlayerLaunched(PlaybackArguments playbackInfo)
        {
            base.OnExternalPlayerLaunched(playbackInfo);

            _PlaybackArguments = playbackInfo;

            _HasStartedPlaying = false;

            // If the playstate directory exists, start watching it
            if (Directory.Exists(PlayStateDirectory))
            {
                StartWatchingTMTInfoFile();
            }
        }

        private void StartWatchingTMTInfoFile()
        {
            Logging.Logger.ReportVerbose("Watching TMT folder: " + PlayStateDirectory);
            _TMTInfoFileWatcher = new FileSystemWatcher(PlayStateDirectory, "*.set");
            
            // Need to include subdirectories since there are subfolders undearneath this with the TMT version #.
            _TMTInfoFileWatcher.IncludeSubdirectories = true;

            _TMTInfoFileWatcher.Changed += _TMTInfoFileWatcher_Changed;
            _TMTInfoFileWatcher.EnableRaisingEvents = true;
        }

        void _TMTInfoFileWatcher_Changed(object sender, FileSystemEventArgs e)
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

            string tmtPlayState = values["State"];

            if (tmtPlayState == "play")
            {
                // Playback just started
                if (!_HasStartedPlaying)
                {
                    if (_PlaybackArguments.Resume)
                    {
                        ExecuteResumeCommand(_PlaybackArguments.PlaylistPosition, _PlaybackArguments.PositionTicks);
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
                _LastPlaybackState = GetPlaybackState(values);

                OnProgress(null, _LastPlaybackState);

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
            if (_TMTInfoFileWatcher != null)
            {
                _TMTInfoFileWatcher.EnableRaisingEvents = false;
                _TMTInfoFileWatcher.Changed -= _TMTInfoFileWatcher_Changed;
                _TMTInfoFileWatcher.Dispose();
                _TMTInfoFileWatcher = null;
            }
        }

        protected override void OnPlaybackFinished(object sender, PlaybackStateEventArgs e)
        {
            // In case anything went wrong trying to do this during the event
            DisposeFileSystemWatcher();

            base.OnPlaybackFinished(sender, e);
        }
        
        /// <summary>
        /// Sends a command to the MMC console to close the player.
        /// Do not use this for the WMC add-in because it will close WMC
        /// </summary>
        private void ClosePlayer()
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

        private string PlayStateDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArcSoft\\" + PlayStatePathAppName);
            }
        }

        protected virtual string PlayStatePathAppName
        {
            get
            {
                return "ArcSoft TotalMedia Theatre 5";
            }
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
