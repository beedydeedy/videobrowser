using System.Collections.Generic;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Playables;

namespace MediaBrowser.Library.Events
{
    public class PlaybackEventArgs : GenericEventArgs<PlayableItem>
    {
        public IEnumerable<Media> MediaItems { get; set; }
    }
}
