using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Events;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Encapsulates play back for a single Media object, which could have multiple playable file.
    /// Alternatively it can play based on a path or paths (playlist), albeit without any Playstate support.
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
            CurrentFilePlaylistPosition = args.FilePlaylistPosition;
            CurrentMediaId = args.CurrentMediaId;
            CurrentPositionTicks = args.Position;

            SaveProgressIntoPlaystates(controller);

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
                MarkWatchedIfNeeded();
            }

            // Postplay housekeeping
            else if (PlayState == PlayableItemPlayState.PostPlayActionsComplete && UnmountISOAfterPlayback)
            {
                Application.CurrentInstance.UnmountIso();
            }

            if (_PlayStateChanged != null)
            {
                _PlayStateChanged(this, new GenericEventArgs<PlayableItem>() { Item = this });
            }
        }
        #endregion

        private Guid _Id = Guid.NewGuid();
        /// <summary>
        /// A new random Guid is generated for every PlayableItem. 
        /// Since there could be multiple PlayableItems queued up, having some sort of Id 
        /// is the only to know which one is playing at a given time.
        /// </summary>
        public Guid Id { get { return _Id; } }

        private List<Media> _MediaItems = new List<Media>();
        /// <summary>
        /// If playback is based on Media items, this will hold the list of them
        /// </summary>
        public List<Media> MediaItems { get { return _MediaItems; } }

        /// <summary>
        /// If Playback is Folder Based this will hold a reference to the Folder object
        /// </summary>
        public Folder Folder { get; set; }

        private List<string> _Files = new List<string>();
        /// <summary>
        /// If the playback is based purely on file paths, this will hold the list of them
        /// </summary>
        public List<string> Files { get { return _Files; } }

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
        internal bool HasUpdatedPlayState { get; set; }

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
            set
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
        public bool PlaybackStoppedByUser { get; internal set; }

        /// <summary>
        /// Helper to determine if this Playable has MediaItems or if it is based on file paths
        /// </summary>
        public bool HasMediaItems { get { return MediaItems.Any(); } }

        /// <summary>
        /// Gets or sets the Id of the current Media being played
        /// </summary>
        public Guid CurrentMediaId { get; internal set; }

        /// <summary>
        /// Gets the current Media being played
        /// </summary>
        public Media CurrentMedia
        {
            get
            {
                if (MediaItems.Count == 1)
                {
                    return MediaItems.First();
                }

                return MediaItems.FirstOrDefault(m => m.Id == CurrentMediaId);
            }
        }

        /// <summary>
        /// Gets or sets the overall playlist position of the current playing file.
        /// That is, with respect to all files from all Media items
        /// </summary>
        public int CurrentFilePlaylistPosition { get; internal set; }

        /// <summary>
        /// Gets the current file being played
        /// </summary>
        public string CurrentFile
        {
            get
            {
                if (Files.Count == 1)
                {
                    return Files.First();
                }

                return Files.ElementAt(CurrentFilePlaylistPosition);
            }
        }

        /// <summary>
        /// Gets or sets the position of the player, in Ticks
        /// </summary>
        public long CurrentPositionTicks { get; internal set; }

        /// <summary>
        /// Helper to get the position to resume at
        /// </summary>
        public long ResumePositionTicks
        {
            get
            {
                return HasMediaItems ? MediaItems.First().PlaybackStatus.PositionTicks : 0;
            }
        }

        /// <summary>
        /// Helper to get the playlist position to resume at
        /// </summary>
        public int ResumePlaylistPosition
        {
            get
            {
                return HasMediaItems ? MediaItems.First().PlaybackStatus.PlaylistPosition : 0;
            }
        }

        /// <summary>
        /// Gets the name of this item that can be used for display or logging purposes
        /// </summary>
        public string Name
        {
            get
            {
                BaseItem item = PrimaryBaseItem;

                if (item == null)
                {
                    return Files.Any() ? Files.First() : string.Empty;
                }

                return item.Name;
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

        public void AddMedia(IEnumerable<string> files)
        {
            Files.AddRange(files);
        }

        public void AddMedia(Media media)
        {
            AddMedia(new Media[] { media });
        }
        public void AddMedia(IEnumerable<Media> mediaItems)
        {
            List<Media> playableItems = MediaItems as List<Media>;
            playableItems.AddRange(mediaItems);
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
            if (!HasMediaItems && !Files.Any())
            {
                Microsoft.MediaCenter.MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                ev.Dialog(Application.CurrentInstance.StringData("NoContentDial"), Application.CurrentInstance.StringData("Playstr"), Microsoft.MediaCenter.DialogButtons.Ok, 500, true);
                return;
            }

            Prepare();

            // Run all pre-play processes
            if (!RunPrePlayProcesses())
            {
                // Abort playback if one of them returns false
                return;
            }

            AddPrePlaybackLogEntry();

            PlaybackController.Play(this);

            // Set the current playback stage
            PlayState = QueueItem ? PlayableItemPlayState.Queued : PlayableItemPlayState.Playing;
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
                _MediaItems = MediaItems.Where(m => IsPlaylistCapable(m)) as List<Media>;
            }

            if (Shuffle)
            {
                ShufflePlayableItems();
            }

            PlaybackStartTime = DateTime.Now;
        }

        private void AddPrePlaybackLogEntry()
        {
            if (Folder != null)
            {
                Logger.ReportInfo("About to play Folder: " + Folder.Name);
            }
            else if (HasMediaItems)
            {
                Logger.ReportInfo(GetType().Name + " About to play : " + string.Join(",", MediaItems.Select(p => p.Name).ToArray()));
            }
            else
            {
                Logger.ReportInfo(GetType().Name + " About to play : " + string.Join(",", Files.ToArray()));
            }
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
            return Kernel.Instance.PlaybackControllers.FirstOrDefault(p => p.GetType() == PlaybackControllerType) as BasePlaybackController;
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
                _MediaItems = MediaItems.OrderBy(i => rnd.Next()) as List<Media>;
            }
            else
            {
                // Otherwise if playback is based on a list of files
                _Files = Files.OrderBy(i => rnd.Next()) as List<string>;
            }
        }

        /// <summary>
        /// Goes through each Media object within PlayableMediaItems and updates Playstate for each individually
        /// </summary>
        private void SaveProgressIntoPlaystates(BasePlaybackController controller)
        {
            string currentFile = CurrentFile;

            foreach (Media media in MediaItems)
            {
                bool isCurrentMedia = media.Id == CurrentMediaId;

                long currentPositionTicks = 0;
                int currentPlaylistPosition = 0;

                if (isCurrentMedia)
                {
                    // If this is where playback is, update position and playlist
                    currentPlaylistPosition = controller.GetPlayableFiles(media).ToList().IndexOf(currentFile);
                    currentPositionTicks = CurrentPositionTicks;
                }

                Application.CurrentInstance.UpdatePlayState(media, media.PlaybackStatus, currentPlaylistPosition, currentPositionTicks, null, PlaybackStartTime);

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

    }

    /// <summary>
    /// Represents all of the stages of the lifecycle of a PlayableItem
    /// </summary>
    public enum PlayableItemPlayState
    {
        /// <summary>
        /// The PlayableItem has been created yet, but has not been passed into Application.Play
        /// </summary>
        Created = 0,

        /// <summary>
        /// The PlayableItem is currently running preplay processes and events
        /// </summary>
        Preplay = 1,

        /// <summary>
        /// Tthe PlayableItem has been sent to the player, but is not currently playing.
        /// For some players we will not be able to determine what is playing at any given time
        /// In those situations, the item will remain Queued until it finishes
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
