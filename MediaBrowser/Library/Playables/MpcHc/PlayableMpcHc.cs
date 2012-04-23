using System.Linq;
using MediaBrowser.Library.Playables.ExternalPlayer;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables.MpcHc
{
    public class PlayableMpcHc : PlayableExternal
    {
        protected override IPlaybackController GetPlaybackController()
        {
            return Kernel.Instance.PlaybackControllers.First(p => p.GetType() == typeof(MpcHcPlaybackController));
        }        
    }
}
