using System.Linq;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables.TMT
{
    /// <summary>
    /// Represents an external player that uses the WMC add-in
    /// </summary>
    public class PlayableTMTAddInForWMC : PlayableTMT
    {
        protected override IPlaybackController GetPlaybackController()
        {
            return Kernel.Instance.PlaybackControllers.First(p => p.GetType() == typeof(TMTAddInPlaybackController));
        }
    }
}
