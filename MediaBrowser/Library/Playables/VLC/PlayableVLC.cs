using System;
using MediaBrowser.Library.Playables.ExternalPlayer;
using MediaBrowser.Library.Entities;

namespace MediaBrowser.Library.Playables.VLC
{
    public class PlayableVLC : PlayableExternal
    {
        protected override Type PlaybackControllerType
        {
            get
            {
                return typeof(VLCPlaybackController);
            }
        }

        protected override bool IsPlaylistCapable(Media media)
        {
            // VLC seems to handle everything in a playlist
            return true;
        }
    }
}
