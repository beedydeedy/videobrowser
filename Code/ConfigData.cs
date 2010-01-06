using System;
using System.Collections.Generic;
using System.Text;
using MediaBrowser.Library.Persistance;

namespace Diamond
{
    [Serializable]
    internal class ConfigData
    {
        #region constructor
        public ConfigData()
        {
        }
        public ConfigData(string file)
        {
            this.file = file;
            this.settings = XmlSettings<ConfigData>.Bind(this, file);
        }
        #endregion

        public bool MiniMode = false;
        public bool FanArtCoverflow = true;
        public bool FanArtDetail = true;
        public bool FanArtPoster = true;
        public bool FanArtThumb = true;
        public bool FanArtThumbstrip = true;

        #region Load / Save Data
        public static ConfigData FromFile(string file)
        { 
            return new ConfigData(file); 
        }

        public void Save()
        { 
            this.settings.Write(); 
        }

        [SkipField]
        string file;

        [SkipField]
        XmlSettings<ConfigData> settings;
        #endregion

    }


}

