using System.Collections.Generic;
using MediaBrowser.Library.Playables.ExternalPlayer;
using Microsoft.Win32;

namespace MediaBrowser.Library.Playables.MpcHc
{
    /// <summary>
    /// Controls editing MPC-HC settings within the configurator
    /// </summary>
    public class PlayableMpcHcConfigurator : PlayableExternalConfigurator
    {
        /// <summary>
        /// Returns a unique name for the external player
        /// </summary>
        public override string ExternalPlayerName
        {
            get { return "MPC-HC"; }
        }

        public override ConfigData.ExternalPlayer GetDefaultConfiguration()
        {
            ConfigData.ExternalPlayer config = base.GetDefaultConfiguration();

            config.SupportsMultiFileCommandArguments = true;

            return config;
        }

        public override bool SupportsConfiguringUserSettings
        {
            get
            {
                return true;
            }
        }

        public override string PlayerTips
        {
            get
            {
                return "Enable the following settings: \"Keep history of recently opened files\", \"Always on top\" and \"Don't use search in folder on commands skip back/forward\". Also map \"MEDIA_STOP\" to the \"exit\" command.";
            }
        }

        public override IEnumerable<string> GetKnownPlayerPaths()
        {
            List<string> paths = new List<string>();

            paths.AddRange(GetProgramFilesPaths("Media Player Classic - Home Cinema\\mpc-hc.exe"));
            paths.AddRange(GetProgramFilesPaths("Media Player Classic - Home Cinema\\mpc-hc64.exe"));

            return paths;
        }

        public override string ConfigureUserSettingsConfirmationMessage
        {
            get
            {
                string msg = "The following settings will be configured for you:";

                msg += "\n\n-Enable: Keep history of recently opened files";
                msg += "\n-Disable: Remember file position";
                msg += "\n-Disable: Remember DVD position";
                msg += "\n-Enable: Use global media keys";
                msg += "\n-Enable: Don't use 'search in folder' on commands 'Skip back/forward' when only one item in playlist";
                msg += "\n-Set medium jump size to 30 seconds (for rewind/ff buttons)";
                msg += "\n-Configure basic media center remote buttons";

                msg += "\n\nAre you sure you would like to continue?";

                return msg;
            }
        }

        public override void ConfigureUserSettings(ConfigData.ExternalPlayer currentConfiguration)
        {
            // General settings
            SetBaseSettings(currentConfiguration);

            // Remote settings
            SetCommandSettings(currentConfiguration);
        }

        /// <summary>
        /// Configures basic settings to allow mpc-hc to work nicely with MB
        /// </summary>
        private void SetBaseSettings(ConfigData.ExternalPlayer currentConfiguration)
        {
            Dictionary<string, object> values = new Dictionary<string, object>();

            values["KeepHistory"] = 1;
            values["RememberPlaylistItems"] = 1;
            values["Remember DVD Pos"] = 0;
            values["Remember File Pos"] = 0;
            values["SearchInDirAfterPlayBack"] = 0;
            values["DontUseSearchInFolder"] = 1;
            values["UseGlobalMedia"] = 1;
            values["JumpDistM"] = 30000;

            string iniPath = MpcHcPlaybackController.GetIniFilePath(currentConfiguration);

            if (string.IsNullOrEmpty(iniPath))
            {
                SetRegistryKeyValues(Registry.CurrentUser.OpenSubKey("Software\\Gabest\\Media Player Classic\\Settings", true), values);
            }
            else
            {
                SetIniFileValues(iniPath, "Settings", values);
            }
        }

        /// <summary>
        /// Configures MPC-HC to work with a media center remote
        /// </summary>
        private void SetCommandSettings(ConfigData.ExternalPlayer currentConfiguration)
        {
            Dictionary<string, object> values = new Dictionary<string, object>();

            // These are unreadable, but they setup basic functions such as play, pause, stop, back, next, ff, rw, etc
            values["CommandMod0"] = "816 13 58 \"\" 5 0 13 0";
            values["CommandMod1"] = "890 3 be \"\" 5 0 0 0";
            values["CommandMod2"] = "902 b 0 \"\" 5 0 49 0";
            values["CommandMod3"] = "901 b 0 \"\" 5 0 50 0";
            values["CommandMod4"] = "904 b 27 \"\" 5 0 0 0";
            values["CommandMod5"] = "903 b 25 \"\" 5 0 0 0";
            values["CommandMod6"] = "920 b 22 \"\" 5 0 51 0";
            values["CommandMod7"] = "919 b 21 \"\" 5 0 52 0";
            values["CommandMod8"] = "907 1 0 \"\" 5 16 10 16";
            values["CommandMod9"] = "908 1 0 \"\" 5 17 9 17";
            values["CommandMod10"] = "929 1 25 \"\" 5 0 0 0";
            values["CommandMod11"] = "930 1 27 \"\" 5 0 0 0";
            values["CommandMod12"] = "931 1 26 \"\" 5 0 0 0";
            values["CommandMod13"] = "932 1 28 \"\" 5 0 0 0";
            values["CommandMod15"] = "933 1 d \"\" 5 0 0 0";
            values["CommandMod15"] = "934 1 8 \"\" 5 0 1 0";
            values["CommandMod16"] = "32778 b 49 \"\" 5 0 66057 0";
            values["CommandMod17"] = "32780 3 59 \"\" 5 0 0 0";
            values["CommandMod18"] = "32781 3 55 \"\" 5 0 0 0";

            string iniPath = MpcHcPlaybackController.GetIniFilePath(currentConfiguration);

            if (string.IsNullOrEmpty(iniPath))
            {
                SetRegistryKeyValues(Registry.CurrentUser.OpenSubKey("Software\\Gabest\\Media Player Classic\\Commands2", true), values);
            }
            else
            {
                SetIniFileValues(iniPath, "Commands2", values);
            }
        }

        /// <summary>
        /// Sets values within a registry key
        /// </summary>
        private static void SetRegistryKeyValues(RegistryKey key, Dictionary<string, object> values)
        {
            foreach (string keyName in values.Keys)
            {
                key.SetValue(keyName, values[keyName]);
            }
        }

        private void SetIniFileValues(string path, string sectionName, Dictionary<string, object> values)
        {
            IniFile instance = new IniFile();

            instance.Load(path);

            foreach (string keyName in values.Keys)
            {
                instance.SetKeyValue(sectionName, keyName, values[keyName].ToString());
            }

            instance.Save(path);
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
