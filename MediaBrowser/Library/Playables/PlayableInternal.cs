using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// Represents a PlayableItem that uses the internal WMC player
    /// </summary>
    public class PlayableInternal : PlayableItem
    {
        /// <summary>
        /// Takes a Media object and returns the list of files that will be sent to the PlaybackController
        /// </summary>
        /// <param name="media"></param>
        /// <returns></returns>
        protected override IEnumerable<string> GetPlayableFiles(Media media)
        {
            IEnumerable<string> files = base.GetPlayableFiles(media);

            Video video = media as Video;

            // Prefix dvd's with dvd://
            if (video != null && video.MediaType == MediaType.DVD)
            {
                files = files.Select(i => GetDVDPath(i));
            }

            return files;
        }

        private string GetDVDPath(string path)
        {
            if (path.StartsWith("\\\\"))
            {
                path = path.Substring(2);
            }

            path = path.Replace("\\", "/").TrimEnd('/');

            return "DVD://" + path + "/";
        }

        /// <summary>
        /// Determines whether this PlayableItem can play a list of Media objects
        /// </summary>
        public override bool CanPlay(IEnumerable<Media> mediaList)
        {
            foreach (Media media in mediaList)
            {
                if (!CanPlay(media))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether this PlayableItem can play a Media object
        /// </summary>
        public override bool CanPlay(Media media)
        {
            // can play DVDs and normal videos
            Video video = media as Video;

            if (video == null)
            {
                return false;
            }

            return CanPlay(video.MediaType);
        }

        /// <summary>
        /// Determines whether this PlayableItem can play a file
        /// </summary>
        public override bool CanPlay(string path)
        {
            return CanPlay(MediaTypeResolver.DetermineType(path));
        }

        /// <summary>
        /// Determines whether this PlayableItem can play a MediaType
        /// </summary>
        private bool CanPlay(MediaType type)
        {
            if (type == MediaType.DVD)
            {
                return true;
            }

            if (Video.IsRippedMedia(type))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether this PlayableItem can play a list of files
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
