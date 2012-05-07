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
    public class MpcHcPlaybackController : ConfigurableExternalPlaybackController
    {
        /// <summary>
        /// Takes a Media object and returns the list of files that will be sent to the player
        /// </summary>
        internal override IEnumerable<string> GetPlayableFiles(Media media)
        {
            return base.GetPlayableFiles(media).Select(i => FormatPath(i));
        }

        /// <summary>
        /// Formats a path to send to the player
        /// </summary>
        private string FormatPath(string path)
        {
            if (path.EndsWith(":\\"))
            {
                path = path.TrimEnd('\\');
            }

            return path;
        }

        /// <summary>
        /// When playback is based purely on files, this will take the files that were supplied to the PlayableItem,
        /// and create the actual paths that will be sent to the player
        /// </summary>
        internal override IEnumerable<string> GetPlayableFiles(IEnumerable<string> files)
        {
            return base.GetPlayableFiles(files).Select(i => FormatPath(i));
        }

        /// <summary>
        /// Gets arguments to be passed to the command line.
        /// </summary>
        protected override List<string> GetCommandArgumentsList(PlayableItem playInfo)
        {
            List<string> args = new List<string>();

            args.Add("{0}");
            args.Add("/play");
            args.Add("/close");
            args.Add("/fullscreen");

            // Be explicit about start time, to avoid any possible player auto-resume settings
            double startTimeInMs = new TimeSpan(playInfo.StartPositionTicks).TotalMilliseconds;

            args.Add("/start " + startTimeInMs);

            return args;
        }

        /// <summary>
        /// Gets the watched state after playback has stopped.
        /// </summary>
        protected override PlaybackStateEventArgs GetPlaybackState()
        {
            PlayableItem playable = GetCurrentPlayableItem();

            if (playable != null)
            {
                NameValueCollection values = GetMPCHCSettings();

                PlaybackStateEventArgs state = GetPlaybackState(values, playable);

                state.Item = playable;

                return state;
            }

            return new PlaybackStateEventArgs();
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
        private PlaybackStateEventArgs GetPlaybackState(NameValueCollection values, PlayableItem playable)
        {
            return playable.HasMediaItems ? GetPlaybackStateBasedOnMediaItems(values, playable) : GetPlaybackStateBasedOnFiles(values, playable);
        }

        /// <summary>
        /// Looks through ini file values to find playstate for a given collection of files
        /// </summary>
        private PlaybackStateEventArgs GetPlaybackStateBasedOnMediaItems(NameValueCollection values, PlayableItem playable)
        {
            PlaybackStateEventArgs args = new PlaybackStateEventArgs();

            int numMediaItems = playable.MediaItems.Count();
            int totalFileCount = 0;

            for (int i = 0; i < numMediaItems; i++)
            {
                args.CurrentMediaIndex = i;

                Media media = playable.MediaItems.ElementAt(i);

                IEnumerable<string> files = GetPlayableFiles(media);

                int numFiles = files.Count();

                for (int j = 0; j < numFiles; j++)
                {
                    args.CurrentFileIndex = totalFileCount + j;

                    args.Position = GetPlaybackPosition(values, files.ElementAt(j));

                    // If file position is > 0 that means playback was stopped during this file
                    if (args.Position > 0)
                    {
                        return args;
                    }
                }

                totalFileCount += numFiles;
            }

            return args;
        }

        /// <summary>
        /// Looks through ini file values to find playstate for a given collection of files
        /// </summary>
        private PlaybackStateEventArgs GetPlaybackStateBasedOnFiles(NameValueCollection values, PlayableItem playable)
        {
            PlaybackStateEventArgs args = new PlaybackStateEventArgs();

            IEnumerable<string> files = playable.FilesFormattedForPlayer;

            int numFiles = files.Count();

            for (int i = 0; i < numFiles; i++)
            {
                args.CurrentFileIndex = i;

                args.Position = GetPlaybackPosition(values, files.ElementAt(i));

                // If file position is > 0 that means playback was stopped during this file
                if (args.Position > 0)
                {
                    break;
                }
            }

            return args;
        }

        /// <summary>
        /// Looks through ini file values to find playstate for a given file
        /// </summary>
        private long GetPlaybackPosition(NameValueCollection values, string filename)
        {
            for (int i = 0; i <= 19; i++)
            {
                if (values["File Name " + i].StartsWith(filename))
                {
                    return long.Parse(values["File Position " + i]);
                }
            }

            return 0;
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
