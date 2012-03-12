using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Library.Entities;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Represents a Playable that can play one Media object, which could contain multiple files
    /// This can also play an individual file based on a path name, albeit without PlayState support.
    /// </summary>
    public class PlayableVideo : PlayableItem
    {
        /// <summary>
        /// We can play any video that is not ripped media (DVD, BluRay, etc)
        /// </summary>
        public override bool CanPlay(Media media)
        {
            // can play DVDs and normal videos
            Video video = media as Video;

            if (video == null)
            {
                return false;
            }

            return !video.ContainsRippedMedia;
        }

        /// <summary>
        /// We can play any video that is not ripped media (DVD, BluRay, etc)
        /// </summary>
        public override bool CanPlay(string path)
        {
            MediaType type = MediaTypeResolver.DetermineType(path);

            if (Video.IsRippedMedia(type))
            {
                return false;
            }

            if (Directory.Exists(path))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// We can play a list of files provided they're all video
        /// </summary>
        public override bool CanPlay(IEnumerable<string> files)
        {
            foreach (string file in files)
            {
                if (!CanPlay(file))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
