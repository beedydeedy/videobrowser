using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;
using Microsoft.Win32;

namespace MediaBrowser.Library.Playables
{
    public class PlayableMpcHc : PlayableExternal
    {
        protected override ConfigData.ExternalPlayerType ExternalPlayerType
        {
            get { return ConfigData.ExternalPlayerType.MpcHc; }
        }

        /// <summary>
        /// Gets arguments to be passed to the command line.
        /// </summary>
        protected override List<string> GetCommandArgumentsList(PlaybackArguments playInfo)
        {
            List<string> args = new List<string>();

            args.Add("{0}");
            args.Add("/play");
            args.Add("/close");
            args.Add("/fullscreen");
            
            // Be explicit about start time, to avoid any possible player auto-resume settings
            double startTimeInMs = playInfo.Resume ? new TimeSpan(playInfo.PositionTicks).TotalMilliseconds : 0;

            args.Add("/start " + startTimeInMs);

            return args;
        }

        protected override IEnumerable<string> GetPlayableFiles(Media media)
        {
            return base.GetPlayableFiles(media).Select(i => i.TrimEnd('\\'));
        }

        /// <summary>
        /// Gets the watched state after playback has stopped.
        /// </summary>
        protected override PlaybackStateEventArgs GetPlaybackState()
        {
            NameValueCollection values = GetMPCHCSettings();

            PlaybackStateEventArgs state = GetPlaybackState(values, PlayableFiles);

            state.PlayableItemId = PlayableItemId;

            return state;
        }

        private NameValueCollection GetMPCHCSettings()
        {
            // mpc-hc.ini will only exist if the user has enabled "Store settings to ini file"
            string playstatePath = GetIniFilePath();

            if (!string.IsNullOrEmpty(playstatePath))
            {
                return Helper.ParseIniFile(playstatePath);
            }

            return GetRegistryKeyValues(Registry.CurrentUser.OpenSubKey("Software\\Gabest\\Media Player Classic\\Settings"));
        }

        private string GetIniFilePath()
        {
            string directory = Path.GetDirectoryName(ExternalPlayerConfiguration.Command);

            string path = Path.Combine(directory, "mpc-hc.ini");

            if (File.Exists(path))
            {
                return path;
            }

            path = Path.Combine(directory, "mpc-hc64.ini");

            if (File.Exists(path))
            {
                return path;
            }

            return string.Empty;
        }

        /// <summary>
        /// Looks through ini file values to find playstate for a given collection of files
        /// </summary>
        private PlaybackStateEventArgs GetPlaybackState(NameValueCollection values, IEnumerable<string> files)
        {
            PlaybackStateEventArgs args = new PlaybackStateEventArgs();

            for (int i = 0; i < files.Count(); i++)
            {
                args.PlaylistPosition = i;

                PlaybackStateEventArgs fileState = GetPlaybackState(values, files.ElementAt(i));
                
                // If file position is > 0 that means playback was stopped during this file
                if (fileState.Position > 0)
                {
                    args.Position = fileState.Position;
                    break;
                }
            }

            return args;
        }

        /// <summary>
        /// Looks through ini file values to find playstate for a given file
        /// </summary>
        private PlaybackStateEventArgs GetPlaybackState(NameValueCollection values, string filename)
        {
            for (int i = 0; i <= 19; i++)
            {
                if (values["File Name " + i].StartsWith(filename))
                {
                    return new PlaybackStateEventArgs() { Position = long.Parse(values["File Position " + i]) };
                }
            }

            return new PlaybackStateEventArgs() { PlayableItemId = PlayableItemId };
        }

        public override ConfigData.ExternalPlayer GetDefaultConfiguration()
        {
            ConfigData.ExternalPlayer config = base.GetDefaultConfiguration();

            config.SupportsMultiFileCommandArguments = true;

            return config;
        }

        public void ConfigurePlayer()
        {
            // General settings
            SetBaseSettings();

            // Remote settings
            SetCommandSettings();
        }

        /// <summary>
        /// Configures basic settings to allow mpc-hc to work nicely with MB
        /// </summary>
        private void SetBaseSettings()
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

            string iniPath = GetIniFilePath();

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
        private void SetCommandSettings()
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

            string iniPath = GetIniFilePath();

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
        /// Gets all names and values of a registry key
        /// </summary>
        private static NameValueCollection GetRegistryKeyValues(RegistryKey key)
        {
            NameValueCollection values = new NameValueCollection();

            foreach (string keyName in key.GetValueNames())
            {
                values[keyName] = key.GetValue(keyName).ToString();
            }

            return values;
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
    }
}
