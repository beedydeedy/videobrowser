using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Events
{
    public class PlaybackEventArgs : EventArgs
    {
        public IEnumerable<Media> MediaItems { get; set; }
        public IPlaybackController PlaybackController { get; set; }
    }
}
