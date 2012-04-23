using System;
using System.Collections.Generic;

namespace MediaBrowser.Library.RemoteControl
{
    /// <summary>
    /// An object that describes what and how an IPlayBackController should play
    /// </summary>
    public class PlaybackArguments
    {
        public bool Resume { get; set; }
        public IEnumerable<string> Files { get; set; }

        public int PlaylistPosition { get; set; }
        public long PositionTicks { get; set; }

        public Guid PlayableItemId { get; set; }

        public bool GoFullScreen { get; set; }
    }
}
