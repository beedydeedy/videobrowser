using System;
using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Events;
using MediaBrowser.Library.Playables;

namespace MediaBrowser.Library.RemoteControl
{
    public class PlaybackStateEventArgs : GenericEventArgs<PlayableItem>
    {
        /// <summary>
        /// Gets or sets the Id of the current Media object being played
        /// </summary>
        public Guid CurrentMediaId { get; set; }

        /// <summary>
        /// Gets or sets the overall playlist position of the current playing file.
        /// That is, with respect to all files from all Media items
        /// </summary>
        public int FilePlaylistPosition { get; set; }

        /// <summary>
        /// Gets or sets the playlist position within the current Media item being played
        /// This is used when the Media item has multiple playables files (e.g. multi-file video).
        /// </summary>
        public int MediaPlaylistPosition { get; set; }

        /// <summary>
        /// Gets or sets the position of the player, in Ticks
        /// </summary>
        public long Position { get; set; }

        // The duration of the item in progress, as read from the player
        public long DurationFromPlayer { get; set; }

        private bool? _StoppedByUser;
        /// <summary>
        /// Gets or sets whether playback was explicitly stopped by the user
        /// </summary>
        public bool StoppedByUser
        {
            get
            {
                // We use the nullable bool so that if a player has some special way of knowing
                // for sure whether playback was stopped, it can be passed in
                // Otherwise we will attempt to auto-detect
                if (_StoppedByUser.HasValue)
                {
                    return _StoppedByUser.Value;
                }

                // If we know the duration, use it to make a guess whether playback was forcefully stopped by the user, as opposed to allowing it to finish
                if (DurationFromPlayer > 0)
                {
                    decimal pctIn = Decimal.Divide(Position, DurationFromPlayer) * 100;

                    return pctIn < Config.Instance.MaxResumePct;
                }

                // Fallback to this if no duration was reported
                return Position > 0;
            }
            set
            {
                _StoppedByUser = value;
            }
        }

        /// <summary>
        /// Gets the current file being played
        /// </summary>
        public string CurrentFile
        {
            get
            {
                return Item.Files.ElementAt(FilePlaylistPosition);
            }
        }

        /// <summary>
        /// Gets the current Media object being played
        /// </summary>
        public Media CurrentMedia
        {
            get
            {
                return Item.MediaItems.FirstOrDefault(m => m.Id == CurrentMediaId);
            }
        }
    }
}
