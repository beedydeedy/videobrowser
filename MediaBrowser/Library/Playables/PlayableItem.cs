﻿using System;
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
        protected List<string> PlayableItems = new List<string>();

        public IPlaybackController PlaybackController { get; set; }

        public bool QueueItem { get; set; }

        public PlaybackStatus PlayState { get; set; }

        /// <summary>
        /// If true, the PlayableItems will be shuffled before playback
        /// </summary>
        public bool Shuffle { get; set; }

        /// <summary>
        /// PlayState will be saved many times during Progress, but we only want to increment play count once.
        /// </summary>
        private bool IncrementedPlayCount { get; set; }

        #region AddMedia
        public void AddMedia(string file)
        {
            PlayableItems.Add(file);
        }

        public void AddMedia(IEnumerable<string> files)
        {
            PlayableItems.AddRange(files);
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

            if (PlayableItems.Count() == 0)
            {
                Microsoft.MediaCenter.MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment;
                ev.Dialog(Application.CurrentInstance.StringData("NoContentDial"), Application.CurrentInstance.StringData("Playstr"), Microsoft.MediaCenter.DialogButtons.Ok, 500, true);
                return;
            }

            Logger.ReportInfo("About to play : " + string.Join(",", PlayableItems.ToArray()));

            SendFilesToPlayer(GetPlaybackArguments(PlayableItems, PlayState, resume));
        }

        protected virtual void Prepare(bool resume)
        {
            if (Shuffle)
            {
                ShufflePlayableItems();
            }
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
                PlaybackController.GoToFullScreen();
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

            info.Resume = resume;
            info.PlayableItemId = PlayableItemId;
            info.MetaDurationTicks = Media == null ? 0 : TimeSpan.FromMinutes(Media.RunTime).Ticks;

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

            if (PlayState != null)
            {
                long duration = e.DurationFromPlayer;

                // The player didn't report the duration, see if we have it in metadata
                if (duration == 0 && Media != null)
                {
                    duration = TimeSpan.FromMinutes(Media.RunTime).Ticks;
                }

                Application.CurrentInstance.UpdatePlayState(Media, PlayState, e.PlaylistPosition, e.Position, duration, !IncrementedPlayCount);

                IncrementedPlayCount = true;
            }
        }

        /// <summary>
        /// Fires whenever the PlaybackController reports that playback has stopped
        /// Subclasses which don't use the PlaybackController can also call this manually
        /// </summary>
        protected void OnPlaybackFinished(object sender, PlaybackStateEventArgs e)
        {
            if (IsPlaybackEventOnCurrentInstance(e) && PlayState != null)
            {
                // If it has a position then update it one last time
                if (e.Position > 0)
                {
                    OnProgress(sender, e);
                }

                // If we haven't been able to update position, at least mark it watched
                if (!IncrementedPlayCount)
                {
                    MarkWatched();
                }
            }

            OnPlaybackFinished();
        }

        protected virtual void OnPlaybackFinished()
        {
            // Clean up event handlers
            PlaybackController.Progress -= OnProgress;
            PlaybackController.PlaybackFinished -= OnPlaybackFinished;

            if (Media != null)
            {
                UpdateResumeStatusIfMediaIsCurrentItem(Media);
            }

        }

        protected void UpdateResumeStatusIfMediaIsCurrentItem(Media media)
        {
            if (media.Id == Application.CurrentInstance.CurrentItem.BaseItem.Id)
            {
                Logger.ReportVerbose("Updating Resume status...");
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
                Application.CurrentInstance.UpdatePlayState(Media, PlayState, 0, 0, 0, true);
            }
        }

        protected virtual void ShufflePlayableItems()
        {
            Random rnd = new Random();

            IEnumerable<string> newList = PlayableItems.OrderBy(i => rnd.Next());

            PlayableItems.Clear();

            AddMedia(newList);
        }

        public bool CanBePlayedByController(IPlaybackController controller)
        {
            return controller.CanPlay(PlayableItems);
        }
    }

}
