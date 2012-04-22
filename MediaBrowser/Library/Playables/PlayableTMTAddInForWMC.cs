using System.Collections.Generic;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Represents an external player that uses the WMC add-in
    /// </summary>
    public class PlayableTMTAddInForWMC : PlayableTMT
    {
        /// <summary>
        /// Gets arguments to be passed to the command line.
        /// </summary>
        protected override List<string> GetCommandArgumentsList(PlaybackArguments playbackInfo)
        {
            List<string> args = new List<string>();

            args.Add("uri={0}");

            return args;
        }
        
        /// <summary>
        /// Removes double quotes and flips slashes
        /// </summary>
        protected override string GetFilePathCommandArgument(IEnumerable<string> filesToPlay)
        {
            return base.GetFilePathCommandArgument(filesToPlay).Replace("\"", string.Empty).Replace('\\', '/');
        }

        protected override string PlayStatePathAppName
        {
            get
            {
                return "ArcSoft TotalMedia Theatre 5(Media Center)";
            }
        }

    }

    public class PlayablePlayableTMTAddInForWMCConfigurator : PlayableTMTConfigurator
    {
        /// <summary>
        /// Returns a unique name for the external player
        /// </summary>
        public override string ExternalPlayerName
        {
            get { return "TotalMedia Theatre WMC Add-On"; }
        }

        /// <summary>
        /// Gets the default configuration that will be pre-populated into the UI of the configurator.
        /// </summary>
        public override ConfigData.ExternalPlayer GetDefaultConfiguration()
        {
            ConfigData.ExternalPlayer config = base.GetDefaultConfiguration();

            config.LaunchType = ConfigData.ExternalPlayerLaunchType.WMCNavigate;

            return config;
        }

        public override string PlayerTips
        {
            get
            {
                return "You will need to enable \"auto-fullscreen\". There is no resume support at this time. There is no multi-part movie or folder-based playback support at this time.";
            }
        }

        public override string CommandFieldTooltip
        {
            get
            {
                return "The path to PlayerLoader.htm within the TMT installation directory.";
            }
        }

        public override IEnumerable<string> GetKnownPlayerPaths()
        {
            return GetProgramFilesPaths("ArcSoft\\TotalMedia Theatre 5\\PlayerLoader.htm");
        }
    }
}
