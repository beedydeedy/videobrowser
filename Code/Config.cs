using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.MediaCenter.UI;
using System.IO;
using MediaBrowser.Library.Configuration;
using Microsoft.MediaCenter;

namespace Diamond
{
    public class Config : ModelItem
    {
        private ConfigData data;
        private bool isValid;

        private readonly string configFilePath = Path.Combine(ApplicationPaths.AppPluginPath, "Configurations\\Diamond.xml");
        private readonly string configFolderPath = Path.Combine(ApplicationPaths.AppPluginPath, "Configurations");

        public Config()
        {
            isValid = Load();
        }

        #region Config Options

        public bool MiniMode
        {
            get { return this.data.MiniMode; }
            set 
            {
                if (this.data.MiniMode != value) 
                {
                    this.data.MiniMode = value; 
                    Save(); 
                    FirePropertyChanged("MiniMode"); 
                } 
            }
        }

        public bool FanArtCoverflow
        {
            get { return this.data.FanArtCoverflow; }
            set
            {
                if (this.data.FanArtCoverflow != value)
                {
                    this.data.FanArtCoverflow = value;
                    Save();
                    FirePropertyChanged("FanArtCoverflow");
                }
            }
        }
        public bool FanArtDetail
        {
            get { return this.data.FanArtDetail; }
            set
            {
                if (this.data.FanArtDetail != value)
                {
                    this.data.FanArtDetail = value;
                    Save();
                    FirePropertyChanged("FanArtDetail");
                }
            }
        }
        public bool FanArtPoster
        {
            get { return this.data.FanArtPoster; }
            set
            {
                if (this.data.FanArtPoster != value)
                {
                    this.data.FanArtPoster = value;
                    Save();
                    FirePropertyChanged("FanArtPoster");
                }
            }
        }
        public bool FanArtThumb
        {
            get { return this.data.FanArtThumb; }
            set
            {
                if (this.data.FanArtThumb != value)
                {
                    this.data.FanArtThumb = value;
                    Save();
                    FirePropertyChanged("FanArtThumb");
                }
            }
        }
        public bool FanArtThumbstrip
        {
            get { return this.data.FanArtThumbstrip; }
            set
            {
                if (this.data.FanArtThumbstrip != value)
                {
                    this.data.FanArtThumbstrip = value;
                    Save();
                    FirePropertyChanged("FanArtThumbstrip");
                }
            }
        }


        #endregion


        #region Save / Load Configuration

        private void Save() 
        { 
            lock (this) this.data.Save(); 
        }

        private bool Load()
        {
            try
            {
                this.data = ConfigData.FromFile(configFilePath);
                return true;
            }
            catch (Exception ex)
            {
                MediaCenterEnvironment ev = Microsoft.MediaCenter.Hosting.AddInHost.Current.MediaCenterEnvironment; 
                DialogResult r = ev.Dialog(ex.Message + "\nReset to default?", "Error in configuration file", DialogButtons.Yes | DialogButtons.No, 600, true); 
                if (r == DialogResult.Yes)
                {
                    if (!Directory.Exists(configFolderPath)) 
                        Directory.CreateDirectory(configFolderPath); 
                    this.data = new ConfigData(configFilePath);
                    Save();
                    return true;
                }
                else 
                    return false;
            }
        }

        #endregion
    }
}
