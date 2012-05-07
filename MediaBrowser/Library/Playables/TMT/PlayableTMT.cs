using System;
using MediaBrowser.Library.Playables.ExternalPlayer;

namespace MediaBrowser.Library.Playables.TMT
{
    /// <summary>
    /// Represents an external player that uses the standalone TMT application
    /// </summary>
    public class PlayableTMT : PlayableExternal
    {
        protected override Type PlaybackControllerType
        {
            get
            {
                return typeof(TMTPlaybackController);
            }
        }
    }
}
