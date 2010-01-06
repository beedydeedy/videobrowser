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
                    Save(); 
                    FirePropertyChanged("MiniMode"); 
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
