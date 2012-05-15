using System;
using System.Collections.Generic;
using Microsoft.MediaCenter.UI;
using Microsoft.MediaCenter.Hosting;
using Microsoft.MediaCenter;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;
using MediaBrowser;
using MusicPlugin.LibraryManagement;


namespace MusicPlugin
{

    public class PlaybackControllerMusic : PlaybackController, IPlaybackController
    {
        public PlaybackControllerMusic()
            : base()
        { }

        #region IPlaybackController Members

        public override bool CanPlay(string filename)
        {
            if (filename == null)
                return false;
            return MusicHelper.IsMusic(filename);
        }

        public override bool CanPlay(IEnumerable<string> files)
        {
            if (files == null)
                return false;

            foreach (string file in files)
                if (CanPlay(file))
                    return true;

            return false;
        }
        #endregion

        protected override bool MonitorMediaTransportPropertyChanged
        {
            get
            {
                return false;
            }
        }
    }
}