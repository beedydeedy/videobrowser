using System;
using System.Text;
using Microsoft.MediaCenter.UI;

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

        
    }
}
