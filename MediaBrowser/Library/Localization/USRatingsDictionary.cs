using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MediaBrowser.Library.Localization
{
    public class USRatingsDictionary : Dictionary<string,int>
    {
        public USRatingsDictionary()
        {
            this.Add("G", 1);
            this.Add("E", 1);
            this.Add("EC", 1);
            this.Add("TV-G", 1);
            this.Add("TV-Y", 2);
            this.Add("TV-Y7", 3);
            this.Add("TV-Y7-FV", 4);
            this.Add("PG", 5);
            this.Add("TV-PG", 5);
            this.Add("PG-13", 6);
            this.Add("T", 6);
            this.Add("TV-14", 7);
            this.Add("R", 8);
            this.Add("M", 8);
            this.Add("TV-MA", 8);
            this.Add("NC-17", 9);
            this.Add("AO", 10);
            this.Add("RP", 10);
            this.Add("UR", 10);
            this.Add("NR", 10);
            this.Add("X", 10);
            this.Add("XXX", 100);
        }
    }
}
