using System.Collections.Generic;
using MediaBrowser.Library.Playables.ExternalPlayer;

namespace MediaBrowser.Library.Playables.VLC
{
    /// <summary>
    /// Controls editing VLC settings within the configurator
    /// </summary>
    public class PlayableVLCConfigurator : PlayableExternalConfigurator
    {
        /// <summary>
        /// Returns a unique name for the external player
        /// </summary>
        public override string ExternalPlayerName
        {
            get { return "VLC"; }
        }

        /// <summary>
        /// Gets the default configuration that will be pre-populated into the UI of the configurator.
        /// </summary>
        public override ConfigData.ExternalPlayer GetDefaultConfiguration()
        {
            ConfigData.ExternalPlayer config = base.GetDefaultConfiguration();

            // http://wiki.videolan.org/VLC_command-line_help

            config.SupportsMultiFileCommandArguments = true;

            return config;
        }

        public override string PlayerTips
        {
            get
            {
                return "Version 2.0+ required. No special configuration is required.";
            }
        }

        public override IEnumerable<string> GetKnownPlayerPaths()
        {
            return GetProgramFilesPaths("VideoLAN\\VLC\\vlc.exe");
        }

        public override bool ShowIsoDirectLaunchWarning
        {
            get
            {
                return false;
            }
        }

        public override bool AllowArgumentsEditing
        {
            get
            {
                return false;
            }
        }

        public override bool AllowMinimizeMCEEditing
        {
            get
            {
                return false;
            }
        }

        public override bool AllowShowSplashScreenEditing
        {
            get
            {
                return false;
            }
        }
    }

}
