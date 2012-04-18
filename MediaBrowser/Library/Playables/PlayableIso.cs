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

        protected override void Prepare()
        {
            base.Prepare();

            string isoPath = GetIsoPath();
            string mountedPath = Application.CurrentInstance.MountISO(isoPath);
            
            // Play the DVD video that was mounted.
            if (!Config.Instance.UseAutoPlayForIso)
            {
                playableExternal = CreatePlayableItemFromMountedPath(mountedPath);
                playableExternal.Resume = Resume;
            }
        }

        protected virtual PlayableItem CreatePlayableItemFromMountedPath(string mountedPath)
        {
            Video video = PlayableMediaItems.FirstOrDefault() as Video;

            if (video == null)
            {
                return PlayableItemFactory.Instance.Create(mountedPath);
            }
            else
            {
                video.Path = mountedPath;

                video.MediaType = MediaTypeResolver.DetermineType(mountedPath);
                video.DisplayMediaType = video.MediaType.ToString();

                return PlayableItemFactory.Instance.Create(video);
            }
        }

        private string GetIsoPath()
        {
            Media media = PlayableMediaItems.FirstOrDefault();

            if (media != null)
            {
                // Playback is based on a strongly typed Media object

                Video video = media as Video;

                if (video != null && video.MediaLocation is IFolderMediaLocation)
                {
                    return Helper.GetIsoFiles(video.Path)[0];
                }

                return Helper.GetIsoFiles(media.Files.First())[0];
            }
            else
            {
                // Playback is based on a string path to the ISO

                return Helper.GetIsoFiles(PlayableFiles.First())[0];
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
                playableExternal.Play();
            }
        }
    }
}
