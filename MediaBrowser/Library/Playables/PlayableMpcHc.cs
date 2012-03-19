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

        protected override List<string> GetAdditionalCommandArguments(bool resume)
        {
            List<string> args = base.GetAdditionalCommandArguments(resume);

            // Determine MediaType
            MediaType mediaType = MediaTypeResolver.DetermineType(PlayableItems.First());

            Video video = Media as Video;

            if (video != null)
            {
                mediaType = video.MediaType;
            }

            if (mediaType == MediaType.DVD)
            {
                args.Add("/dvd");
            }

            if (resume)
            {
                TimeSpan time = new TimeSpan(PlayState.PositionTicks);

                args.Add("/start " + time.TotalMilliseconds.ToString());
            }

            return args;
        }

        protected override IEnumerable<string> GetFilesToSendToPlayer(Media media, Entities.PlaybackStatus playstate, IEnumerable<string> files, bool resume)
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
            if (File.Exists(ExternalPlayerConfiguration.PlayStatePath))
            {
                return Helper.ParseIniFile(ExternalPlayerConfiguration.PlayStatePath);
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

            config.Args = "{0} /play /close /fullscreen";
            config.SupportsMultiFileCommandArguments = true;

            return config;
        }
    }
}
