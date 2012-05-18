﻿using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Library;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Playables;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;

namespace MediaBrowser.Code.ModelItems
{
    /// <summary>
    /// Represents an abstract base class for a PlaybackController
    /// This has no knowledge of any specific player.
    /// </summary>
    public abstract class BasePlaybackController : BaseModelItem
    {
        public long CurrentFilePositionTicks { get; private set; }
        public long CurrentFileDurationTicks { get; private set; }
        private bool _IsStopping;

        protected bool IsDisposing { get; private set; }

        /// <summary>
        /// Subclasses can use this to examine the items that are currently in the player.
        /// </summary>
        protected List<PlayableItem> CurrentPlayableItems = new List<PlayableItem>();

        /// <summary>
        /// Holds the id of the currently playing PlayableItem
        /// </summary>
        public Guid CurrentPlayableItemId { get; private set; }

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
        protected virtual void OnProgress(PlaybackStateEventArgs args)
        {
            CurrentFileDurationTicks = args.DurationFromPlayer;
            CurrentFilePositionTicks = args.Position;

            // Set the current PlayableItem based on the incoming args
            CurrentPlayableItemId = args.Item == null ? Guid.Empty : args.Item.Id;

            NormalizeEventProperties(args);

            PlayableItem playable = args.Item;

            // Update PlayableItem progress event
            // Only report progress to the PlayableItem if the position is 0, because the player may start with an initial position of 0, or reset to 0 when stopping
            // This could end up blowing away resume data
            if (playable != null && args.Position > 0)
            {
                // Fire it's progress event handler
                playable.OnProgress(this, args);
            }

            // Fire PlaybackController progress event
            if (_Progress != null)
            {
                _Progress(this, args);
            }
        }

        /// <summary>
        /// This updates Playstates, runs post-play actions and fires the PlaybackFinished event
        /// </summary>
        protected virtual void OnPlaybackFinished(PlaybackStateEventArgs args)
        {
            _IsStopping = true;

            NormalizeEventProperties(args);

            // Fire the finished event for each PlayableItem
            foreach (PlayableItem playable in CurrentPlayableItems)
            {
                playable.OnPlaybackFinished(this, args);
            }

            // Fire the playback controller's finished event
            if (_PlaybackFinished != null)
            {
                _PlaybackFinished(this, args);
            }

            // Run the kernel's post play processes
            RunPostPlayProcesses();

            // Show or hide the resume button depending on playstate
            UpdateResumeStatusInUI();

            SetPlaybackStage(PlayableItemPlayState.PostPlayActionsComplete);

            // Clear state
            CurrentPlayableItemId = Guid.Empty;
            CurrentPlayableItems.Clear();

            ResetPlaybackProperties();

            PlayStateChanged();

            Logger.ReportVerbose("All post-playback actions have completed.");
        }

        /// <summary>
        /// This is designed to help subclasses during the Progress and Finished event.
        /// Most subclasses can identify the currently playing file, so this uses that to determine the corresponding Media object that's playing
        /// If a subclass can figure this out on their own, it's best that they do so to avoid traversing the entire playlist
        /// </summary>
        private void NormalizeEventProperties(PlaybackStateEventArgs args)
        {
            PlayableItem playable = args.Item;

            if (playable != null)
            {
                // Auto-fill current file index if there's only one file
                if (args.CurrentFileIndex == -1 && playable.FilesFormattedForPlayer.Count() == 1)
                {
                    args.CurrentFileIndex = 0;
                }

                // Fill this in if the subclass wasn't able to supply it
                if (playable.HasMediaItems && args.CurrentMediaIndex == -1)
                {
                    // If there's only one Media item, set CurrentMediaId in the args object
                    // This is just a convenience for subclasses that only support one Media at a time
                    if (playable.MediaItems.Count() == 1)
                    {
                        args.CurrentMediaIndex = 0;
                    }
                    else
                    {
                        SetMediaEventPropertiesBasedOnCurrentFileIndex(playable, args);
                    }
                }
            }
        }

        /// <summary>
        /// Sets the playback stage for each active PlayableItem
        /// </summary>
        private void SetPlaybackStage(PlayableItemPlayState stage)
        {
            foreach (PlayableItem playable in CurrentPlayableItems)
            {
                playable.PlayState = stage;
            }
        }

        /// <summary>
        /// Plays media
        /// </summary>
        public void Play(PlayableItem playable)
        {
            // Break down the Media items into raw files
            PopulatePlayableFiles(playable);

            // Add it to the list of active PlayableItems
            CurrentPlayableItems.Add(playable);

            // If we're playing then this becomes the active item
            if (!playable.QueueItem)
            {
                CurrentPlayableItemId = playable.Id;
            }

            PlayMediaInternal(playable);

            // Set the current playback stage
            playable.PlayState = playable.QueueItem ? PlayableItemPlayState.Queued : PlayableItemPlayState.Playing;

            PlayStateChanged();
        }

        protected virtual void ResetPlaybackProperties()
        {
            _IsStopping = false;

            CurrentFileDurationTicks = 0;
            CurrentFilePositionTicks = 0;
        }

        /// <summary>
        /// Stops whatever is currently playing
        /// </summary>
        public void Stop()
        {
            StopInternal();
        }

        /// <summary>
        /// Gets a friendly, displayable name for the current controller
        /// </summary>
        public abstract string ControllerName { get; }

        /// <summary>
        /// Determines if the player can be programatically paused
        /// </summary>
        public abstract bool CanPause { get; }

        /// <summary>
        /// Determines if the player can be programatically seeked
        /// </summary>
        public abstract bool CanSeek { get; }
       
        public abstract void Pause();
        public abstract void UnPause();
        protected abstract void PlayMediaInternal(PlayableItem playable);
        protected abstract void StopInternal();
        public abstract void Seek(long position);
        public abstract void GoToFullScreen();

        /// <summary>
        /// Gets the current playstate of the player
        /// </summary>
        public PlaybackControllerPlayState PlayState
        {
            get
            {
                if (CurrentPlayableItems.Any())
                {
                    if (_IsStopping)
                    {
                        return PlaybackControllerPlayState.Stopping;
                    }
                    else if (IsPaused)
                    {
                        return PlaybackControllerPlayState.Paused;
                    }

                    return PlaybackControllerPlayState.Playing;
                }

                return PlaybackControllerPlayState.Idle;
            }
        }
        
        /// <summary>
        /// Determines whether or not the controller is currently playing
        /// </summary>
        public virtual bool IsPlaying
        {
            get
            {
                return CurrentPlayableItems.Any();
            }
        }

        /// <summary>
        /// Determines whether or not the controller is currently playing video
        /// </summary>
        public virtual bool IsPlayingVideo
        {
            get
            {
                return IsPlaying && HasVideo;
            }
        }

        /// <summary>
        /// Determines if the player is currently paused
        /// </summary>
        public virtual bool IsPaused
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Determines if the current media is video
        /// </summary>
        private bool HasVideo
        {
            get
            {
                PlayableItem playable = GetCurrentPlayableItem();

                // If something is playing but we can't determine what, then we'll just have to assume true
                if (playable == null)
                {
                    return true;
                }

                if (playable.HasMediaItems)
                {
                    var media = playable.CurrentMedia;

                    // If we can pinpoint the current Media object, test that
                    if (media != null)
                    {
                        return PlaybackControllerHelper.IsVideo(media);
                    }
                }
                else
                {
                    string currentFile = playable.CurrentFile;

                    // See if the current file is a video
                    if (!string.IsNullOrEmpty(currentFile))
                    {
                        return PlaybackControllerHelper.IsVideo(currentFile);
                    }
                }

                return PlaybackControllerHelper.HasVideo(playable);

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
                    var playable = GetCurrentPlayableItem();

                    return playable == null ? "Unknown" : playable.DisplayName;
                }

                return "None";
            }
        }

        /// <summary>
        /// If playback is based on Media items, this will take the list of Media and determine the actual playable files and set them into the Files property
        /// This of course requires a traversal through the whole playback list, so subclasses can skip this if they're able to do during the process of initiating playback
        /// </summary>
        private void PopulatePlayableFiles(PlayableItem playable)
        {
            if (playable.HasMediaItems)
            {
                List<string> files = new List<string>(playable.MediaItems.Count());

                foreach (Media media in playable.MediaItems)
                {
                    files.AddRange(GetPlayableFiles(media));
                }

                playable.Files = files;

                playable.FilesFormattedForPlayer = files;
            }
            else
            {
                playable.FilesFormattedForPlayer = GetPlayableFiles(playable.Files);
            }
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
        public PlayableItem GetCurrentPlayableItem()
        {
            return GetPlayableItem(CurrentPlayableItemId);
        }

        public IEnumerable<PlayableItem> GetAllPlayableItems()
        {
            return CurrentPlayableItems;
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
        private void SetMediaEventPropertiesBasedOnCurrentFileIndex(PlayableItem playable, PlaybackStateEventArgs state)
        {
            int mediaIndex = -1;

            if (state.CurrentFileIndex != -1)
            {
                int totalFileCount = 0;
                int numMediaItems = playable.MediaItems.Count();

                for (int i = 0; i < numMediaItems; i++)
                {
                    int numFiles = playable.MediaItems.ElementAt(i).Files.Count();

                    if (totalFileCount + numFiles > state.CurrentFileIndex)
                    {
                        mediaIndex = i;
                        break;
                    }

                    totalFileCount += numFiles;
                }
            }

            state.CurrentMediaIndex = mediaIndex;
        }

        /// <summary>
        /// Runs all post-play processes
        /// </summary>
        private void RunPostPlayProcesses()
        {
            if (CurrentPlayableItems.Any(p => p.RaiseGlobalPlaybackEvents))
            {
                Application.CurrentInstance.RunPostPlayProcesses();
            }
        }

        /// <summary>
        /// Updates the Resume status in the UI, if needed
        /// </summary>
        private void UpdateResumeStatusInUI()
        {
            Item item = Application.CurrentInstance.CurrentItem;

            if (item.IsPlayable)
            {
                item.UpdateResume();
            }
        }

        /// <summary>
        /// When playback is based on media items, this will take a single Media object and return the raw list of files that will be played
        /// </summary>
        internal virtual IEnumerable<string> GetPlayableFiles(Media media)
        {
            Video video = media as Video;

            if (video != null && video.MediaType == MediaType.ISO)
            {
                return video.IsoFiles;
            }

            return media.Files;
        }

        /// <summary>
        /// When playback is based purely on files, this will take the files that were supplied to the PlayableItem,
        /// and create the actual paths that will be sent to the player
        /// </summary>
        internal virtual IEnumerable<string> GetPlayableFiles(IEnumerable<string> files)
        {
            return files;
        }

        protected void PlayStateChanged()
        {
            Microsoft.MediaCenter.UI.Application.DeferredInvoke(_ =>
            {
                FirePropertyChanged("PlayState");
                FirePropertyChanged("IsPlaying");
                FirePropertyChanged("IsPlayingVideo");
                FirePropertyChanged("IsStopped");
                FirePropertyChanged("IsPaused");
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                Logger.ReportVerbose(ControllerName + " is disposing");
            }

            IsDisposing = IsDisposing;

            base.Dispose(isDisposing);
        }
    }

    public enum PlaybackControllerPlayState
    {
        Idle = 0,
        Stopping = 1,
        Playing = 2,
        Paused = 3
    }
}
