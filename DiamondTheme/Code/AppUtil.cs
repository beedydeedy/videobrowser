using System;
using System.Text;
using Microsoft.MediaCenter.UI;
using MediaBrowser.Library;

namespace Diamond
{
    public class AppUtil : ModelItem
    {
        public AppUtil()
        {
        }

        public string formatFirstAirDate(string airDate)
        {
            //DateTime dt = new DateTime();
            return DateTime.Parse(airDate).ToString("dd MMMM yyyy");
            //return dt.ToString("dd MMMM yyyy");
        }

        public string CalculateEndTime(int runningTime)
        {
            string endtime = "";
            if (runningTime > 0)
            {
                endtime = (DateTime.Now + TimeSpan.FromMinutes(runningTime)).ToShortTimeString();
            }
            return endtime;
        }
    }
}

