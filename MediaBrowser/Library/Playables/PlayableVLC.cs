using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Library.Entities;

namespace MediaBrowser.Library.Playables
{
    public class PlayableVLC : PlayableExternal
    {
        protected override ConfigData.ExternalPlayerType ExternalPlayerType
        {
            get { return ConfigData.ExternalPlayerType.VLC; }
        }

        /// <summary>
        /// Adds additional arguments to the command line. Usually for resume
        /// </summary>
        protected override List<string> GetAdditionalCommandArguments(bool resume)
        {
            List<string> args = base.GetAdditionalCommandArguments(resume);

            if (resume)
            {
                TimeSpan time = new TimeSpan(PlayState.PositionTicks);

                args.Add("--start-time=" + Convert.ToInt32(time.TotalSeconds));
            }

            return args;
        }

        protected override IEnumerable<string> GetFilesToSendToPlayer(Media media, PlaybackStatus playstate, IEnumerable<string> files, bool resume)
        {
            IEnumerable<string> filesToPlay = base.GetFilesToSendToPlayer(media, playstate, files, resume);

            Video video = media as Video;

            if (video != null && video.MediaType == Library.MediaType.DVD)
            {
                filesToPlay = filesToPlay.Select(i => "dvd://" + i);
            }

            return filesToPlay;
        }

        /// <summary>
        /// Gets the default configuration that will be pre-populated into the UI of the configurator.
        /// </summary>
        public override ConfigData.ExternalPlayer GetDefaultConfiguration()
        {
            ConfigData.ExternalPlayer config = base.GetDefaultConfiguration();

            config.Args = "{0} --fullscreen --play-and-exit --no-one-instance --global-key-quit=\"Media Stop\"";
            config.SupportsMultiFileCommandArguments = true;

            return config;
        }
    }
}
