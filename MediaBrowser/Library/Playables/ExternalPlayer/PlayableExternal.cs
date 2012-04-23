using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables.ExternalPlayer
{
    /// <summary>
    /// Represents an abstract base class for all externally playable items
    /// </summary>
    public class PlayableExternal : PlayableItem
    {
        #region CanPlay
        public override bool CanPlay(IEnumerable<string> files)
        {
            return ConfigData.CanPlay(ExternalPlayerConfiguration, files);
        }

        public override bool CanPlay(IEnumerable<Media> mediaList)
        {
            return ConfigData.CanPlay(ExternalPlayerConfiguration, mediaList);
        }

        public override bool CanPlay(Media media)
        {
            return CanPlay(new Media[] { media });
        }

        public override bool CanPlay(string path)
        {
            return CanPlay(new string[] { path });
        }
        #endregion
        
        /// <summary>
        /// Gets the ExternalPlayer configuration for this instance
        /// </summary>
        public ConfigData.ExternalPlayer ExternalPlayerConfiguration
        {
            get;
            set;
        }

        protected override IPlaybackController GetPlaybackController()
        {
            return Kernel.Instance.PlaybackControllers.First(p => p.GetType() == typeof(ConfigurableExternalPlaybackController));
        }

        protected override void Prepare()
        {
            base.Prepare();

            // Need to stop other players, in particular the internal 7MC player
            Application.CurrentInstance.StopAllPlayback();

            (PlaybackController as ConfigurableExternalPlaybackController).ExternalPlayerConfiguration = ExternalPlayerConfiguration;
        }
    }
}
