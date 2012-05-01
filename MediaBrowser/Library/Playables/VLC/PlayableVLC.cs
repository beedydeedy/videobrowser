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
            Video video = media as Video;

            if (video != null)
            {
                // VLC handles this like a champ
                if (video.MediaType == MediaType.DVD || video.MediaType == MediaType.ISO)
                {
                    return true;
                }
            }

            return base.IsPlaylistCapable(media);
        }
    }
}
