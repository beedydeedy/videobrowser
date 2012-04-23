using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables.ExternalPlayer
{
    public class ConfigurableExternalPlaybackController : ExternalPlaybackController
    {
        /// <summary>
        /// Gets the ExternalPlayer configuration for this instance
        /// </summary>
        public ConfigData.ExternalPlayer ExternalPlayerConfiguration
        {
            get;
            set;
        }

        protected override ConfigData.ExternalPlayerLaunchType LaunchType
        {
            get
            {
                return ExternalPlayerConfiguration.LaunchType;
            }
        }

        protected override bool ShowSplashScreen
        {
            get
            {
                return ExternalPlayerConfiguration.ShowSplashScreen;
            }
        }

        protected override bool MinimizeMCE
        {
            get
            {
                return ExternalPlayerConfiguration.MinimizeMCE;
            }
        }

        protected override string GetCommandPath(PlaybackArguments args)
        {
            return ExternalPlayerConfiguration.Command;
        }

        protected override List<string> GetCommandArgumentsList(PlaybackArguments playInfo)
        {
            List<string> args = new List<string>();

            if (!string.IsNullOrEmpty(ExternalPlayerConfiguration.Args))
            {
                args.Add(ExternalPlayerConfiguration.Args);
            }

            return args;
        }
    }
}
