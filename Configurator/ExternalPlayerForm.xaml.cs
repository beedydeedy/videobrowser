using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MediaBrowser;
using MediaBrowser.Library;
using MediaBrowser.Library.Playables;

namespace Configurator
{
    /// <summary>
    /// Interaction logic for ExternalPlayerForm.xaml
    /// </summary>
    public partial class ExternalPlayerForm : Window
    {
        public ExternalPlayerForm(bool isNew)
        {
            InitializeComponent();
            PopulateControls();

            // Set title
            if (isNew)
            {
                Title = "Add External Player";
            }
            else
            {
                Title = "Edit External Player";
            }

            lstPlayerType.SelectionChanged += new SelectionChangedEventHandler(lstPlayerType_SelectionChanged);
            lstLaunchType.SelectionChanged += new SelectionChangedEventHandler(lstLaunchType_SelectionChanged);
            btnCommand.Click += new RoutedEventHandler(btnCommand_Click);
            lnkSelectAllMediaTypes.Click += new RoutedEventHandler(lnkSelectAllMediaTypes_Click);
            lnkSelectAllVideoFormats.Click += new RoutedEventHandler(lnkSelectAllVideoFormats_Click);
            lnkSelectNoneMediaTypes.Click += new RoutedEventHandler(lnkSelectNoneMediaTypes_Click);
            lnkSelectNoneVideoFormats.Click += new RoutedEventHandler(lnkSelectNoneVideoFormats_Click);
        }

        void lnkSelectNoneVideoFormats_Click(object sender, RoutedEventArgs e)
        {
            EnumWrapperList<VideoFormat> source = lstVideoFormats.ItemsSource as EnumWrapperList<VideoFormat>;

            source.SelectAll(false);
            lstVideoFormats.ItemsSource = null;
            lstVideoFormats.ItemsSource = source;
        }   

        void lnkSelectNoneMediaTypes_Click(object sender, RoutedEventArgs e)
        {
            EnumWrapperList<MediaType> source = lstMediaTypes.ItemsSource as EnumWrapperList<MediaType>;

            source.SelectAll(false);
            lstMediaTypes.ItemsSource = null;
            lstMediaTypes.ItemsSource = source;
        }

        void lnkSelectAllVideoFormats_Click(object sender, RoutedEventArgs e)
        {
            EnumWrapperList<VideoFormat> source = lstVideoFormats.ItemsSource as EnumWrapperList<VideoFormat>;

            source.SelectAll(true);
            lstVideoFormats.ItemsSource = null;
            lstVideoFormats.ItemsSource = source;
        }

        void lnkSelectAllMediaTypes_Click(object sender, RoutedEventArgs e)
        {
            EnumWrapperList<MediaType> source = lstMediaTypes.ItemsSource as EnumWrapperList<MediaType>;

            source.SelectAll(true);
            lstMediaTypes.ItemsSource = null;
            lstMediaTypes.ItemsSource = source;
        }

        private ConfigData.ExternalPlayerType ExternalPlayerType
        {
            get
            {
                return (ConfigData.ExternalPlayerType)lstPlayerType.SelectedItem;
            }
        }

        private ConfigData.ExternalPlayerLaunchType ExternalPlayerLaunchType
        {
            get
            {
                return (ConfigData.ExternalPlayerLaunchType)lstLaunchType.SelectedItem;
            }
        }

        void btnCommand_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.OpenFileDialog();

            if (!string.IsNullOrEmpty(txtCommand.Text))
            {
                dialog.FileName = txtCommand.Text;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtCommand.Text = dialog.FileName;
            }
        }

        void lstPlayerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ConfigData.ExternalPlayer externalPlayer = null;

            if (ExternalPlayerType == ConfigData.ExternalPlayerType.MpcHc) externalPlayer = new PlayableMpcHc().GetDefaultConfiguration();
            else if (ExternalPlayerType == ConfigData.ExternalPlayerType.TMT) externalPlayer = new PlayableTMT().GetDefaultConfiguration();
            else if (ExternalPlayerType == ConfigData.ExternalPlayerType.TMTAddInForWMC) externalPlayer = new PlayableTMTAddInForWMC().GetDefaultConfiguration();
            else if (ExternalPlayerType == ConfigData.ExternalPlayerType.VLC) externalPlayer = new PlayableVLC().GetDefaultConfiguration();
            else externalPlayer = new PlayableExternal().GetDefaultConfiguration();

            FillControlsFromObject(externalPlayer);
        }

        void lstLaunchType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            chkMinimizeMce.Visibility = ExternalPlayerLaunchType == ConfigData.ExternalPlayerLaunchType.CommandLine ? Visibility.Visible : Visibility.Hidden;
            chkShowSplashScreen.Visibility = ExternalPlayerLaunchType == ConfigData.ExternalPlayerLaunchType.CommandLine ? Visibility.Visible : Visibility.Hidden;
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateUserInput())
            {
                return;
            }

            this.DialogResult = true;
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void PopulateControls()
        {
            lstPlayerType.ItemsSource = Enum.GetValues(typeof(ConfigData.ExternalPlayerType));
            lstLaunchType.ItemsSource = Enum.GetValues(typeof(ConfigData.ExternalPlayerLaunchType));

            lstMediaTypes.ItemsSource = EnumWrapperList<MediaType>.Create();
            lstVideoFormats.ItemsSource = EnumWrapperList<VideoFormat>.Create();
        }

        public void FillControlsFromObject(ConfigData.ExternalPlayer externalPlayer)
        {
            lstPlayerType.SelectedItem = externalPlayer.ExternalPlayerType;
            lstLaunchType.SelectedItem = externalPlayer.LaunchType;

            txtArguments.Text = externalPlayer.Args;
            txtCommand.Text = externalPlayer.Command;

            chkMinimizeMce.IsChecked = externalPlayer.MinimizeMCE;
            chkShowSplashScreen.IsChecked = externalPlayer.ShowSplashScreen;
            chkSupportsMultiFileCommand.IsChecked = externalPlayer.SupportsMultiFileCommandArguments;
            chkSupportsPLS.IsChecked = externalPlayer.SupportsPlaylists;

            (lstMediaTypes.ItemsSource as EnumWrapperList<MediaType>).SetValues(externalPlayer.MediaTypes);
            (lstVideoFormats.ItemsSource as EnumWrapperList<VideoFormat>).SetValues(externalPlayer.VideoFormats);

            SetControlVisibility(externalPlayer);
            SetTips(externalPlayer);

            AutoFillPaths(externalPlayer);
        }

        public void UpdateObjectFromControls(ConfigData.ExternalPlayer externalPlayer)
        {
            externalPlayer.ExternalPlayerType = (ConfigData.ExternalPlayerType)lstPlayerType.SelectedItem;
            externalPlayer.LaunchType = (ConfigData.ExternalPlayerLaunchType)lstLaunchType.SelectedItem;

            externalPlayer.Args = txtArguments.Text;
            externalPlayer.Command = txtCommand.Text;

            externalPlayer.MinimizeMCE = chkMinimizeMce.IsChecked.Value;
            externalPlayer.ShowSplashScreen = chkShowSplashScreen.IsChecked.Value;
            externalPlayer.SupportsMultiFileCommandArguments = chkSupportsMultiFileCommand.IsChecked.Value;
            externalPlayer.SupportsPlaylists = chkSupportsPLS.IsChecked.Value;

            externalPlayer.MediaTypes = (lstMediaTypes.ItemsSource as EnumWrapperList<MediaType>).GetValues();
            externalPlayer.VideoFormats = (lstVideoFormats.ItemsSource as EnumWrapperList<VideoFormat>).GetValues();
        }

        private void SetControlVisibility(ConfigData.ExternalPlayer externalPlayer)
        {
            if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.Generic)
            {
                lblLaunchType.Visibility = System.Windows.Visibility.Visible;
                lstLaunchType.Visibility = System.Windows.Visibility.Visible;

                lblArguments.Visibility = System.Windows.Visibility.Visible;
                txtArguments.Visibility = System.Windows.Visibility.Visible;

                chkMinimizeMce.Visibility = System.Windows.Visibility.Visible;
                chkShowSplashScreen.Visibility = System.Windows.Visibility.Visible;
                chkSupportsMultiFileCommand.Visibility = System.Windows.Visibility.Visible;
                chkSupportsPLS.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                lblLaunchType.Visibility = System.Windows.Visibility.Hidden;
                lstLaunchType.Visibility = System.Windows.Visibility.Hidden;

                lblArguments.Visibility = System.Windows.Visibility.Hidden;
                txtArguments.Visibility = System.Windows.Visibility.Hidden;

                chkMinimizeMce.Visibility = System.Windows.Visibility.Hidden;
                chkShowSplashScreen.Visibility = System.Windows.Visibility.Hidden;
                chkSupportsMultiFileCommand.Visibility = System.Windows.Visibility.Hidden;
                chkSupportsPLS.Visibility = System.Windows.Visibility.Hidden;
            }
        }

        private void SetTips(ConfigData.ExternalPlayer externalPlayer)
        {
            txtCommand.ToolTip = btnCommand.ToolTip = "The path to the player's executable file."; 
            
            if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.MpcHc)
            {
                lblTipsHeader.Content = "MPC-HC Tips:";
                txtTips.Text = "Please enable the following settings in MPC-HC: \"Keep history of recently opened files\", \"Always on top\" and \"Don't use search in folder on commands skip back/forward when only one item in playlist\". You will also need to map \"MEDIA_STOP\" to the \"exit\" command.";
            }
            else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.TMT)
            {
                lblTipsHeader.Content = "TMT Tips:";
                txtTips.Text = "You will need to enable \"auto-fullscreen\". There is no resume support at this time. There is no multi-part movie or folder-based playback support at this time.";
                txtCommand.ToolTip = btnCommand.ToolTip = "The path to uTotalMediaTheatre5.exe within the TMT installation directory.";
            }
            else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.TMTAddInForWMC)
            {
                lblTipsHeader.Content = "TMT for WMC Tips:";
                txtTips.Text = "You will need to enable \"auto-fullscreen\". There is no resume support at this time. There is no multi-part movie or folder-based playback support at this time.";
                txtCommand.ToolTip = btnCommand.ToolTip = "The path to PlayerLoader.htm within the TMT installation directory.";
            }
            else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.VLC)
            {
                lblTipsHeader.Content = "VLC Tips:";
                txtTips.Text = "Version 2.0+ required. No special configuration is required.";               
            }
            else
            {
                lblTipsHeader.Content = "Tips:";
                txtTips.Text = "If your player has settings for \"always on top\", \"auto-fullscreen\", and \"exit after stopping\", it is recommended to enable them.";
            }
        }

        private bool ValidateUserInput()
        {
            // Validate Player Path
            if (!IsPathValid(txtCommand.Text))
            {
                MessageBox.Show("Please enter a valid player path.");
                return false;
            }

            if ((lstMediaTypes.ItemsSource as EnumWrapperList<MediaType>).GetCheckedValues().Count == 0)
            {
                MessageBox.Show("Please select at least one media type.");
                return false;
            }

            if ((lstVideoFormats.ItemsSource as EnumWrapperList<VideoFormat>).GetCheckedValues().Count == 0)
            {
                MessageBox.Show("Please select at least one video format.");
                return false;
            }

            return true;
        }

        private bool IsPathValid(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (Path.IsPathRooted(path) && !File.Exists(path))
            {
                return false;
            }

            return true;
        }

        private void AutoFillPaths(ConfigData.ExternalPlayer externalPlayer)
        {
            if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.MpcHc)
            {
                AutoFillProgramFilesPath(txtCommand, "Media Player Classic - Home Cinema\\mpc-hc.exe");
            }
            else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.TMT)
            {
                AutoFillProgramFilesPath(txtCommand, "ArcSoft\\TotalMedia Theatre 5\\uTotalMediaTheatre5.exe");
            }
            else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.TMTAddInForWMC)
            {
                AutoFillProgramFilesPath(txtCommand, "ArcSoft\\TotalMedia Theatre 5\\PlayerLoader.htm");
            }
            else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.VLC)
            {
                AutoFillProgramFilesPath(txtCommand, "VideoLAN\\VLC\\vlc.exe");
            }
        }

        private void AutoFillProgramFilesPath(TextBox textBox, string pathSuffix)
        {
            string path1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), pathSuffix);
            string path2 = Path.Combine(GetProgramFilesx86Path(), pathSuffix);

            AutoFillPath(txtCommand, new string[] { path1, path2 });
        }

        private void AutoFillPath(TextBox textBox, IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    textBox.Text = path;
                    break;
                }
            }
        }

        private static string GetProgramFilesx86Path()
        {
            if (8 == IntPtr.Size || (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432"))))
            {
                return Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            }

            return Environment.GetEnvironmentVariable("ProgramFiles");
        }

        private class EnumWrapper<TEnumType>
        {
            public TEnumType Value { get; set; }
            public bool IsChecked { get; set; }
        }

        private class EnumWrapperList<TEnumType> : List<EnumWrapper<TEnumType>>
        {
            public static EnumWrapperList<TEnumType> Create()
            {
                EnumWrapperList<TEnumType> list = new EnumWrapperList<TEnumType>();

                foreach (TEnumType val in Enum.GetValues(typeof(TEnumType)))
                {
                    list.Add(new EnumWrapper<TEnumType>() { Value = val, IsChecked = false });
                }

                return list;
            }

            public List<TEnumType> GetCheckedValues()
            {
                List<TEnumType> values = new List<TEnumType>();

                foreach (EnumWrapper<TEnumType> wrapper in this)
                {
                    if (wrapper.IsChecked)
                    {
                        values.Add(wrapper.Value);
                    }
                }

                return values;
            }

            public List<TEnumType> GetValues()
            {
                List<TEnumType> values = GetCheckedValues();

                if (values.Count == Count)
                {
                    values.Clear();
                }

                return values;
            }

            public void SetValues(List<TEnumType> values)
            {
                foreach (EnumWrapper<TEnumType> wrapper in this)
                {
                    wrapper.IsChecked = values.Count == 0 || values.Contains(wrapper.Value);
                }
            }

            public void SelectAll(bool selected)
            {
                foreach (EnumWrapper<TEnumType> wrapper in this)
                {
                    wrapper.IsChecked = selected;
                }
            }
        }

    }
}
