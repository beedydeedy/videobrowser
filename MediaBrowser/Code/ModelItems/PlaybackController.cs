using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;
using Microsoft.MediaCenter;
using Microsoft.MediaCenter.Hosting;
using Microsoft.MediaCenter.UI;

namespace MediaBrowser
{
    /// <summary>
    /// Plays content using the internal WMC video player.
    /// Don't inherit from this unless you're playing using the internal WMC player
    /// </summary>
    public class PlaybackController : BasePlaybackController
    {
        static MediaBrowser.Library.Transcoder transcoder;

        #region CanPlay
        // Default controller can play everything
        public override bool CanPlay(string filename)
        {
            return true;
        }

        // Default controller can play everything
        public override bool CanPlay(IEnumerable<string> files)
        {
            return true;
        }
        #endregion

        public PlaybackController()
        {

        }

        protected override void PlayMediaInternal(PlaybackArguments playInfo)
        {
            Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => PlayPaths(playInfo, false));
        }

        protected override void QueueMediaInternal(PlaybackArguments playInfo)
        {
            Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => PlayPaths(playInfo, true));
        }

        public override void Seek(long position)
        {
            var transport = MediaTransport;
            if (transport != null)
            {
                transport.Position = new TimeSpan(position);
            }
        }

        /// <summary>
        /// If the last content passed into MediaCenterEnvironment.PlayMedia, it can be retrieved using MediaExperience.GetMediaCollection
        /// This can be done whether the content is still playing or not, and whether it was played by MB or another application.
        /// However, if another application played the collection, it will be read-only
        /// </summary>
        private MediaCollection GetCurrentMediaCollectionFromMediaExperience(MediaExperience mce)
        {
            MediaCollection coll = new MediaCollection();

            try
            {
                mce.GetMediaCollection(coll);
            }
            catch (Exception e)
            {
                Logger.ReportException("MediaExperience.GetMediaCollection: ", e);
            }

            return coll;
        }

        // After calling MediaCenterEnvironment.PlayMedia, playback will begin with a state of Stopped and position 0
        // We'll record it when we see it so we don't get tripped up into thinking playback has actually stopped
        private bool HasStartedPlaying = false;

        private void PlayPaths(PlaybackArguments playInfo, bool queue)
        {
            bool transcode = Config.Instance.EnableTranscode360 && Application.RunningOnExtender;

            MediaCollection mediaCollection;

            // Determines if we will call MediaCenterEnvironment.PlayMedia, or if
            // we'll just add to the currently playing MediaCollection.
            bool callPlayMedia = true;

            // Get this now since we'll be using it frequently
            MediaCenterEnvironment mediaCenterEnvironment = AddInHost.Current.MediaCenterEnvironment;

            if (queue)
            {
                mediaCollection = GetCurrentMediaCollectionFromMediaExperience(mediaCenterEnvironment.MediaExperience);

                // If an empty MediaCollection comes back, create a new one
                if (mediaCollection.Count == 0)
                {
                    mediaCollection = new MediaCollection();
                }
                else
                {
                    // If a MediaCollection comes back with items in it, we'll just add to it
                    // This will throw an exception if the MediaCollection was not originally played by Media Browser.
                    callPlayMedia = false;
                }
            }
            else
            {
                // If we're not queueing then we'll always start with a fresh collection
                mediaCollection = new MediaCollection();
            }

            // Create a MediaCollectionItem for each file to play
            for (int i = 0; i < playInfo.Files.Count(); i++)
            {
                string path = playInfo.Files.ElementAt(i);
                string fileToPlay = transcode ? GetTranscodedPath(path) : path;

                MediaCollectionItem item = new MediaCollectionItem();
                item.Media = fileToPlay;

                // Embed the playlist index, since we could have multiple playlists queued up
                // which prevents us from being able to use MediaCollection.CurrentIndex
                item.FriendlyData["fileIndex"] = i.ToString();

                // Embed the PlayableItemId so we can identify which one to track progress for
                item.FriendlyData["itemId"] = playInfo.PlayableItemId.ToString();

                mediaCollection.Add(item);
            }

            if (callPlayMedia)
            {
                // Set starting position if we're resuming
                if (playInfo.Resume)
                {
                    mediaCollection.CurrentIndex = playInfo.PlaylistPosition;
                    mediaCollection[playInfo.PlaylistPosition].Start = new TimeSpan(playInfo.PositionTicks);
                }

                HasStartedPlaying = false;

                try
                {
                    if (!mediaCenterEnvironment.PlayMedia(MediaType.MediaCollection, mediaCollection, queue))
                    {
                        Logger.ReportInfo("PlayMedia returned false");
                    }

                    if (playInfo.GoFullScreen && !mediaCenterEnvironment.MediaExperience.IsFullScreen)
                    {
                        mediaCenterEnvironment.MediaExperience.GoToFullScreen();
                    }

                    // Attach event handler
                    MediaTransport transport = mediaCenterEnvironment.MediaExperience.Transport;

                    transport.PropertyChanged -= MediaTransport_PropertyChanged;
                    transport.PropertyChanged += MediaTransport_PropertyChanged;
                }
                catch (Exception ex)
                {
                    Logger.ReportException("Playing media failed.", ex);
                    Application.ReportBrokenEnvironment();
                }
            }

        }

        DateTime lastCall = DateTime.Now;

        /// <summary>
        /// Handles the MediaTransport.PropertyChanged event, which most of the time will be due to Position
        /// </summary>
        void MediaTransport_PropertyChanged(IPropertyObject sender, string property)
        {
            try
            {
                MediaTransport transport = sender as MediaTransport;

                MediaExperience mce = MediaExperience;

                PlayState state;
                float bufferingProgress = 0;
                float playRate = 2;
                long positionTicks = 0;

                // If another application is playing the content, such as the WMC autoplay handler, we will
                // not have permission to access Transport properties
                // But we can look at MediaExperience.MediaType to determine if something is playing
                try
                {
                    state = transport.PlayState;
                    bufferingProgress = transport.BufferingProgress;
                    playRate = transport.PlayRate;
                    positionTicks = transport.Position.Ticks;
                }
                catch (InvalidOperationException)
                {
                    state = mce.MediaType == Microsoft.MediaCenter.Extensibility.MediaType.Unknown ? Microsoft.MediaCenter.PlayState.Undefined : Microsoft.MediaCenter.PlayState.Playing;
                }

                // Don't get tripped up at the initial state of Stopped with position 0, which occurs with MediaCollections
                if (!HasStartedPlaying)
                {
                    if (state == Microsoft.MediaCenter.PlayState.Playing)
                    {
                        HasStartedPlaying = true;
                    }
                    else
                    {
                        return;
                    }
                }

                // protect against really agressive calls
                var diff = (DateTime.Now - lastCall).TotalMilliseconds;

                // Only cancel out Position reports
                if (diff < 1000 && diff >= 0 && property == "Position")
                {
                    return;
                }

                lastCall = DateTime.Now;

                // Determine if playback has stopped. When using MediaCollections, after stopping, 
                // PropertyChanged will still fire once with a PlayState of Playing and a PlayRate of 0, or BufferingProgress of -1
                // Also, per MSDN documentation, Finished is no longer used with Windows 7, 
                // but as long as it's part of the enum it probably makes sense to test for it
                bool isStopped = state == Microsoft.MediaCenter.PlayState.Finished || state == Microsoft.MediaCenter.PlayState.Stopped || state == Microsoft.MediaCenter.PlayState.Stopped || bufferingProgress == -1 || playRate == 0;

                // Get metadata from player
                MediaMetadata metadata = mce.MediaMetadata;

                string metadataTitle = GetTitleOfCurrentlyPlayingMedia(metadata);

                int playlistIndex = 0;

                PlaybackArguments currentPlaybackItem = GetCurrentPlaybackItemFromMediaCollection(mce, metadataTitle, out playlistIndex) ?? GetCurrentPlaybackItemFromMetadataTitle(metadataTitle, out playlistIndex);

                Guid playableItemId = currentPlaybackItem == null ? Guid.Empty : currentPlaybackItem.PlayableItemId;
                long duration = currentPlaybackItem == null ? 0 : GetDurationOfCurrentlyPlayingMedia(metadata);

                // Only fire the progress handler while playback is still active, because once playback stops position will be reset to 0
                if (positionTicks > 0)
                {
                    OnProgress(new PlaybackStateEventArgs() { Position = positionTicks, PlaylistPosition = playlistIndex, DurationFromPlayer = duration, PlayableItemId = playableItemId });
                }

                if (property == "PlayState")
                {
                    Logger.ReportVerbose("Playstate changed to {0} for {1}, PositionTicks:{2}, Playlist Index:{3}",
                      state, metadataTitle, positionTicks, playlistIndex);

                    HandlePlaystateChange(transport, isStopped, playableItemId, playlistIndex, positionTicks, duration);
                }
            }
            catch (Exception ex)
            {
                Logger.ReportException("Prop: ", ex);
            }
        }

        /// <summary>
        /// Determines the item currently playing using the now playing title
        /// </summary>
        private PlaybackArguments GetCurrentPlaybackItemFromMediaCollection(MediaExperience mce, string metadataTitle, out int currentPlaylistIndex)
        {
            currentPlaylistIndex = 0;

            MediaCollection mediaCollection = GetCurrentMediaCollectionFromMediaExperience(mce);

            // If the MediaCollectionItem is null or empty then playback is occurring due to some other application or plugin
            // Perhaps a single path was passed into PlayMedia
            // OR playback has previously stopped and the collection is no longer available
            MediaCollectionItem activeItem = mediaCollection.Count == 0 ? null : mediaCollection[mediaCollection.CurrentIndex];

            if (activeItem == null)
            {
                return null;
            }

            Guid playableItemId = new Guid(activeItem.FriendlyData["itemId"].ToString());
            currentPlaylistIndex = int.Parse(activeItem.FriendlyData["fileIndex"].ToString());

            return CurrentPlaybackItems.Where(p => p.PlayableItemId == playableItemId).FirstOrDefault();
        }

        /// <summary>
        /// Determines the item currently playing using the now playing title
        /// </summary>
        private PlaybackArguments GetCurrentPlaybackItemFromMetadataTitle(string title, out int currentPlaylistIndex)
        {
            currentPlaylistIndex = 0;
           
            title = title.ToLower();

            foreach (PlaybackArguments playbackItem in CurrentPlaybackItems)
            {
                for (int i = 0; i < playbackItem.Files.Count(); i++)
                {
                    string file = playbackItem.Files.ElementAt(i).ToLower();
                    string normalized = file.Replace('\\', '/');
                    string alternateTitle = Path.GetFileNameWithoutExtension(file);

                    if (title.EndsWith(normalized) || title == alternateTitle)
                    {
                        currentPlaylistIndex = i;
                        return playbackItem;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Handles a change of Playstate by firing various events and post play processes
        /// </summary>
        private void HandlePlaystateChange(MediaTransport transport, bool isStopped, Guid playableItemId, int playlistIndex, long positionTicks, long duration)
        {
            Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => PlayStateChanged());
            Logger.ReportVerbose("Setting now playing status...");

            Application.CurrentInstance.ShowNowPlaying = !isStopped;

            if (isStopped)
            {
                // Stop listening to the event
                transport.PropertyChanged -= MediaTransport_PropertyChanged;

                // Fire the OnFinished event for each item
                OnPlaybackFinished(new PlaybackStateEventArgs() { Position = positionTicks, PlaylistPosition = playlistIndex, DurationFromPlayer = duration, PlayableItemId = playableItemId });

                //we're done - call post-processor
                Application.CurrentInstance.ReturnToApplication();

                // This will prevent us from getting in here twice after playback stops and calling post-play processes more than once.
                HasStartedPlaying = false;
            }
        }

        public override void GoToFullScreen()
        {
            var mce = MediaExperience;

            // great window 7 has bugs, lets see if we can work around them 
            // http://mediacentersandbox.com/forums/thread/9287.aspx
            if (mce == null)
            {
                Logger.ReportVerbose("MediaExperience is null, trying to work around it");
                System.Threading.Thread.Sleep(200);
                mce = MediaExperience;
                if (mce == null)
                {
                    try
                    {
                        var fi = AddInHost.Current.MediaCenterEnvironment.GetType()
                            .GetField("_checkedMediaExperience", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fi != null)
                        {
                            fi.SetValue(AddInHost.Current.MediaCenterEnvironment, false);
                            mce = MediaExperience;
                        }

                    }
                    catch (Exception e)
                    {
                        // give up ... I do not know what to do 
                        Logger.ReportException("AddInHost.Current.MediaCenterEnvironment.MediaExperience is null", e);
                    }

                }
            }

            if (mce != null)
            {
                Logger.ReportVerbose("Going fullscreen...");
                mce.GoToFullScreen();
            }
            else
            {
                Logger.ReportError("AddInHost.Current.MediaCenterEnvironment.MediaExperience is null, we have no way to go full screen!");
                AddInHost.Current.MediaCenterEnvironment.Dialog(Application.CurrentInstance.StringData("CannotMaximizeDial"), "", Microsoft.MediaCenter.DialogButtons.Ok, 0, true);
            }
        }

        private string GetTranscodedPath(string path)
        {
            // Can't transcode this
            if (Directory.Exists(path))
            {
                return path;
            }

            if (Helper.IsExtenderNativeVideo(path))
            {
                return path;
            }
            else
            {
                if (transcoder == null)
                {
                    transcoder = new MediaBrowser.Library.Transcoder();
                }

                string bufferpath = transcoder.BeginTranscode(path);

                // if bufferpath comes back null, that means the transcoder i) failed to start or ii) they
                // don't even have it installed
                if (string.IsNullOrEmpty(bufferpath))
                {
                    Application.DisplayDialog("Could not start transcoding process", "Transcode Error");
                    throw new Exception("Could not start transcoding process");
                }

                return bufferpath;
            }
        }

        #region Playback status

        public override bool IsPlayingVideo
        {
            get
            {
                MediaExperience mce = MediaExperience;

                // Try to access MediaExperience.Transport and get PlayState from there
                if (mce != null)
                {
                    try
                    {
                        MediaTransport transport = mce.Transport;

                        if (transport != null)
                        {
                            if (transport.PlayState != Microsoft.MediaCenter.PlayState.Playing)
                            {
                                return false;
                            }
                        }

                    }
                    catch (InvalidOperationException e)
                    {
                        Logger.ReportException("EXCEPTION trying to access MediaExperience.Transport from PlaybackController.IsPlayingVideo: ", e);
                    }

                    // If we weren't able to access MediaExperience.Transport, it's likely due to another application playing video
                    Microsoft.MediaCenter.Extensibility.MediaType mediaType = mce.MediaType;

                    return mediaType != Microsoft.MediaCenter.Extensibility.MediaType.Unknown && mediaType != Microsoft.MediaCenter.Extensibility.MediaType.Audio;
                }

                // At this point nothing worked, so return false
                return false;
            }
        }

        public override bool IsPlaying
        {
            get
            {
                return PlayState == PlayState.Playing;
            }
        }

        public override bool IsStopped
        {
            get { return !IsPlaying && !IsPaused; }
        }

        public override bool IsPaused
        {
            get { return PlayState == PlayState.Paused; }
        }

        private PlayState PlayState
        {
            get
            {
                MediaExperience mce = MediaExperience;

                // Try to access MediaExperience.Transport and get PlayState from there
                if (mce != null)
                {
                    try
                    {
                        MediaTransport transport = mce.Transport;

                        if (transport != null)
                        {
                            return transport.PlayState;
                        }

                    }
                    catch (InvalidOperationException e)
                    {
                        Logger.ReportException("EXCEPTION trying to access MediaExperience.Transport from PlaybackController.PlayState: ", e);
                    }

                    // If we weren't able to access MediaExperience.Transport, it's likely due to another application playing video
                    Microsoft.MediaCenter.Extensibility.MediaType mediaType = mce.MediaType;

                    if (mediaType != Microsoft.MediaCenter.Extensibility.MediaType.Unknown)
                    {
                        Logger.ReportVerbose("MediaExperience.MediaType is {0}. Assume content is playing.", mediaType);

                        return Microsoft.MediaCenter.PlayState.Playing;
                    }
                }

                // At this point nothing worked, so return Undefined
                return PlayState.Undefined;
            }
        }

        #endregion

        protected MediaExperience MediaExperience
        {
            get
            {
                return AddInHost.Current.MediaCenterEnvironment.MediaExperience;
            }
        }

        protected MediaTransport MediaTransport
        {
            get
            {
                MediaExperience mce = MediaExperience;

                if (mce != null)
                {
                    try
                    {
                        return mce.Transport;
                    }
                    catch (InvalidOperationException e)
                    {
                        // well if we are inactive we are not allowed to get media experience ...
                        Logger.ReportException("EXCEPTION : ", e);
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the title of the currently playing content
        /// </summary>
        private string GetTitleOfCurrentlyPlayingMedia(MediaMetadata metadata)
        {
            if (metadata == null) return string.Empty;

            //changed this to get the "Name" property instead.  That makes it compatable with DVD playback as well.

            string title = metadata["Name"] as string;

            if (string.IsNullOrEmpty(title))
            {
                title = metadata["Title"] as string;
            }

            return string.IsNullOrEmpty(title) ? string.Empty : title;
        }

        /// <summary>
        /// Gets the duration, in ticks, of the currently playing content
        /// </summary>
        private long GetDurationOfCurrentlyPlayingMedia(MediaMetadata metadataFromPlayer)
        {
            if (metadataFromPlayer != null)
            {
                string strDuration = metadataFromPlayer["Duration"] as string;

                if (string.IsNullOrEmpty(strDuration)) strDuration = metadataFromPlayer["TrackDuration"] as string;

                // Found it in metadata, now parse
                if (!string.IsNullOrEmpty(strDuration))
                {
                    return TimeSpan.Parse(strDuration).Ticks;
                }
            }

            return 0;
        }

        /// <summary>
        /// Gets a friendly (displayable) title of what's currently playing
        /// </summary>
        public override string NowPlayingTitle
        {
            get
            {
                MediaExperience exp = MediaExperience;

                if (exp == null)
                {
                    return "Unknown";
                }

                MediaMetadata metadata = exp.MediaMetadata;

                if (metadata == null)
                {
                    return "Unknown";
                }

                string name = GetTitleOfCurrentlyPlayingMedia(metadata).Trim('/');

                return FormatPathForDisplay(name);
            }
        }

        protected void PlayStateChanged()
        {
            FirePropertyChanged("PlayState");
            FirePropertyChanged("IsPlaying");
            FirePropertyChanged("IsPlayingVideo");
            FirePropertyChanged("IsStopped");
            FirePropertyChanged("IsPaused");
        }

        public override void Pause()
        {
            var transport = MediaTransport;
            if (transport != null)
            {
                transport.PlayRate = 1;
            }
        }

        protected override void StopInternal()
        {
            var transport = MediaTransport;
            if (transport != null)
            {
                transport.PlayRate = 0;
            }
        }

        protected override void Dispose(bool isDisposing)
        {

            Logger.ReportVerbose("Playback controller is being disposed");

            MediaTransport transport = MediaTransport;

            if (transport != null)
            {
                transport.PropertyChanged -= MediaTransport_PropertyChanged;
            }

            base.Dispose(isDisposing);

        }

    }
}
