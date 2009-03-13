﻿using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.MediaCenter.UI;
using System.IO;
using System.Diagnostics;

namespace MediaBrowser.Library
{
    public class MediaMetadata : ModelItem
    {
        private static readonly List<string> emptyStringList = new List<string>();
        private static readonly List<Actor> emptyActorList = new List<Actor>();
        private static readonly List<Studio> emptyStudioList = new List<Studio>();
        private static readonly BackgroundProcessor<RefreshObj> processor = new BackgroundProcessor<RefreshObj>(ThreadPoolSizes.METADATA_REFRESH_THREADS, RefreshProcessor, "MetadataRefresh");
        private MediaMetadataStore store;
        private ItemType type;
        private LibraryImage preferedImage;
        private LibraryImage primaryImage;
        private LibraryImage secondaryImage;
        private LibraryImage bannerImage;
        private LibraryImage backdropImage;
        private bool saveEnabled = false;
        private string sortableName = null;
        private bool preferBanner = false;
        object refreshLock = new object();
        RefreshObj refreshPending = null;
        private MediaMetadataStore intermediateStore = null;

        public MediaMetadata()
        {
        }

        /// <summary>
        /// The default is that saving is disabled when the item is constructed
        /// </summary>
        /// <param name="ownerName"></param>
        public void Assign(UniqueName ownerName, ItemType type)
        {
            store = new MediaMetadataStore(ownerName);
            this.type = type;
        }

        public void Assign(MediaMetadataStore store, ItemType type)
        {
            this.store = store;
            this.type = type;
        }
        
        public UniqueName OwnerName
        {
            get { return this.store.OwnerName; }
        }

        
        
        public string SortableName
        {
            get 
            {
                // if a value exists for SortName, use it for the sortable name
                if (this.SortName != null)
                    return GetSortableName(this.SortName);

                if (this.sortableName == null)
                    lock (this)
                        if (this.sortableName == null)
                            this.sortableName = GetSortableName(this.Name);
                return sortableName; 
            }
        }

        /// <summary>
        /// Created to support mymovies.xml SortTitle entity.
        /// </summary>
        public string SortName
        {
            get { return this.store.SortName; }
            set
            {
                if (this.store.SortName != value) {
                    this.store.SortName = value;
                    lock (this)
                        this.sortableName = null;
                    FirePropertyChanged("SortName");
                    FirePropertyChanged("SortableName");
                    Save();
                }
            }
        }

        public string Name {
            get { return this.store.Name ?? ""; }
            set {
                if (this.store.Name != value) {
                    this.store.Name = value;
                    lock (this)
                        this.sortableName = null;
                    FirePropertyChanged("Name");
                    FirePropertyChanged("SortableName");
                    Save();
                }
            }
        }

        public string SubName
        {
            get { return this.store.SubName ?? ""; }
            set { if (this.store.SubName != value) { this.store.SubName = value; FirePropertyChanged("SubName"); Save(); } }
        }

        public string Overview
        {
            get { return this.store.Overview ?? ""; }
            set { if (this.store.Overview != value) { this.store.Overview = value; FirePropertyChanged("Overview"); Save(); } }
        }

        public string TrailerPath
        {
            get { return this.store.TrailerPath ?? ""; }
            set { if (this.store.TrailerPath != value) { this.store.TrailerPath = value; FirePropertyChanged("TrailerPath"); Save(); } }
        }

        public string Status
        {
            get { return this.store.Status ?? ""; }
            set { if (this.store.Status != value) { this.store.Status = value; FirePropertyChanged("Status"); Save(); } }
        }
        public string FirstAired
        {
            get { return this.store.FirstAired ?? ""; }
            set { if (this.store.FirstAired != value) { this.store.FirstAired = value; FirePropertyChanged("FirstAired"); Save(); } }
        }
        public bool HasPrimaryImage
        {
            get { return (this.store.PrimaryImage != null); }
        }

        public ImageSource PrimaryImageSource
        {
            get
            {
                return this.store.PrimaryImage;
            }
            set
            {
                if ((this.store.PrimaryImage != value)
                    && ((this.store.PrimaryImage == null ? null : this.store.PrimaryImage.OriginalSource) != (value == null ? null : value.OriginalSource)) // ensure we don't try to recache the same image and waste time
                    )
                {
                    this.store.PrimaryImage = value;
                    if (this.primaryImage != null)
                        this.primaryImage.Source = this.PrimaryImageSource;
                    if ((!this.PreferBanner) && (this.preferedImage != null))
                    {
                        this.preferedImage.Source = value;
                        FirePropertyChanged("PreferredImage");
                        FirePropertyChanged("HasPreferredImage");
                    }
                    FirePropertyChanged("PrimaryImageSource");
                    //FirePropertyChanged("PrimaryImage"); 
                    FirePropertyChanged("HasPrimaryImage");
                    Save();
                }
            }
        }

        private ImageSource PrimaryImageSourceInternal
        {
            get
            {
                if (this.store.PrimaryImage != null)
                    return this.store.PrimaryImage;
                else
                {
                    switch (this.type)
                    {
                        case ItemType.Episode:
                        case ItemType.Movie:
                            return new ImageSource { OriginalSource = "res://ehres!MOVIE.ICON.DEFAULT.PNG", LocalSource = "res://ehres!MOVIE.ICON.DEFAULT.PNG" };
                        case ItemType.Actor:
                            return new ImageSource { OriginalSource = "resx://MediaBrowser/MediaBrowser.Resources/MissingPerson", LocalSource = "resx://MediaBrowser/MediaBrowser.Resources/MissingPerson" };
                        case ItemType.Studio:
                            return new ImageSource { OriginalSource = "resx://MediaBrowser/MediaBrowser.Resources/BlankGraphic", LocalSource = "resx://MediaBrowser/MediaBrowser.Resources/MissingStudio" };                       
                        case ItemType.Other:
                            return null;
                        default:
                            return new ImageSource { OriginalSource = "resx://MediaBrowser/MediaBrowser.Resources/folder", LocalSource = "resx://MediaBrowser/MediaBrowser.Resources/folder" };
                    }
                }
            }
        }

        public LibraryImage PrimaryImage
        {
            get
            {
                if (this.primaryImage == null)
                {
                    this.primaryImage = new LibraryImage(this.PrimaryImageSourceInternal);
                    this.primaryImage.PropertyChanged += new PropertyChangedEventHandler(ImagePropertyChanged);
                }
                return this.primaryImage;
            }
        }

        public void ImagePropertyChanged(IPropertyObject sender, string property)
        {
            if (property == "SourceCache")
                Save();
        }

        bool HasSecondaryImage
        {
            get { return (this.store.SecondaryImage != null); }
        }

        public ImageSource SecondaryImageSource
        {
            get { return this.store.SecondaryImage; }
            set
            {
                if ((this.store.SecondaryImage != value)
                    && ((this.store.SecondaryImage == null ? null : this.store.SecondaryImage.OriginalSource) != (value == null ? null : value.OriginalSource))// ensure we don't try to recache the same image and waste time
                    )
                {
                    this.store.SecondaryImage = value;
                    if (this.secondaryImage != null)
                        this.secondaryImage.Source = this.SecondaryImageSource;
                    FirePropertyChanged("SecondaryImageSource");
                    FirePropertyChanged("HasSecondaryImage");
                    Save();
                }
            }
        }

        public LibraryImage SecondaryImage
        {
            get
            {
                if (this.secondaryImage == null)
                {
                    this.secondaryImage = new LibraryImage(this.SecondaryImageSource);
                    this.secondaryImage.PropertyChanged += new PropertyChangedEventHandler(ImagePropertyChanged);
                }
                return this.secondaryImage;
            }
        }

        public bool HasBannerImage
        {
            get { return (this.store.BannerImage != null); }
        }

        public ImageSource BannerImageSource
        {
            get
            {
                if (this.store.BannerImage != null)
                    return this.store.BannerImage;
                else
                {
                    switch (this.type)
                    {
                        case ItemType.Episode:
                        case ItemType.Movie:
                            return new ImageSource { OriginalSource = "res://ehres!MOVIE.ICON.DEFAULT.PNG", LocalSource = "res://ehres!MOVIE.ICON.DEFAULT.PNG" };
                        case ItemType.Other:
                            return null;
                        default:
                            return new ImageSource { OriginalSource = "resx://MediaBrowser/MediaBrowser.Resources/Banner", LocalSource = "resx://MediaBrowser/MediaBrowser.Resources/Banner" };
                    }
                }
            }
            set
            {
                if ((this.store.BannerImage != value)
                    && ((this.store.BannerImage == null ? null : this.store.BannerImage.OriginalSource) != (value == null ? null : value.OriginalSource))// ensure we don't try to recache the same image and waste time
                    )
                {
                    this.store.BannerImage = value;
                    if (this.bannerImage != null)
                        this.bannerImage.Source = this.BannerImageSource;
                    if ((this.PreferBanner) && (this.preferedImage != null))
                    {
                        this.preferedImage.Source = value;
                        FirePropertyChanged("PreferredImage");
                        FirePropertyChanged("HasPreferredImage");
                    }
                    FirePropertyChanged("BannerImageSource");
                    FirePropertyChanged("HasBannerImage");
                    Save();
                }
            }
        }

        public LibraryImage BannerImage
        {
            get
            {
                if (this.bannerImage == null)
                {
                    this.bannerImage = new LibraryImage(this.BannerImageSource);
                    this.bannerImage.PropertyChanged += new PropertyChangedEventHandler(ImagePropertyChanged);
                }
                return this.bannerImage;
            }
        }


        public bool HasBackdropImage
        {
            get { return (this.store.BackdropImage != null); }
        }

        public ImageSource BackdropImageSource
        {
            get { return this.store.BackdropImage; }
            set
            {
                if ((this.store.BackdropImage != value)
                    && ((this.store.BackdropImage == null ? null : this.store.BackdropImage.OriginalSource) != (value == null ? null : value.OriginalSource))// ensure we don't try to recache the same image and waste time
                    )
                {
                    this.store.BackdropImage = value;
                    if (this.backdropImage != null)
                        this.backdropImage.Source = this.BackdropImageSource;
                    FirePropertyChanged("BackdropImageSource");
                    FirePropertyChanged("HasBackdropImage");
                    Save();
                }
            }
        }

        public LibraryImage BackdropImage
        {
            get
            {
                if (this.backdropImage == null)
                {
                    this.backdropImage = new LibraryImage(this.BackdropImageSource);
                    this.backdropImage.PropertyChanged += new PropertyChangedEventHandler(ImagePropertyChanged);
                }
                return this.backdropImage;
            }
        }

        
        public bool PreferBanner
        {
            get { return this.preferBanner; }
            set
            {
                if (this.preferBanner != value)
                {
                    this.preferBanner = value;
                    if (this.preferedImage != null)
                        this.preferedImage.Source = this.PreferBanner ? this.BannerImageSource : this.PrimaryImageSourceInternal;
                    FirePropertyChanged("HasPreferredImage");
                    FirePropertyChanged("PreferredImage");
                }
            }
        }

        public bool HasPreferredImage
        {
            get { return (this.PreferBanner ? this.HasBannerImage : this.HasPrimaryImage); }
        }

        public LibraryImage PreferredImage
        {
            get
            {
                if (this.preferedImage == null)
                {
                    this.preferedImage = new LibraryImage(this.PreferBanner ? this.BannerImageSource : this.PrimaryImageSourceInternal);
                    this.preferedImage.PropertyChanged += new PropertyChangedEventHandler(ImagePropertyChanged);
                }
                return this.preferedImage;
            }
        }

        public string SeasonNumber
        {
            get { return this.store.SeasonNumber ?? ""; }
            set { if (this.store.SeasonNumber != value) { this.store.SeasonNumber = value; FirePropertyChanged("SeasonNumber"); Save(); } }
        }

        public string EpisodeNumber
        {
            get { return this.store.EpisodeNumber ?? ""; }
            set { if (this.store.EpisodeNumber != value) { this.store.EpisodeNumber = value; FirePropertyChanged("EpisodeNumber"); Save(); } }
        }

        public float? ImdbRating
        {
            get { return this.store.ImdbRating ?? -1; }
            set { if (this.store.ImdbRating != value) { this.store.ImdbRating = value; FirePropertyChanged("ImdbRating"); FirePropertyChanged("ImdbRatingString"); Save(); } }
        }

        public string ImdbRatingString
        {
            get { return (ImdbRating).Value.ToString("0.##"); }
        }

        public string MpaaRating
        {
            get { return this.store.MpaaRating ?? ""; }
            set { if (this.store.MpaaRating != value) { this.store.MpaaRating = value; FirePropertyChanged("MpaaRating"); Save(); } }
        }

        public int? ProductionYear
        {
            get { return this.store.ProductionYear; }
            set { if (this.store.ProductionYear != value) { this.store.ProductionYear = value; FirePropertyChanged("ProductionYear"); Save(); } }
        }

        public string ProductionYearString
        {
            get { return this.store.ProductionYear == null ? "" : this.ProductionYear.Value.ToString(); }
        }

        public int? RunningTime
        {
            get { return this.store.RunningTime; }
            set { if (this.store.RunningTime != value) { this.store.RunningTime = value; FirePropertyChanged("RunningTime"); FirePropertyChanged("RunningTimeString"); Save(); } }
        }

        public string RunningTimeString
        {
            get { return this.RunningTime != null ? this.RunningTime.ToString() + " mins" : ""; }
        }

        public List<string> Directors
        {
            get { return this.store.Directors ?? emptyStringList; }
            set { if (this.store.Directors != value) { this.store.Directors = value; FirePropertyChanged("Directors"); FirePropertyChanged("DirectorString"); Save(); } }
        }

        public List<string> Writers
        {
            get { return this.store.Writers ?? emptyStringList; }
            set { if (this.store.Writers != value) { this.store.Writers = value; FirePropertyChanged("Writers"); FirePropertyChanged("WritersString"); Save(); } }
        }

        public string DirectorString
        {
            get { return string.Join(", ", this.Directors.ToArray()); }
        }

        public string WritersString
        {
            get { return string.Join(", ", this.Writers.ToArray()); }
        }

        public List<Actor> Actors
        {
            get { return this.store.Actors ?? emptyActorList; }
            set { if (this.store.Actors != value) { this.store.Actors = value; FirePropertyChanged("Actors"); Save(); } }
        }
        public List<Studio> Studios
        {
            get { return this.store.Studios ?? emptyStudioList; }
            set { if (this.store.Studios != value) { this.store.Studios = value; FirePropertyChanged("Studios"); Save(); } }
        }
        public List<string> Genres
        {
            get { return this.store.Genres ?? emptyStringList; }
            set { if (this.store.Genres != value) { this.store.Genres = value; FirePropertyChanged("Genres"); Save(); } }
        }

        public DateTime? DataTimestamp
        {
            get { return this.store.UtcDataTimestamp; }
            set { if (this.store.UtcDataTimestamp != value) { this.store.UtcDataTimestamp = value; FirePropertyChanged("DataTimestamp"); Save(); } }
        }

        public MediaInfoData MediaInfo
        {
            get { return this.store.MediaInfo == null ? MediaInfoData.Empty : this.store.MediaInfo; }
            set { if (this.store.MediaInfo != value) { this.store.MediaInfo = value; FirePropertyChanged("MediaInfo"); FirePropertyChanged("HasMediaInfo"); FirePropertyChanged("IsHD"); Save(); } }
        }

        public bool HasMediaInfo
        {
            get { return this.store.MediaInfo != null; }
        }

        public bool IsHD
        {
            get 
            { 
                return ((this.MediaInfo.Width >= 1280) || (this.MediaInfo.Height >=720)); 
            }
        }

        public int HDType
        {
            get
            {
                if ((this.MediaInfo.Width >= 1920) || (this.MediaInfo.Height >= 1080))
                    return 1080;
                else if (IsHD)
                    return 720;
                else
                    return 0;
            }
        }

        public string DataSource
        {
            get { return this.store.DataSource; }
            set { if (this.store.DataSource != value) { this.store.DataSource = value; FirePropertyChanged("DataSource"); Save(); } }
        }

        public Dictionary<string, string> ProviderData
        {
            get { return this.store.ProviderData; }
        }

        public static string GetSortableName(string name)
        {
            string sortable = name.ToLower();
            foreach (string search in Config.Instance.SortRemoveCharactersArray)
            {
                sortable = sortable.Replace(search.ToLower(), string.Empty);
            }
            foreach (string search in Config.Instance.SortReplaceCharactersArray)
            {
                sortable = sortable.Replace(search.ToLower(), " ");
            }
            foreach (string search in Config.Instance.SortReplaceWordsArray)
            {
                string searchLower = search.ToLower();
                // Remove from beginning if a space follows
                if (sortable.StartsWith(searchLower + " "))
                {
                    sortable = sortable.Remove(0, searchLower.Length + 1);
                }
                // Remove from middle if surrounded by spaces
                sortable = sortable.Replace(" " + searchLower + " ", " ");

                // Remove from end if followed by a space
                if (sortable.EndsWith(" " + searchLower))
                {
                    sortable = sortable.Remove(sortable.Length - (searchLower.Length + 1));
                }
            }
            //sortableDescription = sortableDescription.Trim();
            return sortable.Trim();
        }

        private class RefreshObj
        {
            public Item Item;
            public MediaMetadata Metadata;
            public bool Force;
            public bool FastFirst;
        }

        private static void RefreshProcessor(RefreshObj obj)
        {
            obj.Metadata.RefreshMetadata(obj);
            Microsoft.MediaCenter.UI.Application.DeferredInvoke(obj.Metadata.RefreshMetadataDone, null);
            if (obj.FastFirst)
            {
                obj.FastFirst = false;
                processor.Enqueue(obj);
            }
        }

        
        internal void RefreshAsync(Item item, bool force, bool fastFirst)
        {
            if (refreshPending==null)
                lock (refreshLock)
                    if (refreshPending == null)
                    {
                        refreshPending = new RefreshObj { Metadata = this, Item = item, Force = force, FastFirst = fastFirst };
                        processor.Inject(refreshPending);
                    }
        }

        internal void RefreshToFront()
        {
            if (refreshPending!=null)
                lock (refreshLock)
                    if (refreshPending!=null)
                        processor.PullToFront(refreshPending);
        }
        

        private void RefreshMetadata(object obj)
        {
            RefreshObj r = (RefreshObj)obj;
            intermediateStore = MetaDataSource.Instance.RefreshMetadata(r.Item, r.Force, r.FastFirst);
            lock (refreshLock)
                refreshPending = null;
        }

        private void RefreshMetadataDone(object nothing)
        {
            if (intermediateStore != null)
            {
                this.AssignFrom(intermediateStore);
                intermediateStore = null;
                Save();
            }
        }

        internal void AssignFrom(MediaMetadataStore data)
        {
            bool old = this.saveEnabled;
            this.saveEnabled = false;
            try
            {
                this.Name = data.Name;
                this.SortName = data.SortName;
                this.SubName = data.SubName;
                this.Overview = data.Overview;
                this.PrimaryImageSource = data.PrimaryImage;
                this.SecondaryImageSource = data.SecondaryImage;
                this.BannerImageSource = data.BannerImage;
                this.BackdropImageSource = data.BackdropImage;
                this.SeasonNumber = data.SeasonNumber;
                this.EpisodeNumber = data.EpisodeNumber;
                this.ImdbRating = data.ImdbRating;
                this.MpaaRating = data.MpaaRating;
                this.ProductionYear = data.ProductionYear;
                this.RunningTime = data.RunningTime;
                this.Directors = data.Directors;
                this.Writers = data.Writers;
                this.Actors = data.Actors;
                this.Genres = data.Genres;
                this.DataTimestamp = data.UtcDataTimestamp;
                this.DataSource = data.DataSource;
                this.MediaInfo = data.MediaInfo;
                this.store.ProviderData = data.ProviderData;
                this.Status = data.Status;
                this.TrailerPath = data.TrailerPath;
                this.FirstAired = data.FirstAired;
                this.Studios = data.Studios;
            }
            finally
            {
                this.saveEnabled = old;
                Save();
            }
        }

        public void Save()
        {
            if ((!saveEnabled) || (this.OwnerName == null))
                return;
            ItemCache.Instance.SaveMetadata(this.store);
        }

        public bool SaveEnabled
        {
            get { return this.saveEnabled; }
            set { this.saveEnabled = value; }
        }

        public bool HasDataForDetailPage
        {
            get
            {
                int score = 0;
                if ((this.store.Actors != null) && (this.store.Actors.Count > 0))
                    score += 2;
                if ((this.store.Studios != null) && (this.store.Studios.Count > 0))
                    score += 2;
                if ((this.store.Genres != null) && (this.store.Genres.Count > 0))
                    score += 2;
                if ((this.store.Directors != null) && (this.store.Directors.Count > 0))
                    score += 2;
                if ((this.store.Writers != null) && (this.store.Writers.Count > 0))
                    score += 2;
                if (this.store.Overview != null)
                    score += 2;
                if (this.store.MpaaRating != null)
                    score += 1;
                if (this.store.ImdbRating != null)
                    score += 1;
                if (this.store.ProductionYear != null)
                    score += 1;
                if (this.store.RunningTime != null)
                    score += 1;
                return score > 5;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (this.primaryImage != null)
                this.primaryImage.Dispose();
            if (this.secondaryImage != null)
                this.secondaryImage.Dispose();
            if (this.bannerImage != null)
                this.bannerImage.Dispose();
            if (this.backdropImage != null)
                this.backdropImage.Dispose();
            base.Dispose(disposing);
        }
    }

    [Flags]
    public enum ItemType
    {
        None = 0,
        Movie = 1,
        Series = 2,
        Season = 4,
        Episode = 8,
        Folder = 16,
        VirtualFolder = 32,
        Actor = 64,
        Director = 128,
        Year = 256,
        Genre = 512,
        Studio = 768,
        Other = 1024,
        All = 2047
    }
}