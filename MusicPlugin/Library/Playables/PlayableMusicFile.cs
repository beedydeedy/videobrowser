using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Playables;
using MusicPlugin.Library.Entities;

namespace MusicPlugin.Library.Playables
{
    class PlayableMusicFile : PlayableVideo
    {
        public override bool CanPlay(Media media)
        {
            return media is Music;
        }

        public override bool CanPlay(string path)
        {
            return false;
        }
    }
}
