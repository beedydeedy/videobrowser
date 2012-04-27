using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Playables;
using MediaBrowser.Library.RemoteControl;
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
        private MediaCollection CurrentMediaCollection;

        /// <summary>
        /// Plays Media
        /// </summary>
        protected override void PlayMediaInternal(PlayableItem playable)
        {
            Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => PlayPaths(playable));
        }

        /// <summary>
        /// Moves the player to a given position
        /// </summary>
        public override void Seek(long position)
        {
            var transport = MediaTransport;
            if (transport != null)
            {
                transport.Position = new TimeSpan(position);
            }
        }

        // After calling MediaCenterEnvironment.PlayMedia, playback will begin with a state of Stopped and position 0
        // We'll record it when we see it so we don't get tripped up into thinking playback has actually stopped
        private bool HasStartedPlaying = false;

        /// <summary>
        /// Plays or queues Media
        /// </summary>
        protected virtual void PlayPaths(PlayableItem playable)
        {
            // Determines if we will call MediaCenterEnvironment.PlayMedia, or if
            // we'll just add to the currently playing MediaCollection.
            bool callPlayMedia = true;

            // Get this now since we'll be using it frequently
            MediaCenterEnvironment mediaCenterEnvironment = AddInHost.Current.MediaCenterEnvironment;

            if (playable.QueueItem)
            {
                // If an empty MediaCollection comes back, create a new one
                if (CurrentMediaCollection == null || CurrentMediaCollection.Count == 0)
                {
                    CurrentMediaCollection = new MediaCollection();
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
                CurrentMediaCollection = new MediaCollection();
            }

            // Create a MediaCollectionItem for each file to play
            if (playable.HasMediaItems)
            {
                PopulateMediaCollectionUsingMediaItems(CurrentMediaCollection, playable);
            }
            else
            {
                PopulateMediaCollectionUsingFiles(CurrentMediaCollection, playable.Files, playable.Id);
            }

            if (callPlayMedia)
            {
                // Set starting position if we're resuming
                if (playable.Resume)
                {
                    var playstate = playable.MediaItems.First().PlaybackStatus;

                    CurrentMediaCollection.CurrentIndex = playstate.PlaylistPosition;
                    CurrentMediaCollection[playstate.PlaylistPosition].Start = new TimeSpan(playstate.PositionTicks);
                }

                HasStartedPlaying = false;

                try
                {
                    if (!mediaCenterEnvironment.PlayMedia(Microsoft.MediaCenter.MediaType.MediaCollection, CurrentMediaCollection, false))
                    {
                        Logger.ReportInfo("PlayMedia returned false");
                    }

                    if (playable.GoFullScreen && !mediaCenterEnvironment.MediaExperience.IsFullScreen)
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

        /// <summary>
        /// Then playback is based on Media items, this will populate the MediaCollection using the items
        /// </summary>
        private void PopulateMediaCollectionUsingMediaItems(MediaCollection coll, PlayableItem playable)
        {
            int currentFileIndex = 0;

            for (int mediaIndex = 0; mediaIndex < playable.MediaItems.Count(); mediaIndex++)
            {
                Media media = playable.MediaItems.ElementAt(mediaIndex);

                IEnumerable<string> files = GetPlayableFiles(media);

                int numFiles = files.Count();

                // Create a MediaCollectionItem for each file to play
                for (int i = 0; i < numFiles; i++)
                {
                    string path = files.ElementAt(i);

                    MediaCollectionItem item = new MediaCollectionItem();
                    item.Media = path;

                    // Embed the playlist index, since we could have multiple playlists queued up
                    // which prevents us from being able to use MediaCollection.CurrentIndex
                    item.FriendlyData["FilePlaylistPosition"] = currentFileIndex.ToString();

                    // Embed the PlayableItemId so we can identify which one to track progress for
                    item.FriendlyData["PlayableItemId"] = playable.Id.ToString();

                    // Embed the MediaId so we can identify which one to track progress for
                    item.FriendlyData["MediaIndex"] = mediaIndex.ToString();

                    CurrentMediaCollection.Add(item);

                    currentFileIndex++;
                }
            }
        }

        /// <summary>
        /// When playback is based purely on file paths, this will populate the MediaCollection using the paths
        /// </summary>
        private void PopulateMediaCollectionUsingFiles(MediaCollection coll, IEnumerable<string> files, Guid playableItemId)
        {
            int numFiles = files.Count();

            // Create a MediaCollectionItem for each file to play
            for (int i = 0; i < numFiles; i++)
            {
                string path = files.ElementAt(i);

                MediaCollectionItem item = new MediaCollectionItem();
                item.Media = path;

                // Embed the playlist index, since we could have multiple playlists queued up
                // which prevents us from being able to use MediaCollection.CurrentIndex
                item.FriendlyData["FilePlaylistPosition"] = i.ToString();

                // Embed the PlayableItemId so we can identify which one to track progress for
                item.FriendlyData["PlayableItemId"] = playableItemId.ToString();

                CurrentMediaCollection.Add(item);
            }
        }

        DateTime lastCall = DateTime.Now;

        /// <summary>
        /// Handles the MediaTransport.PropertyChanged event, which most of the time will be due to Position
        /// </summary>
        void MediaTransport_PropertyChanged(IPropertyObject sender, string property)
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

            int filePlaylistPosition;
            int currentMediaIndex;

            PlayableItem currentPlaybackItem = GetCurrentPlaybackItemFromPlayerState(metadataTitle, out filePlaylistPosition, out currentMediaIndex);

            Guid playableItemId = currentPlaybackItem == null ? Guid.Empty : currentPlaybackItem.Id;
            long duration = currentPlaybackItem == null ? 0 : GetDurationOfCurrentlyPlayingMedia(metadata);

            PlaybackStateEventArgs eventArgs = new PlaybackStateEventArgs() { 
                Position = positionTicks,
                FilePlaylistPosition = filePlaylistPosition, 
                DurationFromPlayer = duration,
                Item = currentPlaybackItem,
                CurrentMediaIndex = currentMediaIndex
            };

            // Only fire the progress handler while playback is still active, because once playback stops position will be reset to 0
            if (positionTicks > 0)
            {
                OnProgress(eventArgs);
            }

            if (property == "PlayState")
            {
                Logger.ReportVerbose("Playstate changed to {0} for {1}, PositionTicks:{2}, Playlist Index:{3}",
                  state, metadataTitle, positionTicks, filePlaylistPosition);

                HandlePlaystateChange(transport, isStopped, eventArgs);
            }
        }

        /// <summary>
        /// Retrieves the current playback item using MediaCollection properties
        /// </summary>
        protected virtual PlayableItem GetCurrentPlaybackItemFromPlayerState(string metadataTitle, out int filePlaylistPosition, out int currentMediaIndex)
        {
            filePlaylistPosition = 0;
            currentMediaIndex = 0;

            MediaCollectionItem activeItem = CurrentMediaCollection.Count == 0 ? null : CurrentMediaCollection[CurrentMediaCollection.CurrentIndex];

            if (activeItem == null)
            {
                return null;
            }

            Guid playableItemId = new Guid(activeItem.FriendlyData["PlayableItemId"].ToString());
            filePlaylistPosition = int.Parse(activeItem.FriendlyData["FilePlaylistPosition"].ToString());

            object objMediaIndex = activeItem.FriendlyData["MediaIndex"];

            if (objMediaIndex != null)
            {
                currentMediaIndex = int.Parse(objMediaIndex.ToString());
            }

            return GetPlayableItem(playableItemId);
        }

        /// <summary>
        /// Handles a change of Playstate by firing various events and post play processes
        /// </summary>
        private void HandlePlaystateChange(MediaTransport transport, bool isStopped, PlaybackStateEventArgs e)
        {
            Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => PlayStateChanged());

            Application.CurrentInstance.ShowNowPlaying = !isStopped;

            if (isStopped)
            {
                // Stop listening to the event
                transport.PropertyChanged -= MediaTransport_PropertyChanged;

                // Fire the OnFinished event for each item
                OnPlaybackFinished(e);

                //we're done - call post-processor
                Application.CurrentInstance.ReturnToApplication();

                // This will prevent us from getting in here twice after playback stops and calling post-play processes more than once.
                HasStartedPlaying = false;
            }
        }

        /// <summary>
        /// Puts the player into fullscreen mode
        /// </summary>
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

        #region Playback status
        public override bool IsPlayingVideo
        {
            get
            {
                // If the base class knows a PlayableItem is playing, use it
                if (base.IsPlaying)
                {
                    return base.IsPlayingVideo;
                }
                
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
                // If the base class knows a PlayableItem is playing
                if (base.IsPlaying)
                {
                    return true;
                }

                // Otherwise see another app within wmc is currently playing (such as live tv)
                return PlayState == PlayState.Playing;
            }
        }

        public override bool IsStopped
        {
            get 
            {
                // If the base class knows a PlayableItem is playing
                if (base.IsPlaying)
                {
                    return false;
                }

                return !IsPlaying && !IsPaused; 
            }
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
                // If base class knows of a PlayableItem playing, return base value
                if (base.IsPlaying)
                {
                    return base.NowPlayingTitle;
                }

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

        /// <summary>
        /// Pauses playback
        /// </summary>
        public override void Pause()
        {
            var transport = MediaTransport;
            if (transport != null)
            {
                transport.PlayRate = 1;
            }
        }

        /// <summary>
        /// Stops playback
        /// </summary>
        protected override void StopInternal()
        {
            var transport = MediaTransport;
            if (transport != null)
            {
                transport.PlayRate = 0;
            }
        }

        /// <summary>
        /// Takes a Media object and returns the list of files that will be sent to the PlaybackController
        /// </summary>
        /// <param name="media"></param>
        /// <returns></returns>
        internal override IEnumerable<string> GetPlayableFiles(Media media)
        {
            IEnumerable<string> files = base.GetPlayableFiles(media);

            Video video = media as Video;

            // Prefix dvd's with dvd://
            if (video != null && video.MediaType == Library.MediaType.DVD)
            {
                files = files.Select(i => GetDVDPath(i));
            }

            return files;
        }

        /// <summary>
        /// Takes a path to a DVD folder and returns the path to send to the player
        /// </summary>
        private string GetDVDPath(string path)
        {
            if (path.StartsWith("\\\\"))
            {
                path = path.Substring(2);
            }

            path = path.Replace("\\", "/").TrimEnd('/');

            return "DVD://" + path + "/";
        }

        protected override void Dispose(bool isDisposing)
        {

            MediaTransport transport = MediaTransport;

            if (transport != null)
            {
                transport.PropertyChanged -= MediaTransport_PropertyChanged;
            }

            base.Dispose(isDisposing);

        }

    }
}
