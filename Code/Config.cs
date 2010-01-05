using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.MediaCenter.UI;

namespace Diamond
{
    public class Config : ModelItem
    {
        private ConfigData data;

        public bool JasBool
        {
            get { return true; }
            //set { if (this.data.AlwaysShowDetailsPage != value) { this.data.AlwaysShowDetailsPage = value; Save(); FirePropertyChanged("AlwaysShowDetailsPage"); } }
        }

        public bool MiniMode
        {
            get { return this.data.MiniMode; }
            set 
            {
                if (this.data.MiniMode != value) 
                {
                    this.data.MiniMode = value; 
                    //Save(); 
                    FirePropertyChanged("MiniMode"); 
                } 
            }
        }



        private static Config _instance = new Config();
        public static Config Instance
        {
            get
            {
                return _instance;
            }
        }


        public Config()
        {
            this.data = new ConfigData();
        }
    }
}
