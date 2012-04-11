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
        /// The current Media object. Will be null if this playable was created to play a file path
        /// </summary>
        protected Media Media { get; private set; }

        /// <summary>
        /// This holds the list of files that will be sent to the player.
        /// </summary>
        protected List<string> PlayableFiles = new List<string>();

        public IPlaybackController PlaybackController { get; set; }

        public bool QueueItem { get; set; }

        public PlaybackStatus PlayState { get; set; }

        /// <summary>
        /// If true, the PlayableItems will be shuffled before playback
        /// </summary>
        public bool Shuffle { get; set; }

        /// <summary>
        /// Holds the time that playback was started
        /// </summary>
        protected DateTime PlaybackStartTime { get; private set; }

        /// <summary>
        /// If we're not able to track playstate at all, we'll at least mark watched once playback stops
        /// </summary>
        protected bool HasUpdatedPlayState { get; set; }

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
            Media = media;
            PlayState = media.PlaybackStatus;
        }
        public virtual void AddMedia(IEnumerable<Media> mediaItems)
        {
            AddMedia(mediaItems.Select(v2 => v2.Files).SelectMany(i => i));
        }
        public virtual void AddMedia(Folder folder)
        {
            AddMedia(folder.RecursiveMedia);
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

        public void Play(bool resume)
        {
            this.Prepare(resume);

            if (PlayableFiles.Count() == 0)
            {
                Microsoft.MediaCenter.MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                ev.Dialog(Application.CurrentInstance.StringData("NoContentDial"), Application.CurrentInstance.StringData("Playstr"), Microsoft.MediaCenter.DialogButtons.Ok, 500, true);
                return;
            }

            Logger.ReportInfo("About to play : " + string.Join(",", PlayableFiles.ToArray()));

            SendFilesToPlayer(GetPlaybackArguments(PlayableFiles, PlayState, resume));
        }

        protected virtual void Prepare(bool resume)
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
            if (PlayState != null)
            {
                PlaybackController.Progress += OnProgress;
                PlaybackController.PlaybackFinished += OnPlaybackFinished;
            }
        }

        /// <summary>
        /// Generates an arguments object to send to the PlaybackController
        /// </summary>
        private PlaybackArguments GetPlaybackArguments(IEnumerable<string> files, PlaybackStatus playstate, bool resume)
        {
            PlaybackArguments info = new PlaybackArguments();

            info.Files = files;

            if (playstate != null)
            {
                info.PositionTicks = playstate.PositionTicks;
                info.PlaylistPosition = playstate.PlaylistPosition;
            }

            info.GoFullScreen = true;
            info.Resume = resume;
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

            if (PlayState != null)
            {
                Application.CurrentInstance.UpdatePlayState(Media, PlayState, e.PlaylistPosition, e.Position, e.DurationFromPlayer, PlaybackStartTime);

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

        protected virtual void UpdateResumeStatusInUI()
        {
            if (Media != null && Media.Id == Application.CurrentInstance.CurrentItem.BaseItem.Id)
            {
                Application.CurrentInstance.CurrentItem.UpdateResume();
            }
        }

        /// <summary>
        /// Determines if the event that came from the PlaybackController was caused by this instance
        /// </summary>
        protected bool IsPlaybackEventOnCurrentInstance(PlaybackStateEventArgs e)
        {
            return Guid.Equals(e.PlayableItemId, PlayableItemId);
        }

        protected virtual void MarkWatched()
        {
            if (PlayState != null)
            {
                Logger.ReportVerbose("Marking watched");
                Application.CurrentInstance.UpdatePlayState(Media, PlayState, 0, 0, null, PlaybackStartTime);
            }
        }

        protected virtual void ShufflePlayableItems()
        {
            Random rnd = new Random();

            IEnumerable<string> newList = PlayableFiles.OrderBy(i => rnd.Next());

            PlayableFiles.Clear();

            AddMedia(newList);
        }

        public bool CanBePlayedByController(IPlaybackController controller)
        {
            return controller.CanPlay(PlayableFiles);
        }
    }

}
