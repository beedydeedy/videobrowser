using System;
using System.Collections.Generic;
using System.Linq;
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
        // After calling MediaCenterEnvironment.PlayMedia, playback will begin with a state of Stopped and position 0
        // We'll record it when we see it so we don't get tripped up into thinking playback has actually stopped
        private bool _HasStartedPlaying = false;
        private MediaCollection _CurrentMediaCollection;
        private DateTime _LastTransportUpdateTime = DateTime.Now;
        private Microsoft.MediaCenter.PlayState _CurrentPlayState;

        public override string ControllerName
        {
            get { return "Internal Player"; }
        }

        protected override void ResetPlaybackProperties()
        {
            base.ResetPlaybackProperties();

            _CurrentMediaCollection = null;
            _HasStartedPlaying = false;
            _CurrentPlayState = Microsoft.MediaCenter.PlayState.Undefined;
            _LastTransportUpdateTime = DateTime.Now;
        }

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

        /// <summary>
        /// Plays or queues Media
        /// </summary>
        protected virtual void PlayPlayableItem(PlayableItem playable)
        {
            _HasStartedPlaying = false;

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

                // Attach event handler to MediaCenterEnvironment
                // We need this because if you press stop on a dvd menu without every playing, Transport property changed will never fire
                mediaCenterEnvironment.PropertyChanged -= mediaCenterEnvironment_PropertyChanged; 
                mediaCenterEnvironment.PropertyChanged += mediaCenterEnvironment_PropertyChanged;

                // Attach event handler to MediaTransport
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
                _CurrentMediaCollection = null;
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

            _CurrentMediaCollection = coll;
            PlaybackControllerHelper.CallPlayMedia(mediaCenterEnvironment, MediaType.MediaCollection, _CurrentMediaCollection, false);
        }

        /// <summary>
        /// Calls PlayMedia
        /// </summary>
        private void CallPlayMediaLegacy(MediaCenterEnvironment mediaCenterEnvironment, PlayableItem playable)
        {
            Microsoft.MediaCenter.MediaType type = PlaybackControllerHelper.GetMediaType(playable);

            // Need to create a playlist
            if (PlaybackControllerHelper.RequiresWPL(playable))
            {
                IEnumerable<string> files = playable.FilesFormattedForPlayer;

                string file = PlaybackControllerHelper.CreateWPLPlaylist(playable.Id.ToString(), files, playable.StartPlaylistPosition);

                PlaybackControllerHelper.CallPlayMedia(mediaCenterEnvironment, type, file, false);
            }
            else
            {
                // Play single file
                string file = playable.FilesFormattedForPlayer.First();

                PlaybackControllerHelper.CallPlayMedia(mediaCenterEnvironment, type, file, false);
            }

            long position = playable.StartPositionTicks;

            if (position > 0)
            {
                mediaCenterEnvironment.MediaExperience.Transport.Position = new TimeSpan(position);
            }
        }

        protected virtual void QueuePlayableItem(PlayableItem playable)
        {
            if (_CurrentMediaCollection == null)
            {
                QueuePlayableItemLegacy(playable);
            }
            else
            {
                QueuePlayableItemIntoMediaCollection(playable);
            }
        }

        private void QueuePlayableItemIntoMediaCollection(PlayableItem playable)
        {
            // Create a MediaCollectionItem for each file to play
            if (playable.HasMediaItems)
            {
                PlaybackControllerHelper.PopulateMediaCollectionUsingMediaItems(this, _CurrentMediaCollection, playable);
            }
            else
            {
                PlaybackControllerHelper.PopulateMediaCollectionUsingFiles(_CurrentMediaCollection, playable);
            }
        }

        private void QueuePlayableItemLegacy(PlayableItem playable)
        {
            Microsoft.MediaCenter.MediaType type = MediaType.Audio;

            foreach (string file in playable.FilesFormattedForPlayer)
            {
                PlaybackControllerHelper.CallPlayMedia(AddInHost.Current.MediaCenterEnvironment, type, file, true);
            }
        }

        /// <summary>
        /// Handles the MediaCenterEnvironment.PropertyChanged event
        /// </summary>
        protected void mediaCenterEnvironment_PropertyChanged(IPropertyObject sender, string property)
        {
            MediaCenterEnvironment env = sender as MediaCenterEnvironment;

            MediaExperience mce = env.MediaExperience;

            MediaTransport transport = mce.Transport;

            HandlePropertyChange(env, mce, transport, property);
        }

        /// <summary>
        /// Handles the MediaTransport.PropertyChanged event, which most of the time will be due to Position
        /// </summary>
        protected void MediaTransport_PropertyChanged(IPropertyObject sender, string property)
        {
            MediaTransport transport = sender as MediaTransport;

            MediaCenterEnvironment env = AddInHost.Current.MediaCenterEnvironment;

            MediaExperience mce = env.MediaExperience;

            HandlePropertyChange(env, mce, transport, property);
        }

        private void HandlePropertyChange(MediaCenterEnvironment env, MediaExperience mce, MediaTransport transport, string property)
        {
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

            _CurrentPlayState = state;

            // Don't get tripped up at the initial state of Stopped with position 0
            if (!_HasStartedPlaying)
            {
                if (state == Microsoft.MediaCenter.PlayState.Playing)
                {
                    _HasStartedPlaying = true;
                }
                else
                {
                    return;
                }
            }

            // protect against really agressive calls
            if (property == "Position")
            {
                var diff = (DateTime.Now - _LastTransportUpdateTime).TotalMilliseconds;

                // Only cancel out Position reports
                if (diff < 1000 && diff >= 0)
                {
                    return;
                }
            }

            _LastTransportUpdateTime = DateTime.Now;

            // Determine if playback has stopped. Per MSDN documentation, Finished is no longer used with Windows 7
            bool isStopped = state == Microsoft.MediaCenter.PlayState.Finished || state == Microsoft.MediaCenter.PlayState.Stopped || state == Microsoft.MediaCenter.PlayState.Undefined;

            // Get metadata from player
            MediaMetadata metadata = mce.MediaMetadata;

            string metadataTitle = PlaybackControllerHelper.GetTitleOfCurrentlyPlayingMedia(metadata);
            long metadataDuration = PlaybackControllerHelper.GetDurationOfCurrentlyPlayingMedia(metadata);

            PlaybackStateEventArgs eventArgs = GetCurrentPlaybackState(metadataTitle, metadataDuration, positionTicks);

            // Only fire the progress handler while playback is still active, because once playback stops position will be reset to 0
            OnProgress(eventArgs);

            Application.CurrentInstance.ShowNowPlaying = true;

            if (property == "PlayState")
            {
                // Get the title from the PlayableItem, if it's available. Otherwise use MediaMetadata
                string title = eventArgs.Item == null ? metadataTitle : (eventArgs.Item.HasMediaItems ? eventArgs.Item.MediaItems.ElementAt(eventArgs.CurrentMediaIndex).Name : eventArgs.Item.Files.ElementAt(eventArgs.CurrentFileIndex));

                Logger.ReportVerbose("Playstate changed to {0} for {1}, PositionTicks:{2}, Playlist Index:{3}", state, title, positionTicks, eventArgs.CurrentFileIndex);

                HandlePlaystateChanged(env, mce, transport, isStopped, eventArgs);
            }
        }

        /// <summary>
        /// Handles a change of Playstate by firing various events and post play processes
        /// </summary>
        private void HandlePlaystateChanged(MediaCenterEnvironment env, MediaExperience mce, MediaTransport transport, bool isStopped, PlaybackStateEventArgs e)
        {
            if (isStopped)
            {
                // Stop listening to the events
                env.PropertyChanged -= mediaCenterEnvironment_PropertyChanged;
                transport.PropertyChanged -= MediaTransport_PropertyChanged;

                // This will prevent us from getting in here twice after playback stops and calling post-play processes more than once.
                _HasStartedPlaying = false;

                _CurrentMediaCollection = null;

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

            PlayStateChanged();
        }

        /// <summary>
        /// Retrieves the current playback item using properties from MediaExperience and Transport
        /// </summary>
        private PlaybackStateEventArgs GetCurrentPlaybackState(string metadataTitle, long metadataDuration, long positionTicks)
        {
            int filePlaylistPosition;
            int currentMediaIndex;
            PlayableItem currentPlayableItem;

            if (_CurrentMediaCollection == null)
            {
                currentPlayableItem = PlaybackControllerHelper.GetCurrentPlaybackItemUsingMetadataTitle(this, CurrentPlayableItems, metadataTitle, out filePlaylistPosition, out currentMediaIndex);
            }
            else
            {
                currentPlayableItem = PlaybackControllerHelper.GetCurrentPlaybackItemFromMediaCollection(CurrentPlayableItems, _CurrentMediaCollection, out filePlaylistPosition, out currentMediaIndex);

                // When playing multiple files with MediaCollections, if you allow playback to finish, CurrentIndex will be reset to 0, but transport.Position will be equal to the duration of the last item played
                if (filePlaylistPosition == 0 && positionTicks >= metadataDuration)
                {
                    positionTicks = 0;
                }
            }

            return new PlaybackStateEventArgs()
            {
                Position = positionTicks,
                CurrentFileIndex = filePlaylistPosition,
                DurationFromPlayer = metadataDuration,
                Item = currentPlayableItem,
                CurrentMediaIndex = currentMediaIndex
            };
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

        protected MediaExperience MediaExperience
        {
            get
            {
                return AddInHost.Current.MediaCenterEnvironment.MediaExperience;
            }
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
        /// Unpauses playback
        /// </summary>
        public override void UnPause()
        {
            var transport = PlaybackControllerHelper.GetCurrentMediaTransport();
            if (transport != null)
            {
                transport.PlayRate = 2;
            }
        }

        /// <summary>
        /// Stops playback
        /// </summary>
        protected override void StopInternal()
        {
            PlaybackControllerHelper.Stop();
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
                    files = files.Select(i => PlaybackControllerHelper.GetDVDPath(i));
                }
                else if (video.MediaType == Library.MediaType.BluRay)
                {
                    files = files.Select(i => PlaybackControllerHelper.GetBluRayPath(i));
                }
            }

            return ShouldTranscode ? files.Select(f => PlaybackControllerHelper.GetTranscodedPath(f)) : files;
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

                if (mediaType == Library.MediaType.DVD)
                {
                    yield return PlaybackControllerHelper.GetDVDPath(file);
                }
                else if (mediaType == Library.MediaType.BluRay)
                {
                    yield return PlaybackControllerHelper.GetBluRayPath(file);
                }
                else if (mediaType == Library.MediaType.HDDVD || mediaType == Library.MediaType.ISO)
                {
                    yield return file;
                }

                yield return ShouldTranscode ? PlaybackControllerHelper.GetTranscodedPath(file) : file;
            }
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

        public override bool IsPaused
        {
            get
            {
                return _CurrentPlayState == Microsoft.MediaCenter.PlayState.Paused;
            }
        }

        public override bool CanPause
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }
    }
}
