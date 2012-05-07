using System;

namespace MediaBrowser.Library.Playables.TMT
{
    /// <summary>
    /// Represents an external player that uses the WMC add-in
    /// </summary>
    public class PlayableTMTAddInForWMC : PlayableTMT
    {
        protected override Type PlaybackControllerType
        {
            get
            {
                return typeof(TMTAddInPlaybackController);
            }
        }
    }
}
