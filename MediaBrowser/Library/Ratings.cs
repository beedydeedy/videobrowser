using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Text;
using System.IO;
using MediaBrowser.LibraryManagement;
using MediaBrowser.Library.Localization;
using MediaBrowser.Library.Configuration;

namespace MediaBrowser.Library
{
    public class Ratings
    {
        private static RatingsDefinition ratingsDef;
        private static Dictionary<string, int> ratings;
        private static Dictionary<int, string> ratingsStrings = new Dictionary<int, string>();

        public Ratings(bool blockUnrated)
        {
            this.Initialize(blockUnrated);
        }

        public Ratings()
        {
            this.Initialize(false);
        }

        public void Initialize(bool blockUnrated)
        {
            //build our ratings dictionary from the combined local one and us one
            ratingsDef = RatingsDefinition.FromFile(Path.Combine(ApplicationPaths.AppLocalizationPath, "Ratings-" + Kernel.Instance.ConfigData.MetadataCountryCode+".xml"));
            ratings = new Dictionary<string, int>();
            //global value of None
            ratings.Add("None", -1);
            foreach (var pair in ratingsDef.RatingsDict) ratings.Add(pair.Key, pair.Value);
            if (Kernel.Instance.ConfigData.MetadataCountryCode.ToUpper() != "US")
                foreach (var pair in new USRatingsDictionary()) ratings.Add(pair.Key, pair.Value);
            //global values of CS
            ratings.Add("CS", 1000);
            if (blockUnrated)
            {
                ratings.Add("", 1000);
            }
            else
            {
                ratings.Add("", 0);
            }
            //and rating reverse lookup dictionary (non-redundant ones)
            ratingsStrings.Clear();
            int lastLevel = -10;
            foreach (var pair in ratingsDef.RatingsDict.OrderBy(p => p.Value))
            {
                if (pair.Value > lastLevel)
                {
                    lastLevel = pair.Value;
                    ratingsStrings.Add(pair.Value, pair.Key);
                }
            }
            ratingsStrings.Add(999, "CS"); //this is different because we want Custom to be protected, not allowed

            return;
        }

        public void SwitchUnrated(bool block)
        {
            ratings.Remove("");
            if (block)
            {
                ratings.Add("", 5);
            }
            else
            {
                ratings.Add("", 0);
            }
        }
        public static int Level(string ratingStr)
        {
            if (ratingStr != null && ratings.ContainsKey(ratingStr))
                return ratings[ratingStr];
            else
            {
                string stripped = stripCountry(ratingStr);
                if (ratingStr != null && ratings.ContainsKey(stripped))
                    return ratings[stripped];
                else
                    return ratings[""]; //return "unknown" level
            }
        }

        private static string stripCountry(string rating)
        {
            int start = rating.IndexOf('-');
            return start > 0 ? rating.Substring(start + 1) : rating;
        }

        public static string ToString(int level)
        {
            //return the closest one
            while (level > 0) 
            {
                if (ratingsStrings.ContainsKey(level))
                    return ratingsStrings[level];
                else 
                    level--;
            }
            return null;
        }
        public IEnumerable<string> ToStrings()
        {
            //return the whole list of ratings strings
            return ratingsStrings.Values;
        }

        public Microsoft.MediaCenter.UI.Image RatingImage(string rating)
        {
            return Helper.GetMediaInfoImage("Rated_" + rating);
        }


    }
}
