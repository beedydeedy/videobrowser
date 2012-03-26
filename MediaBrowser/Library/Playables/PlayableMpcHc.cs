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
        protected override List<string> GetCommandArgumentsList(bool resume)
        {
            List<string> args = new List<string>();

            args.Add("{0}");
            args.Add("/play");
            args.Add("/close");
            args.Add("/fullscreen");
            
            // Be explicit about start time, to avoid any possible player auto-resume settings
            double startTimeInMs = resume ? new TimeSpan(PlayState.PositionTicks).TotalMilliseconds : 0;

            args.Add("/start " + startTimeInMs);

            return args;
        }

        protected override IEnumerable<string> GetFilesToSendToPlayer(Media media, PlaybackStatus playstate, IEnumerable<string> files, bool resume)
        {
            // For folder-based playback, such as dvd, mpc doesn't like trailing slashes
            return base.GetFilesToSendToPlayer(media, playstate, files, resume).Select(i => i.TrimEnd('\\'));
        }

        /// <summary>
        /// Gets the watched state after playback has stopped.
        /// </summary>
        protected override PlaybackStateEventArgs GetPlaybackState(IEnumerable<string> files)
        {
            NameValueCollection values = GetMPCHCSettings();

            PlaybackStateEventArgs state = GetPlaybackState(values, files);

            state.PlayableItemId = PlayableItemId;

            return state;
        }

        private NameValueCollection GetMPCHCSettings()
        {
            // mpc-hc.ini will only exist if the user has enabled "Store settings to ini file"
            string playstatePath = Path.ChangeExtension(ExternalPlayerConfiguration.Command, ".ini");

            if (File.Exists(playstatePath))
            {
                return Helper.ParseIniFile(playstatePath);
            }

            return Helper.GetRegistryKeyValues(Registry.CurrentUser, "Software\\Gabest\\Media Player Classic\\Settings");
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
                if (values["File Name " + i] == filename)
                {
                    return new PlaybackStateEventArgs() { Position = long.Parse(values["File Position " + i]) };
                }
            }

            return base.GetPlaybackState(new string[] { filename });
        }

        public override ConfigData.ExternalPlayer GetDefaultConfiguration()
        {
            ConfigData.ExternalPlayer config = base.GetDefaultConfiguration();

            config.SupportsMultiFileCommandArguments = true;

            return config;
        }
    }
}
