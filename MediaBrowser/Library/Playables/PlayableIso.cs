using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Factories;
using MediaBrowser.Library.Filesystem;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;

namespace MediaBrowser.Library.Playables
{
    class PlayableIso : PlayableItem
    {
        PlayableItem playableExternal = null;

        protected override void Prepare(bool resume)
        {
            base.Prepare(resume);

            string isoPath = GetIsoPath();
            string mountedPath = Application.CurrentInstance.MountISO(isoPath);
            
            // Play the DVD video that was mounted.
            if (!Config.Instance.UseAutoPlayForIso)
            {
                playableExternal = PlayableItemFactory.Instance.Create(mountedPath);
                playableExternal.PlayState = PlayState;
            }
        }

        private string GetIsoPath()
        {
            Video video = Media as Video;

            if (video != null && video.MediaLocation is IFolderMediaLocation)
            {
                return Helper.GetIsoFiles(video.Path)[0];
            }
            else
            {
                return Helper.GetIsoFiles(PlayableItems.First())[0];
            }

        }

        public override bool CanPlay(Media media)
        {
            // can play DVDs and normal videos
            Video video = media as Video;

            if (video == null)
            {
                return false;
            }

            return video.MediaType == MediaType.ISO;
        }

        public override bool CanPlay(string path)
        {
            return MediaTypeResolver.DetermineType(path) == MediaType.ISO;
        }

        protected override void SendFilesToPlayer(PlaybackArguments args)
        {
            if (!Config.Instance.UseAutoPlayForIso)
            {
                playableExternal.Play(args.Resume);
            }
        }
    }
}
