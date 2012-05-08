using System;
using System.Collections.Generic;
using MediaBrowser.Library;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Metadata;
using MediaBrowser.Library.Persistance;

namespace MBTrailers {
    public class ITunesTrailer : Movie {

        [Persist]
        public string RealPath { get; set; }

        public override bool CanResume
        {
            get
            {
                return false;
            }
        }
        
        public override bool RefreshMetadata(MetadataRefreshOptions options) {
            bool changed = false;
            string path = Plugin.proxy == null ? this.Path : Plugin.proxy.ProxyUrl(this);
            if (this.Path != path)
            {
                this.Path = path;
                changed = true;
            }

            MediaType = MediaTypeResolver.DetermineType(Path);
            
            if ((options & MetadataRefreshOptions.FastOnly) != MetadataRefreshOptions.FastOnly &&
                Plugin.PluginOptions.Instance.FetchBackdrops && string.IsNullOrEmpty(this.BackdropImagePath))
            {
                // use our own movieDBProvider to grab just the backdrops
                var provider = new BackdropProvider();
                provider.Item = (Movie)Serializer.Clone(this);
                provider.Fetch();
                this.BackdropImagePaths = provider.Item.BackdropImagePaths;
                foreach (var image in this.BackdropImages) {
                    try
                    {
                        if (image != null)
                        {
                            image.ClearLocalImages();
                            MediaBrowser.Library.Factories.LibraryImageFactory.Instance.ClearCache(image.Path);
                            var ignore = image.GetLocalImagePath();
                        }
                    }
                    catch (Exception ex)
                    {
                        MediaBrowser.Library.Logging.Logger.ReportException("Failed to clear local image (its probably in use)", ex);
                    }
                }
                changed = true;

            }
            
            if ((options & MetadataRefreshOptions.Force) == MetadataRefreshOptions.Force)
            {
                //force images to refresh
                var images = new List<MediaBrowser.Library.ImageManagement.LibraryImage>();
                images.Add(PrimaryImage);
                images.Add(SecondaryImage);
                images.Add(BannerImage);

                foreach (var image in images)
                {
                    try
                    {
                        if (image != null)
                        {
                            image.ClearLocalImages();
                            MediaBrowser.Library.Factories.LibraryImageFactory.Instance.ClearCache(image.Path);
                        }
                    }
                    catch (Exception ex)
                    {
                        MediaBrowser.Library.Logging.Logger.ReportException("Failed to clear local image (its probably in use)", ex);
                    }
                }
                changed = true;
            }
            if (changed) MediaBrowser.Library.Kernel.Instance.ItemRepository.SaveItem(this);
            return changed;
        }
    }
}
