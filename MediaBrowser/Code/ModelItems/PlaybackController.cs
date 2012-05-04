using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Playables;
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
        private MediaCollection CurrentMediaCollection;

        /// <summary>
        /// Plays Media
        /// </summary>
        protected override void PlayMediaInternal(PlayableItem playable)
        {
            if (playable.QueueItem)
            {
                Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => QueuePlayableItem(playable));
            }
            else
            {
                Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => PlayPlayableItem(playable));
            }
        }

        // After calling MediaCenterEnvironment.PlayMedia, playback will begin with a state of Stopped and position 0
        // We'll record it when we see it so we don't get tripped up into thinking playback has actually stopped
        private bool HasStartedPlaying = false;

        /// <summary>
        /// Plays or queues Media
        /// </summary>
        protected virtual void PlayPlayableItem(PlayableItem playable)
        {
            HasStartedPlaying = false;

            // Get this now since we'll be using it frequently
            MediaCenterEnvironment mediaCenterEnvironment = AddInHost.Current.MediaCenterEnvironment;

            try
            {
                CallPlayMediaForPlayableItem(mediaCenterEnvironment, playable);

                MediaExperience mediaExperience;

                if (playable.GoFullScreen)
                {
                    mediaExperience = mediaCenterEnvironment.MediaExperience ?? PlaybackControllerHelper.GetMediaExperienceUsingReflection();

                    if (!mediaExperience.IsFullScreen)
                    {
                        mediaExperience.GoToFullScreen();
                    }
                }

                // Get this again as I've seen issues where it gets reset after the above call
                mediaExperience = mediaCenterEnvironment.MediaExperience ?? PlaybackControllerHelper.GetMediaExperienceUsingReflection();

                // Attach event handler
                MediaTransport transport = mediaExperience.Transport;
                transport.PropertyChanged -= MediaTransport_PropertyChanged;
                transport.PropertyChanged += MediaTransport_PropertyChanged;
            }
            catch (Exception ex)
            {
                Logger.ReportException("Playing media failed.", ex);
                Application.ReportBrokenEnvironment();
            }
        }

        /// <summary>
        /// Calls PlayMedia using either a MediaCollection or a single file
        /// </summary>
        protected virtual void CallPlayMediaForPlayableItem(MediaCenterEnvironment mediaCenterEnvironment, PlayableItem playable)
        {
            if (PlaybackControllerHelper.UseLegacyApi(playable))
            {
                CallPlayMediaLegacy(mediaCenterEnvironment, playable);
                CurrentMediaCollection = null;
            }
            else
            {
                CallPlayMediaUsingMediaCollection(mediaCenterEnvironment, playable);
            }
        }

        private void CallPlayMediaUsingMediaCollection(MediaCenterEnvironment mediaCenterEnvironment, PlayableItem playable)
        {
            MediaCollection coll = new MediaCollection();

            // Create a MediaCollectionItem for each file to play
            if (playable.HasMediaItems)
            {
                PlaybackControllerHelper.PopulateMediaCollectionUsingMediaItems(this, coll, playable);
            }
            else
            {
                PlaybackControllerHelper.PopulateMediaCollectionUsingFiles(coll, playable);
            }

            // Set starting position if we're resuming
            if (playable.Resume)
            {
                var playstate = playable.MediaItems.First().PlaybackStatus;

                coll.CurrentIndex = playstate.PlaylistPosition;
                coll[playstate.PlaylistPosition].Start = new TimeSpan(playstate.PositionTicks);
            }

            CurrentMediaCollection = coll;
            PlaybackControllerHelper.CallPlayMedia(mediaCenterEnvironment, MediaType.MediaCollection, CurrentMediaCollection, false);
        }

        private void CallPlayMediaLegacy(MediaCenterEnvironment mediaCenterEnvironment, PlayableItem playable)
        {
            Microsoft.MediaCenter.MediaType type = PlaybackControllerHelper.GetMediaType(playable);
            
            // Need to create a playlist
            if (PlaybackControllerHelper.RequiresWPL(playable))
            {
                IEnumerable<string> files = playable.Files;

                int playlistPosition = 0;

                if (playable.Resume)
                {
                    playlistPosition = playable.MediaItems.First().PlaybackStatus.PlaylistPosition;
                }

                string file = PlaybackControllerHelper.CreateWPLPlaylist(playable.Id.ToString(), files, ShouldTranscode, playlistPosition);

                PlaybackControllerHelper.CallPlayMedia(mediaCenterEnvironment, type, file, false);
            }
            else
            {
                // Play single file
                string file = playable.Files.First();

                if (ShouldTranscode)
                {
                    file = PlaybackControllerHelper.GetTranscodedPath(file);
                }

                PlaybackControllerHelper.CallPlayMedia(mediaCenterEnvironment, type, file, false);
            }

            if (playable.Resume)
            {
                long position = playable.MediaItems.First().PlaybackStatus.PositionTicks;

                if (position > 0)
                {
                    mediaCenterEnvironment.MediaExperience.Transport.Position = new TimeSpan(position);
                }
            }
        }

        protected virtual void QueuePlayableItem(PlayableItem playable)
        {
            if (CurrentMediaCollection == null)
            {
                QueuePlayableItemLegacy(playable);
            }
            else
            {
                QueuePlayableItemIntoMediaCollection(playable);
            }
        }

        private void QueuePlayableItemLegacy(PlayableItem playable)
        {
            Microsoft.MediaCenter.MediaType type = MediaType.Audio;

            foreach (string file in playable.Files)
            {
               string fileToPlay = ShouldTranscode ? PlaybackControllerHelper.GetTranscodedPath(file) : file;

                PlaybackControllerHelper.CallPlayMedia(AddInHost.Current.MediaCenterEnvironment, type, fileToPlay, true);
            }
        }

        private void QueuePlayableItemIntoMediaCollection(PlayableItem playable)
        {
            // Create a MediaCollectionItem for each file to play
            if (playable.HasMediaItems)
            {
                PlaybackControllerHelper.PopulateMediaCollectionUsingMediaItems(this, CurrentMediaCollection, playable);
            }
            else
            {
                PlaybackControllerHelper.PopulateMediaCollectionUsingFiles(CurrentMediaCollection, playable);
            }
        }

        DateTime lastCall = DateTime.Now;

        /// <summary>
        /// Handles the MediaTransport.PropertyChanged event, which most of the time will be due to Position
        /// </summary>
        protected void MediaTransport_PropertyChanged(IPropertyObject sender, string property)
        {
            MediaTransport transport = sender as MediaTransport;

            MediaExperience mce = MediaExperience;

            PlayState state;
            long positionTicks = 0;

            // If another application is playing the content, such as the WMC autoplay handler, we will
            // not have permission to access Transport properties
            // But we can look at MediaExperience.MediaType to determine if something is playing
            try
            {
                state = transport.PlayState;
                positionTicks = transport.Position.Ticks;
            }
            catch (InvalidOperationException)
            {
                state = mce.MediaType == Microsoft.MediaCenter.Extensibility.MediaType.Unknown ? Microsoft.MediaCenter.PlayState.Undefined : Microsoft.MediaCenter.PlayState.Playing;
            }

            // Don't get tripped up at the initial state of Stopped with position 0
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

            // Determine if playback has stopped. Per MSDN documentation, Finished is no longer used with Windows 7
            bool isStopped = state == Microsoft.MediaCenter.PlayState.Finished || state == Microsoft.MediaCenter.PlayState.Stopped || state == Microsoft.MediaCenter.PlayState.Undefined;

            // Get metadata from player
            MediaMetadata metadata = mce.MediaMetadata;

            int filePlaylistPosition;
            int currentMediaIndex;

            string metadataTitle = PlaybackControllerHelper.GetTitleOfCurrentlyPlayingMedia(metadata);
            PlayableItem currentPlaybackItem = GetCurrentPlaybackItemFromPlayerState(metadataTitle, out filePlaylistPosition, out currentMediaIndex);

            PlaybackStateEventArgs eventArgs = new PlaybackStateEventArgs()
            {
                Position = positionTicks,
                CurrentFileIndex = filePlaylistPosition,
                DurationFromPlayer = PlaybackControllerHelper.GetDurationOfCurrentlyPlayingMedia(metadata),
                Item = currentPlaybackItem,
                CurrentMediaIndex = currentMediaIndex
            };

            // Only fire the progress handler while playback is still active, because once playback stops position will be reset to 0
            if (positionTicks > 0)
            {
                OnProgress(eventArgs);
            }

            Application.CurrentInstance.ShowNowPlaying = true;

            if (property == "PlayState")
            {
                // Get the title from the PlayableItem, if it's available. Otherwise use MediaMetadata
                string title = currentPlaybackItem == null ? metadataTitle : (currentPlaybackItem.HasMediaItems ? currentPlaybackItem.MediaItems.ElementAt(currentMediaIndex).Name : currentPlaybackItem.Files.ElementAt(filePlaylistPosition));

                Logger.ReportVerbose("Playstate changed to {0} for {1}, PositionTicks:{2}, Playlist Index:{3}", state, title, positionTicks, filePlaylistPosition);

                HandlePlaystateChanged(mce, transport, isStopped, eventArgs);
            }
        }

        /// <summary>
        /// Retrieves the current playback item using MediaCollection properties
        /// </summary>
        private PlayableItem GetCurrentPlaybackItemFromPlayerState(string metadataTitle, out int filePlaylistPosition, out int currentMediaIndex)
        {
            if (CurrentMediaCollection == null)
            {
                return PlaybackControllerHelper.GetCurrentPlaybackItemUsingMetadataTitle(this, CurrentPlayableItems, metadataTitle, out filePlaylistPosition, out currentMediaIndex);
            }

            return PlaybackControllerHelper.GetCurrentPlaybackItemFromMediaCollection(CurrentPlayableItems, CurrentMediaCollection, out filePlaylistPosition, out currentMediaIndex);
        }

        /// <summary>
        /// Handles a change of Playstate by firing various events and post play processes
        /// </summary>
        private void HandlePlaystateChanged(MediaExperience mce, MediaTransport transport, bool isStopped, PlaybackStateEventArgs e)
        {
            if (isStopped)
            {
                // Stop listening to the event
                transport.PropertyChanged -= MediaTransport_PropertyChanged;

                // This will prevent us from getting in here twice after playback stops and calling post-play processes more than once.
                HasStartedPlaying = false;

                var mediaType = mce.MediaType;

                // Check if internal wmc player is still playing, which could happen if the user launches live tv while playing something
                if (mediaType != Microsoft.MediaCenter.Extensibility.MediaType.TV)
                {
                    Application.CurrentInstance.ShowNowPlaying = false;

                    bool forceReturn = mediaType == Microsoft.MediaCenter.Extensibility.MediaType.Audio || mediaType == Microsoft.MediaCenter.Extensibility.MediaType.DVD;

                    PlaybackControllerHelper.ReturnToApplication(forceReturn);
                }

                // Fire the OnFinished event for each item
                OnPlaybackFinished(e);
            }

            Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ => PlayStateChanged());
        }

        /// <summary>
        /// Puts the player into fullscreen mode
        /// </summary>
        public override void GoToFullScreen()
        {
            var mce = MediaExperience ?? PlaybackControllerHelper.GetMediaExperienceUsingReflection();

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

                if (!IsPlaying)
                {
                    return false;
                }

                // Otherwise see if another app within wmc is currently playing (such as live tv)
                Microsoft.MediaCenter.Extensibility.MediaType mediaType = PlaybackControllerHelper.GetCurrentMediaType();

                return mediaType != Microsoft.MediaCenter.Extensibility.MediaType.Unknown && mediaType != Microsoft.MediaCenter.Extensibility.MediaType.Audio;
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

                // Otherwise see if another app within wmc is currently playing (such as live tv)
                return PlaybackControllerHelper.GetCurrentPlayState() == PlayState.Playing;
            }
        }

        public override bool IsPaused
        {
            get { return PlaybackControllerHelper.GetCurrentPlayState() == PlayState.Paused; }
        }

        #endregion

        protected MediaExperience MediaExperience
        {
            get
            {
                return AddInHost.Current.MediaCenterEnvironment.MediaExperience;
            }
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

                // Another application could be responsible for the content playing, so try to come up with a title
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

                string name = PlaybackControllerHelper.GetTitleOfCurrentlyPlayingMedia(metadata).Trim('/');

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
            var transport = PlaybackControllerHelper.GetCurrentMediaTransport();
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
            var transport = PlaybackControllerHelper.GetCurrentMediaTransport();
            if (transport != null)
            {
                transport.PlayRate = 0;
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
                if (video.MediaType == Library.MediaType.DVD)
                {
                    files = files.Select(i => GetDVDPath(i));
                }
                else if (video.MediaType == Library.MediaType.BluRay)
                {
                    files = files.Select(i => GetBluRayPath(i));
                }
            }

            return ShouldTranscode ? files.Select(f => PlaybackControllerHelper.GetTranscodedPath(f)) : files;
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

            return "DVD://" + path + "/?1";
        }

        /// <summary>
        /// For Bluray folders this will return the largest m2ts file contained within. For the internal wmc player, this is the best we can do
        /// </summary>
        private string GetBluRayPath(string path)
        {
            string folder = Path.Combine(path, "bdmv\\stream");

            string movieFile = string.Empty;
            long size = 0;

            foreach (FileInfo file in new DirectoryInfo(folder).GetFiles("*.m2ts"))
            {
                long currSize = file.Length;

                if (currSize > size)
                {
                    movieFile = file.FullName;
                    size = currSize;
                }
            }

            return movieFile;
        }

        protected override void Dispose(bool isDisposing)
        {
            MediaTransport transport = PlaybackControllerHelper.GetCurrentMediaTransport();

            if (transport != null)
            {
                transport.PropertyChanged -= MediaTransport_PropertyChanged;
            }

            base.Dispose(isDisposing);
        }

        protected bool ShouldTranscode
        {
            get
            {
                return Config.Instance.EnableTranscode360 && Application.RunningOnExtender;
            }
        }

        /// <summary>
        /// Moves the player to a given position
        /// </summary>
        public override void Seek(long position)
        {
            var mce = AddInHost.Current.MediaCenterEnvironment;
            Logger.ReportVerbose("Trying to seek position :" + new TimeSpan(position).ToString());
            PlaybackControllerHelper.WaitForStream(mce);
            mce.MediaExperience.Transport.Position = new TimeSpan(position);
        }
    }
}
