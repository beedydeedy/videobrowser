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
        /// Creates a list of PlayableItems that use PlaybackController, and thus, the internal player
        /// </summary>
        private IEnumerable<PlayableItem> CreatePlayableItemsFromInternalPlayer()
        {
            List<PlayableItem> playables = new List<PlayableItem>();

            for (int i = 0; i < PlayableMediaItems.Count(); i++)
            {
                PlayableItem playable = PlayableItemFactory.Instance.Create(PlayableMediaItems.ElementAt(i), false);

                // Each one after the first will be queued
                playable.QueueItem = i == 0 ? QueueItem : true;

                // Only the first can be resumed
                playable.Resume = i == 0 ? Resume : false;

                playables.Add(playable);
            }

            return playables;
        }

        /// <summary>
        /// Overrides playback by having each PlayableItem play individually
        /// </summary>
        protected override void SendFilesToPlayer(PlaybackArguments playbackInfo)
        {
            foreach (PlayableItem playable in CreatePlayableItemsFromInternalPlayer())
            {
                playable.Play();
            }
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
    }
}
