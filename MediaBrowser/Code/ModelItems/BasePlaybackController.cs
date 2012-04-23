using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Code.ModelItems;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.RemoteControl;
using MediaBrowser.LibraryManagement;
using Microsoft.MediaCenter;
using Microsoft.MediaCenter.Hosting;
using Microsoft.MediaCenter.UI;

namespace MediaBrowser.Code.ModelItems
{
    /// <summary>
    /// Represents an abstract base class for a PlaybackController
    /// This has no knowledge of any specific player.
    /// </summary>
    public abstract class BasePlaybackController : BaseModelItem, IPlaybackController
    {
        /// <summary>
        /// Subclasses can use this to examine the items that are currently in the player.
        /// </summary>
        protected List<PlaybackArguments> CurrentPlaybackItems = new List<PlaybackArguments>();

        #region Progress EventHandler
        volatile EventHandler<PlaybackStateEventArgs> _Progress;
        public event EventHandler<PlaybackStateEventArgs> Progress
        {
            add
            {
                _Progress += value;
            }
            remove
            {
                _Progress -= value;
            }
        }
        protected void OnProgress(PlaybackStateEventArgs args)
        {
            if (_Progress != null)
            {
                _Progress(this, args);
            }
        }
        #endregion

        #region PlaybackFinished EventHandler
        volatile EventHandler<PlaybackStateEventArgs> _PlaybackFinished;
        public event EventHandler<PlaybackStateEventArgs> PlaybackFinished
        {
            add
            {
                _PlaybackFinished += value;
            }
            remove
            {
                _PlaybackFinished -= value;
            }
        }


        protected void OnPlaybackFinished(PlaybackStateEventArgs args)
        {
            CurrentPlaybackItems.Clear(); 
            
            if (_PlaybackFinished != null)
            {
                _PlaybackFinished(this, args);
            }
        }
        #endregion

        /// <summary>
        /// Plays media
        /// </summary>
        public void PlayMedia(PlaybackArguments playInfo)
        {
            CurrentPlaybackItems.Clear();
            CurrentPlaybackItems.Add(playInfo);
            PlayMediaInternal(playInfo);
        }

        /// <summary>
        /// Queues media
        /// </summary>
        public void QueueMedia(PlaybackArguments playInfo)
        {
            CurrentPlaybackItems.Add(playInfo);
            QueueMediaInternal(playInfo);
        }

        /// <summary>
        /// Stops whatever is currently playing
        /// </summary>
        public void Stop()
        {
            CurrentPlaybackItems.Clear();
            StopInternal();
        }

        public abstract void Pause();
        protected abstract void PlayMediaInternal(PlaybackArguments playInfo);
        protected abstract void StopInternal();
        public abstract void Seek(long position);
        public abstract void GoToFullScreen();
        public abstract bool CanPlay(string filename);
        public abstract bool CanPlay(IEnumerable<string> files);

        /// <summary>
        /// Queues media
        /// </summary>
        protected virtual void QueueMediaInternal(PlaybackArguments playInfo)
        {
            // We will implement this and just have it throw an exception, since not all players can queue
            // If a player can queue, it will need to override this
            throw new NotSupportedException();
        }

        /// <summary>
        /// Determines whether or not the controller is currently playing
        /// </summary>
        public virtual bool IsPlaying
        {
            get
            {
                return CurrentPlaybackItems.Count > 0;
            }
        }

        /// <summary>
        /// Determines whether or not the controller is currently playing video
        /// </summary>
        public virtual bool IsPlayingVideo
        {
            get
            {
                return IsPlaying;
            }
        }

        /// <summary>
        /// Determines whether or not the controller is currently stopped
        /// </summary>
        public virtual bool IsStopped
        {
            get
            {
                return CurrentPlaybackItems.Count == 0;
            }
        }

        /// <summary>
        /// Determines if the player is currently paused
        /// </summary>
        public virtual bool IsPaused
        {
            get
            {
                // For the majority of players there will be no way to determine this
                // Those that can should override
                return false;
            }
        }

        /// <summary>
        /// Gets the title of the currently playing media
        /// </summary>
        public virtual string NowPlayingTitle
        {
            get
            {
                if (IsPlaying)
                {
                    return FormatPathForDisplay(CurrentPlaybackItems.First().Files.First());
                }

                return "None";
            }
        }

        /// <summary>
        /// Formats a media path for display as the title
        /// </summary>
        protected virtual string FormatPathForDisplay(string path)
        {
            if (path.ToLower().StartsWith("file://"))
            {
                path = path.Substring(7);
            }

            else if (path.ToLower().StartsWith("dvd://"))
            {
                path = path.Substring(6);
            }

            int index = path.LastIndexOf('/');

            if (index != -1)
            {
                path = path.Substring(index + 1);
            }

            // Remove file extension
            index = path.LastIndexOf('.');

            if (index != -1)
            {
                path = path.Substring(0, index);
            }

            return path;
        }

        /// <summary>
        /// Processes commands
        /// </summary>
        public virtual void ProcessCommand(RemoteCommand command)
        {
            // dont do anything (only plugins need to handle this)
        }

        /// <summary>
        /// Determines if an external playback page is required
        /// </summary>
        public virtual bool RequiresExternalPage
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the item currently playing
        /// </summary>
        protected virtual PlaybackArguments GetCurrentPlaybackItem()
        {
            return CurrentPlaybackItems.FirstOrDefault();
        }

        protected override void Dispose(bool isDisposing)
        {
            Logger.ReportVerbose(GetType().Name + " is disposing");            
            
            base.Dispose(isDisposing);
        }
    }
}
