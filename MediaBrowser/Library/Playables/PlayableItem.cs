using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Logging;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Encapsulates play back for a single Media object, which could have multiple playable file.
    /// Alternatively it can play based on a path or paths (playlist), albeit without any Playstate support.
    /// </summary>
    public abstract class PlayableItem
    {
        /// <summary>
        /// A new random Guid is generated for every PlayableItem. 
        /// Since there could be multiple PlayableItems queued up, having some sort of Id 
        /// is the only to know which one is playing at a given time.
        /// </summary>
        public Guid Id = Guid.NewGuid();

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
        public PlayMethod PlayMethod { get; set; }

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
        /// If we're not able to track playstate at all, we'll at least mark watched once playback stops
        /// </summary>
        internal bool HasUpdatedPlayState { get; set; }

        private bool _RaisePlaybackEvents = true;
        /// <summary>
        /// Determines if pre/post play events should fire
        /// </summary>
        public bool RaisePlaybackEvents { get { return _RaisePlaybackEvents; } set { _RaisePlaybackEvents = value; } }

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
        /// Helper to determine if this Playable has MediaItems or if it is based on file paths
        /// </summary>
        public bool HasMediaItems { get { return MediaItems.Any(); } }

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
        /// Gets the primary BaseItem object that was passed into AddMedia
        /// If playback is folder-based, this will return the Folder
        /// Otherwise it will return the Media object (or null if playback is path-based).
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

            if (QueueItem)
            {
                PlaybackController.QueueMedia(this);
            }
            else
            {
                PlaybackController.PlayMedia(this);
            }
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

            // If playback is path based, force this to false as there's no point
            if (!HasMediaItems)
            {
                RaisePlaybackEvents = false;
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
            if (!RaisePlaybackEvents)
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
    }

}
