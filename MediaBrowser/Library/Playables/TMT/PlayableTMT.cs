using System.Linq;
using MediaBrowser.Library.Playables.ExternalPlayer;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables.TMT
{
    /// <summary>
    /// Represents an external player that uses the standalone TMT application
    /// </summary>
    public class PlayableTMT : PlayableExternal
    {
        protected override IPlaybackController GetPlaybackController()
        {
            return Kernel.Instance.PlaybackControllers.First(p => p.GetType() == typeof(TMTPlaybackController));
        }
    }
}
