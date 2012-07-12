using System;
using System.IO;
using MediaBrowser.Library.Configuration;
using MediaBrowser.Library.Logging;
using Microsoft.MediaCenter.UI;

namespace MediaBrowser.Library
{
    public static class DefaultImages
    {
        private static readonly string location;

        private static readonly Image ActorImage;
        private static readonly Image StudioImage;
        private static readonly Image FolderImage;
        private static readonly Image VideoImage;

        static DefaultImages()
        {
            location = Config.Instance.ImageByNameLocation;

            if (string.IsNullOrEmpty(location))
            {
                location = Path.Combine(ApplicationPaths.AppConfigPath, "ImagesByName");
            }
            
            VideoImage = GetDefaultImage(GetPath("video"), "res://ehres!MOVIE.ICON.DEFAULT.PNG");
            ActorImage = GetDefaultImage(GetPath("actor"), "resx://MediaBrowser/MediaBrowser.Resources/MissingPerson");
            StudioImage = GetDefaultImage(GetPath("studio"), "resx://MediaBrowser/MediaBrowser.Resources/BlankGraphic");
            FolderImage = GetDefaultImage(GetPath("folder"), "resx://MediaBrowser/MediaBrowser.Resources/folder");
        }

        public static Image Actor
        {
            get { return ActorImage; }
        }
        public static Image Studio
        {
            get { return StudioImage; }
        }
        public static Image Folder
        {
            get { return FolderImage; }
        }
        public static Image Video
        {
            get { return VideoImage; }
        }

        private static Image GetDefaultImage(string customImagePath, string defaultImagePath)
        {
            var image = new Image(customImagePath);

            if (image.HasError)
            {
                image = new Image(defaultImagePath);
            }
            return image;
        }

        private static string GetPath(string path)
        {
            var s = string.Format(@"file://{0}default/{1}/folder.png", location.Replace('\\', '/'), path);
            return s;
        }
    }
}