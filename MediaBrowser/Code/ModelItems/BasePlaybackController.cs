using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Library;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Playables;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Code.ModelItems
{
    /// <summary>
    /// Represents an abstract base class for a PlaybackController
    /// This has no knowledge of any specific player.
    /// </summary>
    public abstract class BasePlaybackController : BaseModelItem, IPlaybackController
    {
        /// <summary>
        /// Subclasses can use this to examine the items that are currently in the player.
        /// </summary>
        protected List<PlayableItem> CurrentPlayableItems = new List<PlayableItem>();

        /// <summary>
        /// Holds the id of the currently playing PlayableItem
        /// </summary>
        private Guid CurrentPlayableItemId;

        #region Progress EventHandler
        volatile EventHandler<PlaybackStateEventArgs> _Progress;
        /// <summary>
        /// Fires during playback when the position changes
        /// </summary>
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
        #endregion

        #region PlaybackFinished EventHandler
        volatile EventHandler<PlaybackStateEventArgs> _PlaybackFinished;
        /// <summary>
        /// Fires when playback completes
        /// </summary>
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
        #endregion

        /// <summary>
        /// This updates Playstates and fires the Progress event
        /// </summary>
        protected void OnProgress(PlaybackStateEventArgs args)
        {
            // Set the current PlayableItem based on the incoming args
            CurrentPlayableItemId = args.Item == null ? Guid.Empty : args.Item.Id;

            AddEventArgsProperties(args);

            PlayableItem playable = args.Item;

            // Update playstates
            if (playable != null)
            {
                SaveProgressIntoPlaystates(playable, args);
                playable.HasUpdatedPlayState = true;
            }

            // Fire event handler
            if (_Progress != null)
            {
                _Progress(this, args);
            }
        }

        /// <summary>
        /// This updates Playstates, runs post-play actions and fires the PlaybackFinished event
        /// </summary>
        protected void OnPlaybackFinished(PlaybackStateEventArgs args)
        {
            AddEventArgsProperties(args);

            PlayableItem playable = args.Item;

            // Update playstates as long as the position hasn't been reset to 0 yet
            if (playable != null && args.Position > 0)
            {
                SaveProgressIntoPlaystates(playable, args);
                playable.HasUpdatedPlayState = true;
            }

            // If progress never fired at all we'll just have to assume everything was watched
            MarkWatchedIfNeeded();

            // Show or hide the resume button depending on playstate
            UpdateResumeStatusInUI();

            RunPostPlayProcesses();

            // Fire event handler
            if (_PlaybackFinished != null)
            {
                _PlaybackFinished(this, args);
            }

            // Clear state
            CurrentPlayableItemId = Guid.Empty;
            CurrentPlayableItems.Clear();
        }

        /// <summary>
        /// This is designed to help subclasses during the Progress and Finished event.
        /// Most subclasses can identify the currently playing file, so this uses that to determine the corresponding Media object that's playing
        /// If a subclass can figure this out on their own, it's best that they do so to avoid traversing the entire playlist
        /// </summary>
        private void AddEventArgsProperties(PlaybackStateEventArgs args)
        {
            PlayableItem playable = args.Item;

            if (playable != null)
            {
                // Fill this in if the subclass wasn't able to supply it
                if (playable.HasMediaItems && (args.CurrentMediaId == null || args.CurrentMediaId == Guid.Empty))
                {
                    // If there's only one Media item, set CurrentMediaId in the args object
                    // This is just a convenience for subclasses that only support one Media at a time
                    if (playable.MediaItems.Count() == 1)
                    {
                        args.CurrentMediaId = playable.MediaItems.First().Id;
                    }
                    else
                    {
                        SetMediaEventPropertiesBasedOnCurrentFile(playable, args);
                    }
                }
            }
        }

        /// <summary>
        /// Plays media
        /// </summary>
        public void PlayMedia(PlayableItem playable)
        {
            if (playable.HasMediaItems)
            {
                PopulatePlayableFiles(playable);
            }

            CurrentPlayableItems.Add(playable);
            CurrentPlayableItemId = playable.Id;
            PlayMediaInternal(playable);
        }

        /// <summary>
        /// Queues media
        /// </summary>
        public void QueueMedia(PlayableItem playable)
        {
            if (playable.HasMediaItems)
            {
                PopulatePlayableFiles(playable);
            }

            CurrentPlayableItems.Add(playable);
            QueueMediaInternal(playable);
        }

        /// <summary>
        /// Stops whatever is currently playing
        /// </summary>
        public void Stop()
        {
            StopInternal();
        }

        public abstract void Pause();
        protected abstract void PlayMediaInternal(PlayableItem playable);
        protected abstract void StopInternal();
        public abstract void Seek(long position);
        public abstract void GoToFullScreen();

        /// <summary>
        /// Queues media
        /// </summary>
        protected virtual void QueueMediaInternal(PlayableItem playable)
        {
            // We will implement this and just have it throw an exception, since not all players can queue
            // If a player can queue, it will need to override this
            throw new NotSupportedException();
        }

        /// <summary>
        /// Determines whether or not the controller is currently playing
        /// </summary>
        public virtual bool IsPlaying
        {
            get
            {
                return CurrentPlayableItems.Count > 0;
            }
        }

        /// <summary>
        /// Determines whether or not the controller is currently playing video
        /// </summary>
        public virtual bool IsPlayingVideo
        {
            get
            {
                return IsPlaying;
            }
        }

        /// <summary>
        /// Determines whether or not the controller is currently stopped
        /// </summary>
        public virtual bool IsStopped
        {
            get
            {
                return CurrentPlayableItems.Count == 0;
            }
        }

        /// <summary>
        /// Determines if the player is currently paused
        /// </summary>
        public virtual bool IsPaused
        {
            get
            {
                // For the majority of players there will be no way to determine this
                // Those that can should override
                return false;
            }
        }

        /// <summary>
        /// Gets the title of the currently playing media
        /// </summary>
        public virtual string NowPlayingTitle
        {
            get
            {
                if (IsPlaying)
                {
                    return GetCurrentPlayableItem().Name;
                }

                return "None";
            }
        }

        /// <summary>
        /// If playback is based on Media items, this will take the list of Media and determine the actual playable files and set them into the Files property
        /// This of course requires a traversal through the whole playback list, so subclasses can skip this if they're able to do during the process of initiating playback
        /// </summary>
        protected virtual void PopulatePlayableFiles(PlayableItem playable)
        {
            IEnumerable<string> files = playable.MediaItems.Select(m => GetPlayableFiles(m)).SelectMany(i => i);

            playable.Files.Clear();
            playable.Files.AddRange(files);
        }

        /// <summary>
        /// Formats a media path for display as the title
        /// </summary>
        protected string FormatPathForDisplay(string path)
        {
            if (path.ToLower().StartsWith("file://"))
            {
                path = path.Substring(7);
            }

            else if (path.ToLower().StartsWith("dvd://"))
            {
                path = path.Substring(6);
            }

            int index = path.LastIndexOf('/');

            if (index != -1)
            {
                path = path.Substring(index + 1);
            }

            // Remove file extension
            index = path.LastIndexOf('.');

            if (index != -1)
            {
                path = path.Substring(0, index);
            }

            return path;
        }

        /// <summary>
        /// Processes commands
        /// </summary>
        public virtual void ProcessCommand(RemoteCommand command)
        {
            // dont do anything (only plugins need to handle this)
        }

        /// <summary>
        /// Determines if an external playback page is required
        /// </summary>
        public virtual bool RequiresExternalPage
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the item currently playing
        /// </summary>
        protected virtual PlayableItem GetCurrentPlayableItem()
        {
            return GetPlayableItem(CurrentPlayableItemId);
        }

        /// <summary>
        /// Gets a PlayableItem in the player based on it's Id
        /// </summary>
        protected PlayableItem GetPlayableItem(Guid playableItemId)
        {
            return CurrentPlayableItems.FirstOrDefault(p => p.Id == CurrentPlayableItemId);
        }

        /// <summary>
        /// Takes the current playing file in PlaybackStateEventArgs and uses that to determine the corresponding Media object
        /// </summary>
        private void SetMediaEventPropertiesBasedOnCurrentFile(PlayableItem playable, PlaybackStateEventArgs state)
        {
            foreach (Media media in playable.MediaItems)
            {
                List<string> playableFiles = GetPlayableFiles(media).ToList();
                int index = playableFiles.IndexOf(state.CurrentFile);

                if (index != -1)
                {
                    state.CurrentMediaId = media.Id;
                    state.MediaPlaylistPosition = index;
                    break;
                }
            }
        }

        /// <summary>
        /// Goes through each Media object within PlayableMediaItems and updates Playstate for each individually
        /// </summary>
        private void SaveProgressIntoPlaystates(PlayableItem playable, PlaybackStateEventArgs state)
        {
            foreach (Media media in playable.MediaItems)
            {
                bool isCurrentMedia = media.Id == state.CurrentMediaId;

                long currentPositionTicks = 0;
                int currentPlaylistPosition = 0;

                if (isCurrentMedia)
                {
                    // If this is where playback is, update position and playlist
                    currentPlaylistPosition = state.MediaPlaylistPosition;
                    currentPositionTicks = state.Position;
                }

                Application.CurrentInstance.UpdatePlayState(media, media.PlaybackStatus, currentPlaylistPosition, currentPositionTicks, null, playable.PlaybackStartTime);

                if (isCurrentMedia)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Marks all Media objects as watched, if progress has not been saved at all yet
        /// </summary>
        private void MarkWatchedIfNeeded()
        {
            foreach (PlayableItem playable in CurrentPlayableItems)
            {
                if (!playable.HasUpdatedPlayState)
                {
                    foreach (Media media in playable.MediaItems)
                    {
                        Logger.ReportVerbose("Marking watched: " + media.Name);
                        Application.CurrentInstance.UpdatePlayState(media, media.PlaybackStatus, 0, 0, null, playable.PlaybackStartTime);
                    }
                }
            }
        }

        /// <summary>
        /// Runs all post-play processes
        /// </summary>
        private void RunPostPlayProcesses()
        {
            bool runKernelPostPlay = CurrentPlayableItems.Any(p => p.RaisePlaybackEvents);

            foreach (PlayableItem playable in CurrentPlayableItems)
            {
                Application.CurrentInstance.RunPostPlayProcesses(playable, runKernelPostPlay);

                // Only do this once
                runKernelPostPlay = false;
            }

            Logger.ReportVerbose("All post-playback actions have completed.");
        }

        /// <summary>
        /// Updates the Resume status in the UI, if needed
        /// </summary>
        private void UpdateResumeStatusInUI()
        {
            Item item = Application.CurrentInstance.CurrentItem;
            Guid currentMediaId = item.BaseItem.Id;

            foreach (PlayableItem playable in CurrentPlayableItems)
            {
                foreach (Media media in playable.MediaItems)
                {
                    if (media.Id == currentMediaId)
                    {
                        item.UpdateResume();
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the raw playable files for a given Media object
        /// </summary>
        protected virtual IEnumerable<string> GetPlayableFiles(Media media)
        {
            Video video = media as Video;

            if (video != null && video.MediaType == MediaType.ISO)
            {
                return video.IsoFiles;
            }

            return media.Files;
        }

        protected override void Dispose(bool isDisposing)
        {
            Logger.ReportVerbose(GetType().Name + " is disposing");

            base.Dispose(isDisposing);
        }
    }
}
