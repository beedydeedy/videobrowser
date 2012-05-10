using System.Collections.Generic;

namespace MediaBrowser.Library.Playables.TMT5
{
    public class TMT5AddInPlaybackController : TMT5PlaybackController
    {
        /// <summary>
        /// Gets arguments to be passed to the command line.
        /// </summary>
        protected override List<string> GetCommandArgumentsList(PlayableItem playbackInfo)
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

        protected override void StopInternal()
        {
            SendCommandToMMC("-stop");
        }
    }
}
