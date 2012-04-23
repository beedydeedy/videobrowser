using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Playables.ExternalPlayer;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;
using Microsoft.Win32;

namespace MediaBrowser.Library.Playables.MpcHc
{
    public class PlayableMpcHc : PlayableExternal
    {
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
            string playstatePath = GetIniFilePath(ExternalPlayerConfiguration);

            if (!string.IsNullOrEmpty(playstatePath))
            {
                return Helper.ParseIniFile(playstatePath);
            }

            return GetRegistryKeyValues(Registry.CurrentUser.OpenSubKey("Software\\Gabest\\Media Player Classic\\Settings"));
        }

        public static string GetIniFilePath(ConfigData.ExternalPlayer currentConfiguration)
        {
            string directory = Path.GetDirectoryName(currentConfiguration.Command);

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

    }
}
