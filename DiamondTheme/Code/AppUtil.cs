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

        //public string CalculateEndTime(string runTime)
        //{
        //    string str = runTime;
        //    if (string.IsNullOrEmpty(runTime))
        //        return "";

        //    int minutes = int.Parse(runTime.Replace(" mins", ""));
        //    DateTime time = DateTime.Now.AddMinutes((double)minutes);

        //    return time.ToString("h:mm tt");
        //    //Item x = new Item()
        //    //x.pat
        //}
        
    }
}
