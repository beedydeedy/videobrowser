using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MediaBrowser.Library.RemoteControl {
    public class PlaybackStateEventArgs : EventArgs
    {
        public int PlaylistPosition { get; set; }
        public long Position { get; set; }

        // The duration of the item in progress, as read from the player
        public long DurationFromPlayer { get; set; }

        public Guid PlayableItemId { get; set; }

        public bool StoppedByUser { get; set; }
    }
}
