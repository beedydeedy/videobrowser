﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using System.Globalization;
using MediaBrowser.Library.Extensions;
using MediaBrowser.Library.Configuration;
using MediaBrowser.Library.Localization;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Persistance;

//***************************************************************************************************
//  This class is used to extend the string data used by MB.  It is localizable.
//  The most common use will be to provide description strings for config options.  To do this
//  define public string properties that are named the same as the label text of your options on your
//  config panel +desc.  A couple of examples have been generated by the template.
//***************************************************************************************************
namespace Diamond
{
    [Serializable]
    public class MyStrings : LocalizedStringData    
    {
        const string VERSION = "0.3.0.7"; //this is used to see if we have changed and need to re-save

        //these are our strings keyed by property name
        public string DiamondOptionsDesc = "Options for the Diamond Theme.";
        public string MediaDetailsinMiniModeDesc = "Start the details page for media items in mini-mode.";
        public string DisplayEndTimeDesc = "Calculate and display end time of current media item.";
        
        public string DisplayInfoboxinCoverflowViewsDesc = "Display an information box for media in Coverflow view.";
        public string DisplayInfoboxinThumbstripViewsDesc = "Display an information box for media in Thumbstrip view.";
        public string DisplayInfoboxinPosterViewsDesc = "Display an information box for media in Poster view.";

        public string DisplayGlassOverlayDesc = "Display diamond glass overlay on poster images.";

        public string DisplayWeatherDesc = "Display weather in diamond theme.";
        public string DisplayColourMediaInfoIconsDesc = "Replaces monochrome mediainfo icons with colour mediainfo icons.";
        public string ExtenderLayoutEnhancementsDesc = "Use custom layout adjustments for use with extenders.";

        //Diamond Config Panel
        public string DiamondOptions = "Diamond Theme Options";
        public string MediaDetailsinMiniMode = "Media Details in Mini Mode";
        public string DisplayEndTime = "Display End Time";
        public string DisplayInfoboxinCoverflowViews = "Display Infobox in Coverflow Views";
        public string DisplayInfoboxinThumbstripViews = "Display Infobox in Thumbstrip Views";
        public string DisplayInfoboxinPosterViews = "Display Infobox in Poster Views";
        public string DisplayGlassOverlay = "Display Glass Overlay";
        public string DisplayWeather = "Display Weather";
        public string ExtenderLayoutEnhancements = "Extender Layout Enhancements";
        public string EHSGradientOpacity = "EHS Gradient Opacity";
        public string RequireRestart = "*Changes require a restart.*";

        //EHS
        public string RecentlyAddedUnwatchedEHS = "recently added unwatched";

        //Movie Detail Page
        public string DisplayDetail = "Display";
        public string GenreDetail = "Genre";
        public string StudioDetail = "Studio";
        public string FirstAiredDetail = "First Aired";
        public string EpisodesDetail = "Episodes";
        public string MediaDetail = "Media-Details";

        //Media details
        public string LocationMedia = "Location";
        public string VCodecMedia = "Video Codec";
        public string VResMedia = "Video Resolution";
        public string VFrameRateMedia = "Video Frame Rate";
        public string ACodecMedia = "Audio Codec";
        public string AStreamsMedia = "Audio Streams";
        public string AChannelsMedia = "Audio Channels";
        public string SubtitlesMedia = "Subtitles";

        //FolderMenu
        public string FolderMenuFM = "Folder Menu";
        public string ViewByFM = "View By";
        public string SortByFM = "Sort By";
        public string IndexByFM = "Index By";
        public string CoverSizeFM = "Cover Size";
        public string BackdropFM = "Backdrop";
        public string TitlesFM = "Titles";
        public string VerticalScrollFM = "Vertical Scroll";
        public string BannersFM = "Banners";
        public string increaseFM = "increase";
        public string decreaseFM = "decrease";
        public string enableFM = "enable";
        public string disableFM = "disable";

        //Album
        public string AlbumInfoAlb = "Album Info";
        public string QueueAlb = "Queue Album";
        public string ShuffleAlb = "Shuffle Album";

        //public string ExtenderLayoutEnhancementsDesc = "Automatic layout edge enhancement for use with extenders. This may result in overscan (does not affect non-extender devices).";
        MyStrings(string file)
        {
            this.FileName = file;
        }

        public MyStrings() //for the serializer
        {
        }

        public static MyStrings FromFile(string file)
        {
            MyStrings s = new MyStrings();
            XmlSettings<MyStrings> settings = XmlSettings<MyStrings>.Bind(s, file);

            Logger.ReportInfo("Using String Data from " + file);

            if (VERSION != s.Version)
            {
                File.Delete(file);
                s = new MyStrings();
                settings = XmlSettings<MyStrings>.Bind(s, file);
            }
            return s;
        }
    }
}
