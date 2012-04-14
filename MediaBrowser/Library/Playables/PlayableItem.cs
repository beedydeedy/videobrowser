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

        private List<Media> _PlayableMediaItems = new List<Media>();
        /// <summary>
        /// This holds the list of Media objects from which PlayableItems will be created.
        /// </summary>
        protected List<Media> PlayableMediaItems { get { return _PlayableMediaItems; } }

        /// <summary>
        /// If Playback is Folder Based this will hold a reference to the Folder object
        /// </summary>
        public Folder Folder { get; private set; }

        /// <summary>
        /// This holds the list of files that will be sent to the player.
        /// </summary>
        protected List<string> PlayableFiles = new List<string>();

        public IPlaybackController PlaybackController { get; set; }

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

        /// <summary>
        /// Gets all Media objects that will be played by this item
        /// </summary>
        public IEnumerable<Media> GetPlayableMediaItems()
        {
            return PlayableMediaItems.Select(m => m);
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
                    return PlayableFiles.Count > 0 ? PlayableFiles.First() : string.Empty;
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
            AddMedia(media.Files);
            _PlayableMediaItems = new Media[] { media }.ToList();
        }
        public virtual void AddMedia(IEnumerable<Media> mediaItems)
        {
            if (mediaItems.Count() > 1)
            {
                // First filter out items that can't be queued in a playlist
                mediaItems = mediaItems.Where(m => m.IsPlaylistCapable());
            }

            _PlayableMediaItems = mediaItems.ToList();
            AddMedia(mediaItems.Select(v2 => v2.Files).SelectMany(i => i));
        }
        public virtual void AddMedia(Folder folder)
        {
            AddMedia(folder.RecursiveMedia);
            Folder = folder;
        }
        #endregion

        #region CanPlay
        /// <summary>
        /// Subclasses will have to override this if they want to be able to play a list of files
        /// </summary>
        public virtual bool CanPlay(IEnumerable<string> files)
        {
            return false;
        }

        /// <summary>
        /// Subclasses will have to override this if they want to be able to play a list of Media objects
        /// </summary>
        public virtual bool CanPlay(IEnumerable<Media> mediaList)
        {
            return false;
        }

        /// <summary>
        /// Subclasses will have to override this if they want to be able to play a Folder
        /// </summary>
        public virtual bool CanPlay(Folder folder)
        {
            return CanPlay(folder.RecursiveMedia);
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

        internal void Play()
        {
            this.Prepare();

            if (PlayableFiles.Count() == 0)
            {
                Microsoft.MediaCenter.MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                ev.Dialog(Application.CurrentInstance.StringData("NoContentDial"), Application.CurrentInstance.StringData("Playstr"), Microsoft.MediaCenter.DialogButtons.Ok, 500, true);
                return;
            }

            Logger.ReportInfo(GetType().Name +  " About to play : " + string.Join(",", PlayableFiles.ToArray()));

            Media media = PlayableMediaItems.FirstOrDefault();
            PlaybackStatus playstate = null;

            if (media != null)
            {
                playstate = media.PlaybackStatus;
            }

            SendFilesToPlayer(GetPlaybackArguments(PlayableFiles, playstate));
        }

        protected virtual void Prepare()
        {
            if (Shuffle)
            {
                ShufflePlayableItems();
            }

            PlaybackStartTime = DateTime.Now;
        }

        protected virtual void SendFilesToPlayer(PlaybackArguments args)
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
                PlaybackController.PlaybackFinished += OnPlaybackFinished;
            }
        }

        /// <summary>
        /// Generates an arguments object to send to the PlaybackController
        /// </summary>
        private PlaybackArguments GetPlaybackArguments(IEnumerable<string> files, PlaybackStatus playstate)
        {
            PlaybackArguments info = new PlaybackArguments();

            info.Files = files;

            if (playstate != null)
            {
                info.PositionTicks = playstate.PositionTicks;
                info.PlaylistPosition = playstate.PlaylistPosition;
            }

            info.GoFullScreen = true;
            info.Resume = Resume;
            info.PlayableItemId = PlayableItemId;

            return info;
        }

        /// <summary>
        /// Fires whenever the PlaybackController reports that playback has changed position
        /// Subclasses which don't use the PlaybackController can also call this manually
        /// </summary>
        protected virtual void OnProgress(object sender, PlaybackStateEventArgs e)
        {
            // Something else is currently playing
            if (!IsPlaybackEventOnCurrentInstance(e))
            {
                return;
            }

            Media media = PlayableMediaItems.FirstOrDefault();

            if (media != null)
            {
                Application.CurrentInstance.UpdatePlayState(media, media.PlaybackStatus, e.PlaylistPosition, e.Position, e.DurationFromPlayer, PlaybackStartTime);

                HasUpdatedPlayState = true;
            }
        }

        /// <summary>
        /// Fires whenever the PlaybackController reports that playback has stopped
        /// Subclasses which don't use the PlaybackController can also call this manually
        /// </summary>
        protected virtual void OnPlaybackFinished(object sender, PlaybackStateEventArgs e)
        {
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

            // Clean up event handlers
            PlaybackController.Progress -= OnProgress;
            PlaybackController.PlaybackFinished -= OnPlaybackFinished;

            UpdateResumeStatusInUI();
        }

        private void UpdateResumeStatusInUI()
        {
            foreach (Media media in PlayableMediaItems)
            {
                if (media.Id == Application.CurrentInstance.CurrentItem.BaseItem.Id)
                {
                    Application.CurrentInstance.CurrentItem.UpdateResume();
                    break;
                }
            }
        }

        /// <summary>
        /// Determines if the event that came from the PlaybackController was caused by this instance
        /// </summary>
        protected bool IsPlaybackEventOnCurrentInstance(PlaybackStateEventArgs e)
        {
            return Guid.Equals(e.PlayableItemId, PlayableItemId);
        }

        private void MarkWatched()
        {
            foreach (Media media in PlayableMediaItems)
            {
                Logger.ReportVerbose("Marking watched: " + media.Name);
                Application.CurrentInstance.UpdatePlayState(media, media.PlaybackStatus, 0, 0, null, PlaybackStartTime);
            }
        }

        private void ShufflePlayableItems()
        {
            Random rnd = new Random();

            if (PlayableMediaItems.Count > 0)
            {
                IEnumerable<Media> newList = PlayableMediaItems.OrderBy(i => rnd.Next());

                PlayableMediaItems.Clear();
                PlayableFiles.Clear();

                AddMedia(newList);
            }
            else
            {
                IEnumerable<string> newList = PlayableFiles.OrderBy(i => rnd.Next());

                PlayableFiles.Clear();

                AddMedia(newList);
            }
        }

        public bool CanBePlayedByController(IPlaybackController controller)
        {
            return controller.CanPlay(PlayableFiles);
        }
    }

}
