using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Playables;
using MusicPlugin.Library.Entities;

namespace MusicPlugin.Library.Playables
{
    class PlayableMultiFileMusic : PlayableMultiMediaVideo
    {
        public override bool CanPlay(IEnumerable<Media> mediaList)
        {
            return mediaList.First() as Music != null;
        }
    }
}
