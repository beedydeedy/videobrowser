using System;
using MediaBrowser.Library.Playables.ExternalPlayer;

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
    }
}
