﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Events;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Encapsulates play back for Media.
    /// </summary>
    public abstract class PlayableItem
    {
        #region Progress EventHandler
        volatile EventHandler<GenericEventArgs<PlayableItem>> _Progress;
        /// <summary>
        /// Fires whenever the PlaybackController reports playback progress
        /// </summary>
        public event EventHandler<GenericEventArgs<PlayableItem>> Progress
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

        internal void OnProgress(BasePlaybackController controller, PlaybackStateEventArgs args)
        {
            CurrentFileIndex = args.CurrentFileIndex;
            CurrentMediaIndex = args.CurrentMediaIndex;

            SaveProgressIntoPlaystates(controller, args);

            PlayState = PlayableItemPlayState.Playing;

            if (_Progress != null)
            {
                _Progress(this, new GenericEventArgs<PlayableItem>() { Item = this });
            }
        }
        #endregion

        #region PlayStateChanged EventHandler
        volatile EventHandler<GenericEventArgs<PlayableItem>> _PlayStateChanged;
        /// <summary>
        /// Fires when the current instance finishes playback
        /// </summary>
        public event EventHandler<GenericEventArgs<PlayableItem>> PlayStateChanged
        {
            add
            {
                _PlayStateChanged += value;
            }
            remove
            {
                _PlayStateChanged -= value;
            }
        }

        private void OnPlayStateChanged()
        {
            if (PlayState == PlayableItemPlayState.Stopped)
            {
                Application.CurrentInstance.RunPostPlayProcesses(this);

                if (UnmountISOAfterPlayback)
                {
                    Application.CurrentInstance.UnmountIso();
                }
            }

            if (_PlayStateChanged != null)
            {
                _PlayStateChanged(this, new GenericEventArgs<PlayableItem>() { Item = this });
            }
        }
        #endregion

        internal void OnPlaybackFinished(BasePlaybackController controller, PlaybackStateEventArgs args)
        {
            // If there's still a valid position, fire progress one last time
            if (args.Position > 0)
            {
                OnProgress(controller, args);
            }

            PlaybackStoppedByUser = args.StoppedByUser;
            MarkWatchedIfNeeded();

            PlayState = PlayableItemPlayState.Stopped;
        }

        private Guid _Id = Guid.NewGuid();
        /// <summary>
        /// A new random Guid is generated for every PlayableItem. 
        /// Since there could be multiple PlayableItems queued up, having some sort of Id 
        /// is the most accurate way to know which one is playing at a given time.
        /// </summary>
        public Guid Id { get { return _Id; } }

        private IEnumerable<Media> _MediaItems = new List<Media>();
        /// <summary>
        /// If playback is based on Media items, this will hold the list of them
        /// </summary>
        public IEnumerable<Media> MediaItems { get { return _MediaItems; } internal set { _MediaItems = value; } }

        /// <summary>
        /// If Playback is Folder Based this will hold a reference to the Folder object
        /// </summary>
        public Folder Folder { get; internal set; }

        private IEnumerable<string> _Files = new List<string>();
        /// <summary>
        /// If the playback is based purely on file paths, this will hold the list of them
        /// </summary>
        public IEnumerable<string> Files { get { return _Files; } internal set { _Files = value; } }

        /// <summary>
        /// Describes how the item was played by the user
        /// </summary>
        public PlayMethod PlayMethod { get; internal set; }

        /// <summary>
        /// Determines if the item should be queued, as opposed to played immediately
        /// </summary>
        public bool QueueItem { get; set; }

        /// <summary>
        /// If true, the PlayableItems will be shuffled before playback
        /// </summary>
        public bool Shuffle { get; set; }

        /// <summary>
        /// If true, Playback will be resumed from the last known position
        /// </summary>
        public bool Resume { get; set; }

        /// <summary>
        /// Holds the time that playback was started
        /// </summary>
        public DateTime PlaybackStartTime { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating if a mounted ISO should be unmounted after playback
        /// </summary>
        public bool UnmountISOAfterPlayback { get; set; }

        /// <summary>
        /// If we're not able to track playstate at all, we'll at least mark watched once playback stops
        /// </summary>
        private bool HasUpdatedPlayState { get; set; }

        private bool _RaiseGlobalPlaybackEvents = true;
        /// <summary>
        /// Determines if global pre/post play events should fire
        /// </summary>
        public bool RaiseGlobalPlaybackEvents { get { return _RaiseGlobalPlaybackEvents; } set { _RaiseGlobalPlaybackEvents = value; } }

        private bool _GoFullScreen = true;
        /// <summary>
        /// Determines whether or not the PlaybackController should go full screen upon beginning playback
        /// </summary>
        public bool GoFullScreen { get { return _GoFullScreen; } set { _GoFullScreen = value; } }

        private BasePlaybackController _PlaybackController = null;
        /// <summary>
        /// Gets the PlaybackController for this Playable
        /// </summary>
        public BasePlaybackController PlaybackController
        {
            get
            {
                if (_PlaybackController == null)
                {
                    _PlaybackController = GetPlaybackController();

                    // If it's still null, create it
                    if (_PlaybackController == null)
                    {
                        _PlaybackController = Activator.CreateInstance(PlaybackControllerType) as BasePlaybackController;

                        Logger.ReportVerbose("Creating a new instance of " + PlaybackControllerType.Name);
                        Kernel.Instance.PlaybackControllers.Add(_PlaybackController);
                    }
                }

                return _PlaybackController;
            }
        }

        private PlayableItemPlayState _PlayState = PlayableItemPlayState.Created;
        /// <summary>
        /// Gets or sets the current playback stage
        /// </summary>
        public PlayableItemPlayState PlayState
        {
            get
            {
                return _PlayState;
            }
            internal set
            {
                var changed = _PlayState != value;

                _PlayState = value;

                if (changed)
                {
                    OnPlayStateChanged();
                }
            }
        }

        /// <summary>
        /// Gets the Media Items that have actually been played up to this point
        /// </summary>
        public IEnumerable<Media> PlayedMediaItems
        {
            get
            {
                return MediaItems.Where(p => p.PlaybackStatus.LastPlayed.Equals(PlaybackStartTime));
            }
        }

        /// <summary>
        /// Once playback is complete this value will indicate if the player was allowed to finish or if it was explicitly stopped by the user
        /// </summary>
        public bool PlaybackStoppedByUser { get; private set; }

        /// <summary>
        /// Helper to determine if this Playable has MediaItems or if it is based on file paths
        /// </summary>
        public bool HasMediaItems { get { return MediaItems.Any(); } }

        /// <summary>
        /// Gets the index of the current media item being played.
        /// </summary>
        public int CurrentMediaIndex { get; private set; }

        /// <summary>
        /// Gets the current Media being played
        /// </summary>
        public Media CurrentMedia
        {
            get
            {
                return MediaItems.ElementAtOrDefault(CurrentMediaIndex);
            }
        }

        /// <summary>
        /// Gets or sets the overall playlist position of the current playing file.
        /// That is, with respect to all files from all Media items
        /// </summary>
        public int CurrentFileIndex { get; private set; }

        /// <summary>
        /// Gets the current file being played
        /// </summary>
        public string CurrentFile
        {
            get
            {
                return Files.ElementAtOrDefault(CurrentFileIndex);
            }
        }

        /// <summary>
        /// Gets the name of this item that can be used for display or logging purposes
        /// </summary>
        public string DisplayName
        {
            get
            {
                // If playback is folder-based, use the name of the folder
                if (Folder != null)
                {
                    return Folder.Name;
                }

                // Otherwise if we're playing Media items, use the name of the current one
                if (HasMediaItems)
                {
                    return CurrentMedia.Name;
                }

                // Playback is file-based so use the current file
                return Files.Any() ? CurrentFile : string.Empty;
            }
        }

        /// <summary>
        /// Gets the primary BaseItem object that was playback was initiated on
        /// If playback is folder-based, this will return the Folder
        /// Otherwise it will return the first Media object (or null if playback is path-based).
        /// </summary>
        private BaseItem PrimaryBaseItem
        {
            get
            {
                // If playback is folder-based, return the Folder
                if (Folder != null)
                {
                    return Folder;
                }

                // Return the first item
                return MediaItems.FirstOrDefault();
            }
        }

        /// <summary>
        /// Determines whether or not this item is restricted by parental controls
        /// </summary>
        public bool ParentalAllowed
        {
            get
            {
                BaseItem item = PrimaryBaseItem;

                return item == null ? true : item.ParentalAllowed;
            }
        }

        /// <summary>
        /// Gets the parental control pin that would need to be entered in order to play the item
        /// </summary>
        public string ParentalControlPin
        {
            get
            {
                BaseItem item = PrimaryBaseItem;

                return item == null ? string.Empty : item.CustomPIN;
            }
        }

        #region AddMedia
        public void AddMedia(string file)
        {
            AddMedia(new string[] { file });
        }

        public void AddMedia(IEnumerable<string> filesToAdd)
        {
            List<string> newList = Files.ToList();
            newList.AddRange(filesToAdd);

            Files = newList;
        }

        public void AddMedia(Media media)
        {
            AddMedia(new Media[] { media });
        }
        public void AddMedia(IEnumerable<Media> itemsToAdd)
        {
            List<Media> newList = MediaItems.ToList();
            newList.AddRange(itemsToAdd);

            MediaItems = newList;
        }
        #endregion

        #region CanPlay
        /// <summary>
        /// Subclasses will have to override this if they want to be able to play a list of files
        /// </summary>
        public virtual bool CanPlay(IEnumerable<string> files)
        {
            if (files.Count() == 1)
            {
                return CanPlay(files.First());
            }

            return false;
        }

        /// <summary>
        /// Subclasses will have to override this if they want to be able to play a list of Media objects
        /// </summary>
        public virtual bool CanPlay(IEnumerable<Media> mediaList)
        {
            if (mediaList.Count() == 1)
            {
                return CanPlay(mediaList.First());
            }

            return false;
        }

        /// <summary>
        /// Subclasses will have to override this if they want to be able to play a Media object
        /// </summary>
        public virtual bool CanPlay(Media media)
        {
            return false;
        }

        /// <summary>
        /// Subclasses will have to override this if they want to be able to play based on a path
        /// </summary>
        public virtual bool CanPlay(string path)
        {
            return false;
        }
        #endregion

        /// <summary>
        /// Determines if this PlayableItem can play a given Media object within a playlist
        /// </summary>
        protected virtual bool IsPlaylistCapable(Media media)
        {
            Video video = media as Video;
            if (video != null)
            {
                return !video.ContainsRippedMedia;
            }
            return true;
        }

        internal void Play()
        {
            Prepare();

            if (!HasMediaItems && !Files.Any())
            {
                Microsoft.MediaCenter.MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                ev.Dialog(Application.CurrentInstance.StringData("NoContentDial"), Application.CurrentInstance.StringData("Playstr"), Microsoft.MediaCenter.DialogButtons.Ok, 500, true);
                return;
            }

            // Run all pre-play processes
            if (!RunPrePlayProcesses())
            {
                // Abort playback if one of them returns false
                return;
            }

            Logger.ReportInfo(GetType().Name + " about to play " + DisplayName);

            PlaybackController.Play(this);
        }

        /// <summary>
        /// Performs any necessary housekeeping before playback
        /// </summary>
        protected virtual void Prepare()
        {
            // Filter for IsPlaylistCapable
            if (MediaItems.Count() > 1)
            {
                // First filter out items that can't be queued in a playlist
                _MediaItems = MediaItems.Where(m => IsPlaylistCapable(m)).ToList();
            }

            if (Shuffle)
            {
                ShufflePlayableItems();
            }

            PlaybackStartTime = DateTime.Now;
        }

        /// <summary>
        /// Runs preplay processes and aborts playback if one of them returns false
        /// </summary>
        private bool RunPrePlayProcesses()
        {
            PlayState = Playables.PlayableItemPlayState.Preplay;

            if (!RaiseGlobalPlaybackEvents)
            {
                return true;
            }

            return Application.CurrentInstance.RunPrePlayProcesses(PrimaryBaseItem, this);
        }

        /// <summary>
        /// Gets the Type of PlaybackController that this Playable uses
        /// </summary>
        protected abstract Type PlaybackControllerType
        {
            get;
        }

        /// <summary>
        /// Gets the PlaybackController for this PlayableItem
        /// </summary>
        protected virtual BasePlaybackController GetPlaybackController()
        {
            return Kernel.Instance.PlaybackControllers.FirstOrDefault(p => p.GetType() == PlaybackControllerType);
        }

        /// <summary>
        /// Shuffles the list of playable items
        /// </summary>
        private void ShufflePlayableItems()
        {
            Random rnd = new Random();

            // If playback is based on Media objects
            if (HasMediaItems)
            {
                MediaItems = MediaItems.OrderBy(i => rnd.Next()).ToList();
            }
            else
            {
                // Otherwise if playback is based on a list of files
                Files = Files.OrderBy(i => rnd.Next()).ToList();
            }
        }

        /// <summary>
        /// Goes through each Media object within PlayableMediaItems and updates Playstate for each individually
        /// </summary>
        private void SaveProgressIntoPlaystates(BasePlaybackController controller, PlaybackStateEventArgs args)
        {
            string currentFile = CurrentFile;

            for (int i = 0; i < MediaItems.Count(); i++)
            {
                Media media = MediaItems.ElementAt(i);

                bool isCurrentMedia = i == CurrentMediaIndex;

                long currentPositionTicks = 0;
                int currentPlaylistPosition = 0;

                if (isCurrentMedia)
                {
                    // If this is where playback is, update position and playlist
                    currentPlaylistPosition = controller.GetPlayableFiles(media).ToList().IndexOf(currentFile);
                    currentPositionTicks = args.Position;
                }

                Application.CurrentInstance.UpdatePlayState(media, media.PlaybackStatus, currentPlaylistPosition, currentPositionTicks, args.DurationFromPlayer, PlaybackStartTime);

                if (isCurrentMedia)
                {
                    break;
                }
            }

            HasUpdatedPlayState = true;
        }

        /// <summary>
        /// Marks all Media objects as watched, if progress has not been saved at all yet
        /// </summary>
        private void MarkWatchedIfNeeded()
        {
            if (!HasUpdatedPlayState)
            {
                foreach (Media media in MediaItems)
                {
                    Logger.ReportVerbose("Marking watched: " + media.Name);
                    Application.CurrentInstance.UpdatePlayState(media, media.PlaybackStatus, 0, 0, null, PlaybackStartTime);
                }
            }
        }

        /// <summary>
        /// Stops playback on the current PlaybackController
        /// </summary>
        public void StopPlayback()
        {
            PlaybackController.Stop();
        }

        /// <summary>
        /// Waits for the PlayableItem to reach a given state and then returns
        /// </summary>
        public void WaitForPlayState(PlayableItemPlayState state)
        {
            while (PlayState != state)
            {
                Thread.Sleep(1000);
            }
        }
    }

    /// <summary>
    /// Represents all of the stages of the lifecycle of a PlayableItem
    /// </summary>
    public enum PlayableItemPlayState
    {
        /// <summary>
        /// The PlayableItem has been created, but has not been passed into Application.Play
        /// </summary>
        Created = 0,

        /// <summary>
        /// The PlayableItem is currently running preplay processes and events
        /// </summary>
        Preplay = 1,

        /// <summary>
        /// Tthe PlayableItem has been sent to the player, but is not currently playing.
        /// </summary>
        Queued = 2,

        /// <summary>
        /// The PlayableItem is playing right now
        /// </summary>
        Playing = 3,

        /// <summary>
        /// The PlayableItem has finished playback and is performing post-play actions
        /// </summary>
        Stopped = 4,

        /// <summary>
        /// The PlayableItem has completed all post-play processes and events
        /// </summary>
        PostPlayActionsComplete = 5
    }

}
