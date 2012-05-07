using System;

namespace MediaBrowser.Library.Playables.TMT5
{
    /// <summary>
    /// Represents an external player that uses the WMC add-in
    /// </summary>
    public class PlayableTMT5AddInForWMC : PlayableTMT5
    {
        protected override Type PlaybackControllerType
        {
            get
            {
                return typeof(TMT5AddInPlaybackController);
            }
        }
    }
}
