using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Represents an external player that uses the WMC add-in
    /// </summary>
    class PlayableTMTMcml : PlayableTMT
    {
        protected override ConfigData.ExternalPlayerType ExternalPlayerType
        {
            get { return ConfigData.ExternalPlayerType.TMTMcml; }
        }

        /// <summary>
        /// Removes double quotes and flips slashes
        /// </summary>
        protected override string GetFilePathCommandArgument(IEnumerable<string> filesToPlay)
        {
            return base.GetFilePathCommandArgument(filesToPlay).Replace("\"", string.Empty).Replace('\\', '/');
        }

        protected override void SendFilesToPlayer(PlaybackArguments args)
        {
            base.SendFilesToPlayer(args);

            WaitForPlaybackToStartThenStop(args);
            OnExternalPlayerClosed();
        }

        /// <summary>
        /// Gets the default configuration that will be pre-populated into the UI of the configurator.
        /// </summary>
        public override ConfigData.ExternalPlayer GetDefaultConfiguration()
        {
            ConfigData.ExternalPlayer config = base.GetDefaultConfiguration();

            config.ShowSplashScreen = false;
            config.MinimizeMCE = false;
            config.LaunchType = ConfigData.ExternalPlayerLaunchType.WMCNavigate;
            config.Args = "uri={0}";

            return config;
        }
    }
}
