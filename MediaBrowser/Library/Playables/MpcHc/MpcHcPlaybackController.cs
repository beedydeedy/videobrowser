using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Playables.ExternalPlayer;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;
using Microsoft.Win32;

namespace MediaBrowser.Library.Playables.MpcHc
{
    public class MpcHcPlaybackController : ConfigurableExternalPlaybackController
    {
        private const int ProgressInterval = 1000;

        // All of these hold state about what's being played. They're all reset when playback starts
        private string _CurrentPlayingFileName;
        private long _CurrentFileDuration = 0;
        private long _CurrentPlayingPosition = 0;
        private bool _MonitorPlayback;

        // This will get the current file position
        private WebClient _StatusRequestClient;
        private Thread _StatusRequestThread;

        private bool _IsDisposing = false;

        /// <summary>
        /// Starts monitoring playstate using the player's Http interface
        /// </summary>
        protected override void OnExternalPlayerLaunched(PlayableItem playbackInfo)
        {
            base.OnExternalPlayerLaunched(playbackInfo);

            // Reset these fields since they hold state
            _CurrentPlayingFileName = string.Empty;
            _CurrentPlayingPosition = 0;
            _CurrentFileDuration = 0;

            if (_StatusRequestClient == null)
            {
                _StatusRequestClient = new WebClient();

                // Start up the thread that will perform the monitoring
                _StatusRequestThread = new Thread(MonitorStatus);
                _StatusRequestThread.IsBackground = true;
                _StatusRequestThread.Start();
            }

            _StatusRequestClient.DownloadStringCompleted -= statusRequestCompleted;
            _StatusRequestClient.DownloadStringCompleted += statusRequestCompleted;

            _MonitorPlayback = true;
        }

        /// <summary>
        /// Sends out requests to the player's Http interface
        /// </summary>
        private void MonitorStatus()
        {
            Uri statusUri = new Uri(StatusUrl);

            while (!_IsDisposing)
            {
                if (_MonitorPlayback)
                {
                    try
                    {
                        _StatusRequestClient.DownloadStringAsync(statusUri);
                    }
                    catch (Exception ex)
                    {
                        Logger.ReportException("Error connecting to MPC status url", ex);
                    }
                }

                Thread.Sleep(ProgressInterval);
            }
        }

        void statusRequestCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            // If playback just finished, or if there was some type of error, skip it
            if (!_MonitorPlayback || e.Cancelled || e.Error != null || string.IsNullOrEmpty(e.Result))
            {
                return;
            }

            string result = e.Result;

            result = result.Substring(result.IndexOf('\''));
            result = result.Substring(0, result.LastIndexOf('\''));

            IEnumerable<string> values = result.Split(',').Select(v => v.Trim().Trim('\''));

            _CurrentPlayingPosition = TimeSpan.FromMilliseconds(double.Parse(values.ElementAt(2))).Ticks;
            _CurrentFileDuration = TimeSpan.FromMilliseconds(double.Parse(values.ElementAt(4))).Ticks;
            _CurrentPlayingFileName = values.Last().ToLower();

            OnProgress(GetPlaybackState());
        }

        protected override void OnExternalPlayerClosed()
        {
            // Stop checking status
            _MonitorPlayback = false;

            // Cleanup events
            _StatusRequestClient.DownloadStringCompleted -= statusRequestCompleted;

            base.OnExternalPlayerClosed();
        }

        /// <summary>
        /// Takes a Media object and returns the list of files that will be sent to the player
        /// </summary>
        internal override IEnumerable<string> GetPlayableFiles(Media media)
        {
            return base.GetPlayableFiles(media).Select(i => FormatPath(i));
        }

        /// <summary>
        /// Formats a path to send to the player
        /// </summary>
        private string FormatPath(string path)
        {
            if (path.EndsWith(":\\"))
            {
                path = path.TrimEnd('\\');
            }

            return path;
        }

        /// <summary>
        /// When playback is based purely on files, this will take the files that were supplied to the PlayableItem,
        /// and create the actual paths that will be sent to the player
        /// </summary>
        internal override IEnumerable<string> GetPlayableFiles(IEnumerable<string> files)
        {
            return base.GetPlayableFiles(files).Select(i => FormatPath(i));
        }

        /// <summary>
        /// Gets arguments to be passed to the command line.
        /// </summary>
        protected override List<string> GetCommandArgumentsList(PlayableItem playInfo)
        {
            List<string> args = new List<string>();

            args.Add("{0}");
            args.Add("/play");
            args.Add("/close");
            args.Add("/fullscreen");

            // Be explicit about start time, to avoid any possible player auto-resume settings
            double startTimeInMs = new TimeSpan(playInfo.StartPositionTicks).TotalMilliseconds;

            args.Add("/start " + startTimeInMs);

            return args;
        }

        /// <summary>
        /// Gets the watched state after playback has stopped.
        /// </summary>
        protected override PlaybackStateEventArgs GetPlaybackState()
        {
            PlaybackStateEventArgs args = new PlaybackStateEventArgs();

            PlayableItem playable = GetCurrentPlayableItem();

            args.DurationFromPlayer = _CurrentFileDuration;
            args.Position = _CurrentPlayingPosition;

            if (playable != null)
            {
                args.Item = playable;

                if (playable.HasMediaItems)
                {
                    int currentFileIndex;
                    args.CurrentMediaIndex = GetCurrentPlayingMediaIndex(playable, out currentFileIndex);
                    args.CurrentFileIndex = currentFileIndex;
                }
                else
                {
                    args.CurrentFileIndex = GetCurrentPlayingFileIndex(playable);
                }
            }

            return args;
        }

        private int GetCurrentPlayingFileIndex(PlayableItem playable)
        {
            return GetIndexOfFile(playable.FilesFormattedForPlayer, _CurrentPlayingFileName);
        }

        private int GetCurrentPlayingMediaIndex(PlayableItem playable, out int currentPlayingFileIndex)
        {
            currentPlayingFileIndex = -1;

            int numMediaItems = playable.MediaItems.Count();
            int totalFileCount = 0;

            for (int i = 0; i < numMediaItems; i++)
            {
                IEnumerable<string> mediaFiles = GetPlayableFiles(playable.MediaItems.ElementAt(i));

                int fileIndex = GetIndexOfFile(mediaFiles, _CurrentPlayingFileName);

                if (fileIndex != -1)
                {
                    currentPlayingFileIndex = totalFileCount + fileIndex;
                    return i;
                }

                totalFileCount += mediaFiles.Count();
            }

            return -1;
        }

        private int GetIndexOfFile(IEnumerable<string> files, string file)
        {
            int numFiles = files.Count();

            for (int i = 0; i < numFiles; i++)
            {
                if (file.StartsWith(files.ElementAt(i).ToLower()))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Gets the server name that the http interface will be running on
        /// </summary>
        private string HttpServer
        {
            get
            {
                return "localhost";
            }
        }

        /// <summary>
        /// Gets the port that the web interface will be running on
        /// </summary>
        public static string HttpPort
        {
            get
            {
                return "13579";
            }
        }

        /// <summary>
        /// Gets the url of that will be called to for status
        /// </summary>
        private string StatusUrl
        {
            get
            {
                return "http://" + HttpServer + ":" + HttpPort + "/status.html";
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            _IsDisposing = isDisposing;

            base.Dispose(isDisposing);
        }
    }
}
