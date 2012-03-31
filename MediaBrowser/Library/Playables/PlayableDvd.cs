using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables
{
    class PlayableDvd : PlayableItem
    {
        protected override void SendFilesToPlayer(PlaybackArguments args)
        {
            args.Files = args.Files.Select(i => GetDVDPath(i));
            
            base.SendFilesToPlayer(args);
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

        public override bool CanPlay(Media media)
        {
            Video video = media as Video;

            if (video == null)
            {
                return false;
            }

            return video.MediaType == MediaType.DVD;
        }

        public override bool CanPlay(string path)
        {
            return MediaTypeResolver.DetermineType(path) == MediaType.DVD;
        }
    }
}
