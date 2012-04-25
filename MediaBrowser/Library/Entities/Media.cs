using System;
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
            Application.CurrentInstance.Play(item, false, false, PlayMethod.RemotePlayButton, false); //play with no intros
            return true;
        }

        public override bool IsPlayable
        {
            get
            {
                return true;
            }
        }

        public virtual int RunTime
        {
            get { return 0; }
        }
    }
}
