using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.RemoteControl;

namespace MediaBrowser.Library.Playables
{
    class PlayableDvd : PlayableItem
    {
        protected override void SendFilesToPlayer(PlaybackArguments args)
        {
            args.Files = args.Files.Select(i => "DVD://" + i);
            
            base.SendFilesToPlayer(args);
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
