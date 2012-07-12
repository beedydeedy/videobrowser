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
        private static RatingsDefinition ratingsDef = RatingsDefinition.FromFile(Path.Combine(ApplicationPaths.AppLocalizationPath, "Ratings-" + Kernel.Instance.ConfigData.MetadataCountryCode+".xml"));
        private static Dictionary<string,int> usRatings = new USRatingsDictionary(); //we need this for defaults
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
            ratings = new Dictionary<string, int>();
            //global value of None
            ratings.Add("None", -1);
            foreach (var pair in ratingsDef.RatingsDict) ratings.Add(pair.Key, pair.Value);
            if (Kernel.Instance.ConfigData.MetadataCountryCode.ToUpper() != "US")
                foreach (var pair in usRatings) ratings.Add(pair.Key, pair.Value);
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
            //ratings.Add("None", -1);
            //ratings.Add("G", 1);
            //ratings.Add("E", 1);
            //ratings.Add("EC", 1);
            //ratings.Add("GB-U", 1);
            //ratings.Add("TV-G", 1);
            //ratings.Add("TV-Y", 1);
            //ratings.Add("TV-Y7", 1);
            //ratings.Add("TV-Y7-FV", 1);
            //ratings.Add("PG", 2);
            //ratings.Add("GB-PG", 2);
            //ratings.Add("GB-12", 2);
            //ratings.Add("GB-12A", 2);
            //ratings.Add("10+", 2);
            //ratings.Add("TV-PG", 2);
            //ratings.Add("PG-13", 3);
            //ratings.Add("T", 3);
            //ratings.Add("GB-15", 3);
            //ratings.Add("TV-14", 3);
            //ratings.Add("R", 4);
            //ratings.Add("M", 4);
            //ratings.Add("GB-18", 4);
            //ratings.Add("TV-MA", 4);
            //ratings.Add("NC-17", 5);
            //ratings.Add("GB-R18", 5);
            //ratings.Add("AO", 5);
            //ratings.Add("RP", 5);
            //ratings.Add("UR", 5);
            //ratings.Add("NR", 5);
            //ratings.Add("X", 10);
            //ratings.Add("XXX", 100);
            //ratings.Add("CS", 1000);
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
            if (ratingsStrings.ContainsKey(level))
                return ratingsStrings[level];
            else return null;
        }
        public IEnumerable<string> ToString()
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
