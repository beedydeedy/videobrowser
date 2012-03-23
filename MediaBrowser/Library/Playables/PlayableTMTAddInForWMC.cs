﻿using System.Collections.Generic;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Represents an external player that uses the WMC add-in
    /// </summary>
    public class PlayableTMTAddInForWMC : PlayableTMT
    {
        protected override ConfigData.ExternalPlayerType ExternalPlayerType
        {
            get { return ConfigData.ExternalPlayerType.TMTAddInForWMC; }
        }

        /// <summary>
        /// Gets arguments to be passed to the command line.
        /// </summary>
        protected override List<string> GetCommandArgumentsList(bool resume)
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

        /// <summary>
        /// Gets the default configuration that will be pre-populated into the UI of the configurator.
        /// </summary>
        public override ConfigData.ExternalPlayer GetDefaultConfiguration()
        {
            ConfigData.ExternalPlayer config = base.GetDefaultConfiguration();

            config.LaunchType = ConfigData.ExternalPlayerLaunchType.WMCNavigate;

            return config;
        }
    }
}
