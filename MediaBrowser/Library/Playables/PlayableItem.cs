using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library
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
        protected Guid PlayableItemId = Guid.NewGuid();

        /// <summary>
        /// This holds the list of Media objects from which PlayableItems will be created.
        /// </summary>
        protected List<Media> PlayableMediaItems = new List<Media>();

        /// <summary>
        /// If Playback is Folder Based this will hold a reference to the Folder object
        /// </summary>
        public Folder Folder { get; set; }

        /// <summary>
        /// This holds the list of files that will be sent to the player.
        /// </summary>
        private List<string> PlayableFiles = new List<string>();

        public bool PlayIntros { get; set; }

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
        protected DateTime PlaybackStartTime { get; private set; }

        /// <summary>
        /// If we're not able to track playstate at all, we'll at least mark watched once playback stops
        /// </summary>
        protected bool HasUpdatedPlayState { get; set; }

        private bool _GoFullScreen = true;
        public bool GoFullScreen { get { return _GoFullScreen; } set { _GoFullScreen = value; } }

        /// <summary>
        /// Gets all Media objects that will be played by this item
        /// </summary>
        public IEnumerable<Media> GetPlayableMediaItems()
        {
            return PlayableMediaItems.Select(m => m);
        }

        private IPlaybackController _PlaybackController = null;
        public IPlaybackController PlaybackController
        {
            get
            {
                if (_PlaybackController == null)
                {
                    _PlaybackController = GetPlaybackController();

                    // If it's still null, create it
                    if (_PlaybackController == null)
                    {
                        _PlaybackController = Activator.CreateInstance(PlaybackControllerType) as IPlaybackController;

                        Logger.ReportVerbose("Creating a new instance of " + PlaybackControllerType.Name);
                        Kernel.Instance.PlaybackControllers.Add(_PlaybackController);
                    }
                }

                return _PlaybackController;
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
                    return PlayableFiles.Any() ? PlayableFiles.First() : string.Empty;
                }

                return item.Name;
            }
        }

        /// <summary>
        /// Gets the primary BaseItem object that was passed into AddMedia
        /// If playback is folder-based, this will return the Folder
        /// Otherwise it will return the Media object (or null if playback is path-based).
        /// </summary>
        public BaseItem PrimaryBaseItem
        {
            get
            {
                // If playback is folder-based, return the Folder
                if (Folder != null)
                {
                    return Folder;
                }

                // Return the first item
                return PlayableMediaItems.FirstOrDefault();
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
            PlayableFiles.Add(file);
        }

        public void AddMedia(IEnumerable<string> files)
        {
            PlayableFiles.AddRange(files);
        }

        public void AddMedia(Media media)
        {
            PlayableMediaItems.Add(media);
        }
        public void AddMedia(IEnumerable<Media> mediaItems)
        {
            AddMedia(mediaItems, true);
        }

        private void AddMedia(IEnumerable<Media> mediaItems, bool filterPlaylistCapable)
        {
            if (filterPlaylistCapable && mediaItems.Count() > 1)
            {
                // First filter out items that can't be queued in a playlist
                mediaItems = mediaItems.Where(m => IsPlaylistCapable(m));
            }

            PlayableMediaItems.AddRange(mediaItems);
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

        internal void Play()
        {
            bool hasMediaItems = PlayableMediaItems.Any();

            if (!hasMediaItems && !PlayableFiles.Any())
            {
                Microsoft.MediaCenter.MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                ev.Dialog(Application.CurrentInstance.StringData("NoContentDial"), Application.CurrentInstance.StringData("Playstr"), Microsoft.MediaCenter.DialogButtons.Ok, 500, true);
                return;
            }

            this.Prepare();

            if (Folder != null)
            {
                Logger.ReportInfo("About to play Folder: " + Folder.Name);
            }
            else if (hasMediaItems)
            {
                Logger.ReportInfo(GetType().Name + " About to play : " + string.Join(",", PlayableMediaItems.Select(p => p.Name).ToArray()));
            }
            else
            {
                Logger.ReportInfo(GetType().Name + " About to play : " + string.Join(",", PlayableFiles.ToArray()));
            }

            if (hasMediaItems)
            {
                PlayableFiles = PlayableMediaItems.Select(m => GetPlayableFiles(m)).SelectMany(i => i).ToList();
            }

            SendFilesToPlayer(GetPlaybackArguments());
        }

        protected virtual void Prepare()
        {
            if (Shuffle)
            {
                ShufflePlayableItems();
            }

            PlaybackStartTime = DateTime.Now;
        }

        protected abstract Type PlaybackControllerType
        {
            get;
        }

        protected virtual IPlaybackController GetPlaybackController()
        {
            return Kernel.Instance.PlaybackControllers.FirstOrDefault(p => p.GetType() == PlaybackControllerType);
        }

        private void SendFilesToPlayer(PlaybackArguments args)
        {
            if (QueueItem)
            {
                PlaybackController.QueueMedia(args);
            }
            else
            {
                PlaybackController.PlayMedia(args);
            }

            // Optimization for items that don't have PlayState
            if (PlayableMediaItems.Count() > 0)
            {
                PlaybackController.Progress += OnProgress;
            }

            PlaybackController.PlaybackFinished += OnPlaybackFinished;
        }

        /// <summary>
        /// Generates an arguments object to send to the PlaybackController
        /// </summary>
        private PlaybackArguments GetPlaybackArguments()
        {
            PlaybackArguments info = new PlaybackArguments();

            info.Files = PlayableFiles;

            Media media = PlayableMediaItems.FirstOrDefault();

            if (media != null && media.PlaybackStatus != null)
            {
                info.PositionTicks = media.PlaybackStatus.PositionTicks;
                info.PlaylistPosition = media.PlaybackStatus.PlaylistPosition;
            }

            info.GoFullScreen = GoFullScreen;
            info.Resume = Resume;
            info.PlayableItemId = PlayableItemId;

            return info;
        }

        /// <summary>
        /// Fires whenever the PlaybackController reports that playback has changed position
        /// Subclasses which don't use the PlaybackController can also call this manually
        /// </summary>
        protected void OnProgress(object sender, PlaybackStateEventArgs e)
        {
            // Something else is currently playing
            if (!IsPlaybackEventOnCurrentInstance(e))
            {
                return;
            }

            if (PlayableMediaItems.Count == 1)
            {
                Media media = PlayableMediaItems.First();

                Application.CurrentInstance.UpdatePlayState(media, media.PlaybackStatus, e.PlaylistPosition, e.Position, e.DurationFromPlayer, PlaybackStartTime);
            }

            else if (PlayableMediaItems.Count > 1)
            {
                UpdateProgressForMultipleMediaItems(e);
            }

            HasUpdatedPlayState = true;
        }

        /// <summary>
        /// Fires whenever the PlaybackController reports that playback has stopped
        /// Subclasses which don't use the PlaybackController can also call this manually
        /// </summary>
        protected virtual void OnPlaybackFinished(object sender, PlaybackStateEventArgs e)
        {
            // Clean up event handlers
            PlaybackController.Progress -= OnProgress;
            PlaybackController.PlaybackFinished -= OnPlaybackFinished;

            // If it has a position then update it one last time
            if (e.Position > 0)
            {
                OnProgress(sender, e);
            }

            if (IsPlaybackEventOnCurrentInstance(e))
            {
                // If we haven't been able to update position, at least mark it watched
                if (!HasUpdatedPlayState)
                {
                    MarkWatched();
                }
            }

            Application.CurrentInstance.RunPostPlayProcesses(PlaybackController, PlayableMediaItems.Where(p => p.PlaybackStatus.LastPlayed.Equals(PlaybackStartTime)));

            UpdateResumeStatusInUI();
            Logger.ReportVerbose("All post-playback actions have completed.");
        }

        /// <summary>
        /// Goes through each Media object within PlayableMediaItems and updates Playstate for each individually
        /// </summary>
        private void UpdateProgressForMultipleMediaItems(PlaybackStateEventArgs state)
        {
            string currentFile = PlayableFiles.ElementAt(state.PlaylistPosition);

            int foundIndex = -1;

            // First find which media item we left off at
            for (int i = 0; i < PlayableMediaItems.Count; i++)
            {
                if (GetPlayableFiles(PlayableMediaItems.ElementAt(i)).Contains(currentFile))
                {
                    foundIndex = i;
                }
            }

            // Go through each media item up until the current one and update playstate
            for (int i = 0; i <= foundIndex; i++)
            {
                Media media = PlayableMediaItems.ElementAt(i);

                // Perhaps not a resumable item
                if (media.PlaybackStatus == null)
                {
                    continue;
                }

                long currentPositionTicks = 0;
                int currentPlaylistPosition = 0;

                if (foundIndex == i)
                {
                    // If this is where playback is, update position and playlist
                    currentPlaylistPosition = GetPlayableFiles(media).ToList().IndexOf(currentFile);
                    currentPositionTicks = state.Position;
                }

                Application.CurrentInstance.UpdatePlayState(media, media.PlaybackStatus, currentPlaylistPosition, currentPositionTicks, null, PlaybackStartTime);
            }

        }

        /// <summary>
        /// Updates the Resume status in the UI, if needed
        /// </summary>
        private void UpdateResumeStatusInUI()
        {
            Item item = Application.CurrentInstance.CurrentItem;
            Guid currentMediaId = item.BaseItem.Id;

            foreach (Media media in PlayableMediaItems)
            {
                if (media.Id == currentMediaId)
                {
                    item.UpdateResume();
                    break;
                }
            }
        }

        /// <summary>
        /// Determines if the event that came from the PlaybackController was caused by this instance
        /// </summary>
        private bool IsPlaybackEventOnCurrentInstance(PlaybackStateEventArgs e)
        {
            return Guid.Equals(e.PlayableItemId, PlayableItemId);
        }

        /// <summary>
        /// Marks all Media objects as watched
        /// </summary>
        private void MarkWatched()
        {
            foreach (Media media in PlayableMediaItems)
            {
                Logger.ReportVerbose("Marking watched: " + media.Name);
                Application.CurrentInstance.UpdatePlayState(media, media.PlaybackStatus, 0, 0, null, PlaybackStartTime);
            }
        }

        /// <summary>
        /// Shuffles the list of playable items
        /// </summary>
        private void ShufflePlayableItems()
        {
            Random rnd = new Random();

            // If playback is based on Media objects
            if (PlayableMediaItems.Any())
            {
                PlayableMediaItems = PlayableMediaItems.OrderBy(i => rnd.Next()).ToList();
            }
            else
            {
                // Otherwise if playback is based on a list of files
                PlayableFiles = PlayableFiles.OrderBy(i => rnd.Next()).ToList();
            }
        }
    }

}
