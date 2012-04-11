using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Factories;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Has all the features of PlayableVideo, except this class also has Multi-Entity support, meaning PlayState tracking across multiple Media objects.
    /// </summary>
    public class PlayableMultiMediaVideo : PlayableItem
    {
        /// <summary>
        /// This holds the list of Media objects from which PlayableItems will be created.
        /// </summary>
        protected List<Media> PlayableMediaItems = new List<Media>();

        /// <summary>
        /// If playback is folder-based this will hold a reference to the original Folder object
        /// </summary>
        protected Folder Folder { get; set; }

        public override void AddMedia(IEnumerable<Media> mediaItems)
        {
            if (mediaItems.Count() > 1)
            {
                // First filter out items that can't be queued in a playlist
                mediaItems = mediaItems.Where(m => m.IsPlaylistCapable());
            }

            PlayableMediaItems.AddRange(mediaItems);
            base.AddMedia(mediaItems);
        }

        public override void AddMedia(Folder folder)
        {
            base.AddMedia(folder);

            Folder = folder;
        }

        /// <summary>
        /// Creates a list of PlayableItems that use PlaybackController, and thus, the internal player
        /// </summary>
        private IEnumerable<PlayableItem> CreatePlayableItemsFromInternalPlayer()
        {
            List<PlayableItem> playables = new List<PlayableItem>();

            for (int i = 0; i < PlayableMediaItems.Count; i++)
            {
                PlayableItem playable = PlayableItemFactory.Instance.Create(PlayableMediaItems[i], false);

                // Each one after the first will be queued
                playable.QueueItem = i == 0 ? QueueItem : true;

                playables.Add(playable);
            }

            return playables;
        }

        /// <summary>
        /// Overrides playback by having each PlayableItem play individually
        /// </summary>
        protected override void SendFilesToPlayer(PlaybackArguments playbackInfo)
        {
            IEnumerable<PlayableItem> playables = CreatePlayableItemsFromInternalPlayer();

            for (int i = 0; i < playables.Count(); i++)
            {
                // Only the first one is allowed to resume, obviously
                bool resume = i == 0 ? playbackInfo.Resume : false;

                playables.ElementAt(i).Play(resume);
            }
        }

        protected override void ShufflePlayableItems()
        {
            Random rnd = new Random();

            IEnumerable<Media> newList = PlayableMediaItems.OrderBy(i => rnd.Next());

            PlayableMediaItems.Clear();

            AddMedia(newList);
        }

        /// <summary>
        /// We can play a list of Media provided they're all video
        /// </summary>
        public override bool CanPlay(IEnumerable<Media> mediaList)
        {
            foreach (Media media in mediaList)
            {
                if (!(media is Video))
                {
                    return false;
                }
            }

            return true;
        }

        protected override void UpdateResumeStatusInUI()
        {
            base.UpdateResumeStatusInUI();

            foreach (Media media in PlayableMediaItems)
            {
                if (media.Id == Application.CurrentInstance.CurrentItem.BaseItem.Id)
                {
                    Application.CurrentInstance.CurrentItem.UpdateResume();
                    break;
                }
            }
        }
    }
}
