using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
    /// Random notes (so they don't get lost)
    /// - MediaTransport must be queried in some form every 5 minutes (even to just check position or playstate).
    /// If it idles it will dispose. This is why we constantly detach and reattach the PropertyChanged event handler
    /// - MediaExperience is constantly reset, so it should not be cached anywhere or even stored in a variable.
    /// Always get it fresh from MediaCenterEnvironment
    /// </summary>
    public class PlaybackController : BaseModelItem, IPlaybackController
    {
        static MediaBrowser.Library.Transcoder transcoder;

        #region Progress EventHandler
        volatile EventHandler<PlaybackStateEventArgs> _Progress;
        public event EventHandler<PlaybackStateEventArgs> Progress
        {
            add
            {
                _Progress += value;
            }
            remove
            {
                _Progress -= value;
            }
        }
        public void OnProgress(PlaybackStateEventArgs args)
        {
            if (_Progress != null)
            {
                _Progress(this, args);
            }
        }
        #endregion

        #region PlaybackFinished EventHandler
        volatile EventHandler<PlaybackStateEventArgs> _PlaybackFinished;
        public event EventHandler<PlaybackStateEventArgs> PlaybackFinished
        {
            add
            {
                _PlaybackFinished += value;
            }
            remove
            {
                _PlaybackFinished -= value;
            }
        }


        private void OnPlaybackFinished(PlaybackStateEventArgs args)
        {
            if (_PlaybackFinished != null)
            {
                _PlaybackFinished(this, args);
            }
        }
        #endregion

        public virtual bool RequiresExternalPage
        {
            get
            {
                return false;
            }
        }

        // Default controller can play everything
        public virtual bool CanPlay(string filename)
        {

            return true;
        }

        // Default controller can play everything
        public virtual bool CanPlay(IEnumerable<string> files)
        {
            return true;
        }

        // commands are not routed in this way ... 
        public virtual void ProcessCommand(RemoteCommand command)
        {
            // dont do anything (only plugins need to handle this)
        }

        public PlaybackController()
        {

        }

        public virtual void PlayMedia(PlaybackArguments playInfo)
        {
            Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => PlayPaths(playInfo, false));
        }

        public virtual void QueueMedia(PlaybackArguments playInfo)
        {
            Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => PlayPaths(playInfo, true));
        }

        public virtual void Seek(long position)
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

                MediaCollection mediaCollection = GetCurrentMediaCollectionFromMediaExperience(mce);

                // If the MediaCollectionItem is null or empty then playback is occurring due to some other application or plugin
                // Perhaps a single path was passed into PlayMedia
                // OR playback has previously stopped and the collection is no longer available
                MediaCollectionItem activeItem = mediaCollection.Count == 0 ? null : mediaCollection[mediaCollection.CurrentIndex];

                // Get metadata from player
                MediaMetadata metadata = mce.MediaMetadata;

                Guid playableItemId = activeItem == null ? Guid.Empty : new Guid(activeItem.FriendlyData["itemId"].ToString());
                int playlistIndex = activeItem == null ? 0 : int.Parse(activeItem.FriendlyData["fileIndex"].ToString());
                long duration = activeItem == null ? 0 : GetDurationOfCurrentlyPlayingMedia(metadata, activeItem);

                // Only fire the progress handler while playback is still active, because once playback stops position will be reset to 0
                if (positionTicks > 0)
                {
                    OnProgress(new PlaybackStateEventArgs() { Position = positionTicks, PlaylistPosition = playlistIndex, DurationFromPlayer = duration, PlayableItemId = playableItemId });
                }

                if (property == "PlayState")
                {
                    Logger.ReportVerbose("Playstate changed to {0} for {1}, PositionTicks:{2}, Playlist Index:{3}",
                      state, GetTitleOfCurrentlyPlayingMedia(metadata), positionTicks, playlistIndex);

                    // Only use below when debugging (to minimize queries made on the transport).
                    //Logger.ReportVerbose("PlayRate:{0}, BufferingProgress:{1}", transport.PlayRate, transport.BufferingProgress);

                    HandlePlaystateChange(transport, isStopped, playableItemId, playlistIndex, positionTicks, duration);
                }
            }
            catch (Exception ex)
            {
                Logger.ReportException("Prop: ", ex);
            }
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

                Application.CurrentInstance.RunPostPlayProcesses();

                //we're done - call post-processor
                Application.CurrentInstance.ReturnToApplication();

                // This will prevent us from getting in here twice after playback stops and calling post-play processes more than once.
                HasStartedPlaying = false;
            }
        }

        public virtual void GoToFullScreen()
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

        public virtual bool IsPlayingVideo
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

        public virtual bool IsPlaying
        {
            get
            {
                return PlayState == PlayState.Playing;
            }
        }

        public virtual bool IsStopped
        {
            get { return !IsPlaying && !IsPaused; }
        }

        public virtual bool IsPaused
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
        private long GetDurationOfCurrentlyPlayingMedia(MediaMetadata metadataFromPlayer, MediaCollectionItem item)
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

        protected void PlayStateChanged()
        {
            FirePropertyChanged("PlayState");
            FirePropertyChanged("IsPlaying");
            FirePropertyChanged("IsPlayingVideo");
            FirePropertyChanged("IsStopped");
            FirePropertyChanged("IsPaused");
        }

        public virtual void Pause()
        {
            var transport = MediaTransport;
            if (transport != null)
            {
                transport.PlayRate = 1;
            }
        }

        public virtual void Stop()
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
