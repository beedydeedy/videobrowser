using System.Linq;
using MediaBrowser.Library.Playables.ExternalPlayer;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables.VLC
{
    public class PlayableVLC : PlayableExternal
    {
        protected override IPlaybackController GetPlaybackController()
        {
            return Kernel.Instance.PlaybackControllers.First(p => p.GetType() == typeof(VLCPlaybackController));
        }   
    }

}
