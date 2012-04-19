using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Factories;
using MediaBrowser.Library.Filesystem;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;
using System.Collections.Generic;

namespace MediaBrowser.Library.Playables
{
    /// <summary>
    /// This is a unique PlayableItem in that it mounts the ISO and then passes the new path off to another PlayableItem
    /// </summary>
    class PlayableIso : PlayableItem
    {
        PlayableItem playableExternal = null;

        protected override void Prepare()
        {
            base.Prepare();

            string isoPath = PlayableFiles.First();
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

            video.Path = mountedPath;

            video.MediaType = MediaTypeResolver.DetermineType(mountedPath);
            video.DisplayMediaType = video.MediaType.ToString();

            return PlayableItemFactory.Instance.Create(video);
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

        public override bool CanPlay(IEnumerable<Media> mediaList)
        {
            if (mediaList.Count() == 1)
            {
                return CanPlay(mediaList.First());
            }

            return false;
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
