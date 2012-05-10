using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Xml;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Playables.ExternalPlayer;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables.VLC2
{
    public class VLC2PlaybackController : ConfigurableExternalPlaybackController
    {
        private const int ProgressInterval = 1000;

        // All of these hold state about what's being played. They're all reset when playback starts
        private int _CurrrentPlayingFileIndex = -1;
        private long _CurrentFileDuration = 0;
        private long _CurrentPlayingPosition = 0;
        private bool _MonitorPlayback;
        private string _CurrentPlayState;

        // This will get the current file position
        private WebClient _StatusRequestClient;
        private Thread _StatusRequestThread;

        // This will get the current file index
        private WebClient _PlaylistRequestClient;

        private bool _IsDisposing = false;

        /// <summary>
        /// Gets arguments to be passed to the command line.
        /// </summary>
        protected override List<string> GetCommandArgumentsList(PlayableItem playInfo)
        {
            List<string> args = new List<string>();

            args.Add("{0}");

            // Be explicit about start time, to avoid any possible player auto-resume settings
            double startTimeInSeconds = new TimeSpan(playInfo.StartPositionTicks).TotalSeconds;

            args.Add("--start-time=" + startTimeInSeconds);

            // Play in fullscreen
            args.Add("--fullscreen");
            // Keep the window on top of others
            args.Add("--video-on-top");
            // Start a new instance
            args.Add("--no-one-instance");
            // Close the player when playback finishes
            args.Add("--play-and-exit");
            // Disable system screen saver during playback
            args.Add("--disable-screensaver");

            args.Add("--qt-minimal-view");

            // Startup the Http interface so we can send out requests to monitor playstate
            args.Add("--extraintf=http");
            args.Add("--http-host=" + HttpServer);
            args.Add("--http-port=" + HttpPort);

            // Map the stop button on the remote to close the player
            args.Add("--global-key-quit=\"Media Stop\"");

            args.Add("--global-key-play=\"Media Play\"");
            args.Add("--global-key-pause=\"Media Pause\"");
            args.Add("--global-key-play-pause=\"Media Play Pause\"");

            args.Add("--global-key-vol-down=\"Volume Down\"");
            args.Add("--global-key-vol-up=\"Volume Up\"");
            args.Add("--global-key-vol-mute=\"Mute\"");

            args.Add("--key-nav-up=\"Up\"");
            args.Add("--key-nav-down=\"Down\"");
            args.Add("--key-nav-left=\"Left\"");
            args.Add("--key-nav-right=\"Right\"");
            args.Add("--key-nav-activate=\"Enter\"");

            args.Add("--global-key-jump-long=\"Media Prev Track\"");
            args.Add("--global-key-jump+long=\"Media Next Track\"");

            return args;
        }

        /// <summary>
        /// Starts monitoring playstate using the VLC Http interface
        /// </summary>
        protected override void OnExternalPlayerLaunched(PlayableItem playbackInfo)
        {
            base.OnExternalPlayerLaunched(playbackInfo);

            // Reset these fields since they hold state
            _CurrrentPlayingFileIndex = -1;
            _CurrentPlayingPosition = 0;
            _CurrentFileDuration = 0;
            _CurrentPlayState = string.Empty;

            if (_StatusRequestClient == null)
            {
                _StatusRequestClient = new WebClient();
                _PlaylistRequestClient = new WebClient();

                // Start up the thread that will perform the monitoring
                _StatusRequestThread = new Thread(MonitorStatus);
                _StatusRequestThread.IsBackground = true;
                _StatusRequestThread.Start();
            }

            _PlaylistRequestClient.DownloadStringCompleted -= playlistRequestCompleted;
            _StatusRequestClient.DownloadStringCompleted -= statusRequestCompleted;

            _PlaylistRequestClient.DownloadStringCompleted += playlistRequestCompleted;
            _StatusRequestClient.DownloadStringCompleted += statusRequestCompleted;

            _MonitorPlayback = true;
        }

        /// <summary>
        /// Sends out requests to VLC's Http interface
        /// </summary>
        private void MonitorStatus()
        {
            Uri statusUri = new Uri(StatusUrl);
            Uri playlistUri = new Uri(VlcPlaylistXmlUrl);

            while (!_IsDisposing)
            {
                if (_MonitorPlayback)
                {
                    SendStatusRequest(statusUri);

                    try
                    {
                        _PlaylistRequestClient.DownloadStringAsync(playlistUri);
                    }
                    catch (Exception ex)
                    {
                        Logger.ReportException("Error connecting to VLC playlist url", ex);
                    }
                }

                Thread.Sleep(ProgressInterval);
            }
        }

        private void SendStatusRequest(Uri uri)
        {
            try
            {
                _StatusRequestClient.DownloadStringAsync(uri);
            }
            catch (Exception ex)
            {
                Logger.ReportException("Error connecting to VLC status url", ex);
            }
        }

        void statusRequestCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            // If playback just finished, or if there was some type of error, skip it
            if (!_MonitorPlayback || e.Cancelled || e.Error != null || string.IsNullOrEmpty(e.Result))
            {
                return;
            }

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(e.Result);
            XmlElement docElement = doc.DocumentElement;

            XmlNode fileNameNode = docElement.SelectSingleNode("information/category[@name='meta']/info[@name='filename']");

            _CurrentPlayState = docElement.SafeGetString("state", string.Empty).ToLower();

            // Check the filename node for null first, because if that's the case then it means nothing's currently playing.
            // This could happen after playback has stopped, but before the player has exited
            if (fileNameNode != null)
            {
                _CurrentPlayingPosition = TimeSpan.FromSeconds(int.Parse(docElement.SelectSingleNode("time").InnerText)).Ticks;
                _CurrentFileDuration = TimeSpan.FromSeconds(int.Parse(docElement.SelectSingleNode("length").InnerText)).Ticks;
            }
        }

        void playlistRequestCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            // If playback just finished, or if there was some type of error, skip it
            if (!_MonitorPlayback || e.Cancelled || e.Error != null || string.IsNullOrEmpty(e.Result))
            {
                return;
            }

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(e.Result);
            XmlElement docElement = doc.DocumentElement;

            XmlNode leafNode = docElement.SelectSingleNode("node/leaf[@current='current']");

            if (leafNode != null)
            {
                _CurrentFileDuration = TimeSpan.FromSeconds(int.Parse(leafNode.Attributes["duration"].Value)).Ticks;

                _CurrrentPlayingFileIndex = IndexOfNode(leafNode.ParentNode.ChildNodes, leafNode);

                OnProgress(GetPlaybackState());
            }
        }

        private int IndexOfNode(XmlNodeList nodes, XmlNode node)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] == node)
                {
                    return i;
                }
            }

            return -1;
        }

        protected override void OnExternalPlayerClosed()
        {
            // Stop sending requests to VLC's http interface
            _MonitorPlayback = false;
            _CurrentPlayState = string.Empty;

            // Cleanup events
            _PlaylistRequestClient.DownloadStringCompleted -= playlistRequestCompleted;
            _StatusRequestClient.DownloadStringCompleted -= statusRequestCompleted;

            base.OnExternalPlayerClosed();
        }

        protected override PlaybackStateEventArgs GetPlaybackState()
        {
            PlaybackStateEventArgs state = new PlaybackStateEventArgs();

            PlayableItem playable = GetCurrentPlayableItem();

            state.Position = _CurrentPlayingPosition;
            state.DurationFromPlayer = _CurrentFileDuration;

            if (playable != null)
            {
                state.Item = playable;

                state.CurrentFileIndex = _CurrrentPlayingFileIndex;
            }

            return state;
        }

        /// <summary>
        /// Gets the server name that VLC's Http interface will be running on
        /// </summary>
        private string HttpServer
        {
            get
            {
                return "localhost";
            }
        }

        /// <summary>
        /// Gets the port that VLC's Http interface will be running on
        /// </summary>
        private string HttpPort
        {
            get
            {
                return "8088";
            }
        }

        /// <summary>
        /// Gets the url of VLC's xml status file
        /// </summary>
        private string StatusUrl
        {
            get
            {
                return "http://" + HttpServer + ":" + HttpPort + "/requests/status.xml";
            }
        }

        /// <summary>
        /// Gets the url of VLC's xml status file
        /// </summary>
        private string VlcPlaylistXmlUrl
        {
            get
            {
                return "http://" + HttpServer + ":" + HttpPort + "/requests/playlist.xml";
            }
        }

        /// <summary>
        /// Takes a Media object and returns the list of files that will be sent to the player
        /// </summary>
        internal override IEnumerable<string> GetPlayableFiles(Media media)
        {
            IEnumerable<string> files = base.GetPlayableFiles(media);

            Video video = media as Video;

            if (video != null)
            {
                files = files.Select(i => FormatPath(i, video.MediaType));
            }

            return files;
        }

        /// <summary>
        /// Formats a path to send to the player
        /// </summary>
        private string FormatPath(string path, Library.MediaType mediaType)
        {
            if (path.EndsWith(":\\"))
            {
                path = path.TrimEnd('\\');
            }

            if (mediaType == MediaType.DVD)
            {
                path = "dvd:///" + path;
            }

            return path;
        }

        /// <summary>
        /// When playback is based purely on files, this will take the files that were supplied to the PlayableItem,
        /// and create the actual paths that will be sent to the player
        /// </summary>
        internal override IEnumerable<string> GetPlayableFiles(IEnumerable<string> files)
        {
            foreach (string file in files)
            {
                MediaBrowser.Library.MediaType mediaType = MediaBrowser.Library.MediaTypeResolver.DetermineType(file);

                yield return FormatPath(file, mediaType);
            }
        }

        public override bool IsPlaying
        {
            get
            {
                if (!string.IsNullOrEmpty(_CurrentPlayState))
                {
                    return _CurrentPlayState == "playing";
                }

                return base.IsPlaying;
            }
        }

        public override bool IsPaused
        {
            get
            {
                if (!string.IsNullOrEmpty(_CurrentPlayState))
                {
                    return _CurrentPlayState == "paused";
                }

                return base.IsPaused;
            }
        }

        public override void Pause()
        {
            SendStatusRequest(new Uri(StatusUrl + "?command=pl_pause"));
        }

        public override void UnPause()
        {
            SendStatusRequest(new Uri(StatusUrl + "?command=pl_play"));
        }

        protected override void StopInternal()
        {
            ClosePlayer();
        }

        private void ClosePlayer()
        {
            PlayableItem playable = GetCurrentPlayableItem();

            string commandPath = playable == null ? ExternalPlayerConfiguration.Command : GetCommandPath(playable);
            string commandArgs = "vlc://quit";

            ProcessStartInfo processInfo = new ProcessStartInfo(commandPath, commandArgs);

            processInfo.CreateNoWindow = true;

            Process.Start(processInfo);
        }

        public override void Seek(long position)
        {
            TimeSpan time = TimeSpan.FromTicks(position);

            SendStatusRequest(new Uri(StatusUrl + "?command=seek&val=" + time.TotalSeconds));
        }

        protected override void Dispose(bool isDisposing)
        {
            _IsDisposing = isDisposing;

            base.Dispose(isDisposing);
        }
    }
}
