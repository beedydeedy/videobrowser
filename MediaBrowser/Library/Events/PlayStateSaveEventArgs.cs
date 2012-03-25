using System;
using MediaBrowser.Library.Entities;

namespace MediaBrowser.Library.Events
{
    /// <summary>
    /// An EventArgs subclass that is used whenever PlaybackStatus objects are saved.
    /// </summary>
    public class PlayStateSaveEventArgs : EventArgs
    {
        public PlaybackStatus PlaybackStatus { get; set; }
        public BaseItem Item { get; set; }
    }
}
