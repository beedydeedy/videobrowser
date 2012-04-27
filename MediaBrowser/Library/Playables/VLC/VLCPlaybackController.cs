using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Xml;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Playables.ExternalPlayer;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables.VLC
{
    public class VLCPlaybackController : ConfigurableExternalPlaybackController
    {
        // All of these hold state about what's being played. They're all reset when playback starts
        private string _CurrentPlayingFile = string.Empty;
        private long _CurrentFileDuration = 0;
        private long _CurrentPlayingPosition = 0;
        private bool _MonitorPlayback;
        private WebClient _WebClient;
        private Thread _WebRequestThread;

        /// <summary>
        /// Gets arguments to be passed to the command line.
        /// </summary>
        protected override List<string> GetCommandArgumentsList(PlayableItem playInfo)
        {
            List<string> args = new List<string>();

            args.Add("{0}");

            // Be explicit about start time, to avoid any possible player auto-resume settings
            double startTimeInSeconds = playInfo.Resume ? new TimeSpan(playInfo.MediaItems.First().PlaybackStatus.PositionTicks).TotalSeconds : 0;

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

            // Startup the Http interface so we can send out requests to monitor playstate
            args.Add("--extraintf=http");
            args.Add("--http-host=" + VlcHttpServer);
            args.Add("--http-port=" + VlcHttpPort);

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
            _MonitorPlayback = true;
            _CurrentPlayingFile = string.Empty;
            _CurrentPlayingPosition = 0;
            _CurrentFileDuration = 0;
            _WebClient = new WebClient();
            _WebClient.DownloadStringCompleted += client_DownloadStringCompleted;

            // Start up the thread that will perform the monitoring
            _WebRequestThread = new Thread(MonitorVlcHttpServer);
            _WebRequestThread.IsBackground = true;
            _WebRequestThread.Start();
        }

        /// <summary>
        /// Sends out requests to VLC's Http interface
        /// </summary>
        private void MonitorVlcHttpServer()
        {
            Uri uri = new Uri(VlcStatusXmlUrl);

            while (_MonitorPlayback)
            {
                try
                {
                    _WebClient.DownloadStringAsync(uri);
                }
                catch (Exception ex)
                {
                    Logging.Logger.ReportException("Error connecting to VLC http server", ex);
                }

                Thread.Sleep(1000);
            }
        }

        void client_DownloadStringCompleted(object sender, DownloadStringCompletedEventArgs e)
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

            // Check the filename node for null first, because if that's the case then it means nothing's currently playing.
            // This could happen after playback has stopped, but before the player has exited
            if (fileNameNode != null)
            {
                _CurrentPlayingPosition = TimeSpan.FromSeconds(int.Parse(docElement.SelectSingleNode("time").InnerText)).Ticks;
                _CurrentFileDuration = TimeSpan.FromSeconds(int.Parse(docElement.SelectSingleNode("length").InnerText)).Ticks;

                _CurrentPlayingFile = fileNameNode.InnerText;

                OnProgress(GetPlaybackState());
            }
        }

        protected override void OnExternalPlayerClosed()
        {
            // Stop sending requests to VLC's http interface
            _MonitorPlayback = false;

            if (_WebClient.IsBusy)
            {
                _WebClient.CancelAsync();
            }

            // Cleanup WebClient resources
            _WebClient.DownloadStringCompleted -= client_DownloadStringCompleted;
            _WebClient.Dispose();

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

                if (playable.HasMediaItems)
                {
                    SetMediaEventPropertiesBasedOnCurrentFile(playable, state);
                }

                else
                {
                    state.FilePlaylistPosition = GetPlaylistIndex(playable.Files, _CurrentPlayingFile);
                }
            }

            return state;
        }

        private void SetMediaEventPropertiesBasedOnCurrentFile(PlayableItem playable, PlaybackStateEventArgs state)
        {
            for (int i = 0; i < playable.MediaItems.Count(); i++)
            {
                Media media = playable.MediaItems.ElementAt(i);

                IEnumerable<string> files = GetPlayableFiles(media);

                int index = GetPlaylistIndex(files, _CurrentPlayingFile);

                if (index != -1)
                {
                    state.CurrentMediaIndex = i;
                    state.FilePlaylistPosition = playable.Files.ToList().IndexOf(files.ElementAt(index));                   
                    break;
                }
            }
        }

        private int GetPlaylistIndex(IEnumerable<string> files, string currentPlayingFile)
        {
            int count = files.Count();

            // Get the playlist position by matching the filename that VLC reported with the original
            for (int i = 0; i < count; i++)
            {
                string file = files.ElementAt(i);

                if (file.EndsWith(_CurrentPlayingFile))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Gets the server name that VLC's Http interface will be running on
        /// </summary>
        private string VlcHttpServer
        {
            get
            {
                return "localhost";
            }
        }

        /// <summary>
        /// Gets the port that VLC's Http interface will be running on
        /// </summary>
        private string VlcHttpPort
        {
            get
            {
                return "8088";
            }
        }

        /// <summary>
        /// Gets the url of VLC's xml status file
        /// </summary>
        private string VlcStatusXmlUrl
        {
            get
            {
                return "http://" + VlcHttpServer + ":" + VlcHttpPort + "/requests/status.xml";
            }
        }
    }
}
