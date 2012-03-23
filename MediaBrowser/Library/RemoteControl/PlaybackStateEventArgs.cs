using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MediaBrowser.Library.RemoteControl
{
    public class PlaybackStateEventArgs : EventArgs
    {
        public int PlaylistPosition { get; set; }
        public long Position { get; set; }

        // The duration of the item in progress, as read from the player
        public long DurationFromPlayer { get; set; }

        public Guid PlayableItemId { get; set; }

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
    }
}
