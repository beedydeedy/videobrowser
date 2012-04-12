﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MediaBrowser.Library.Entities {
    public abstract class Media : BaseItem{
        protected PlaybackStatus playbackStatus;
        public virtual PlaybackStatus PlaybackStatus { get { return playbackStatus; } }
        public abstract IEnumerable<string> Files {get;}

        public override bool PlayAction(Item item)
        {
            Application.CurrentInstance.Play(item, false, false, false, false); //play with no intros
            return true;
        }

        public override bool IsPlayable
        {
            get
            {
                return true;
            }
        }

        public bool IsPlaylistCapable()
        {
            Video us = this as Video;
            if (us != null)
            {
                return !us.ContainsRippedMedia;
            }
            return true;
        }

        public virtual int RunTime
        {
            get { return 0; }
        }
    }
}
