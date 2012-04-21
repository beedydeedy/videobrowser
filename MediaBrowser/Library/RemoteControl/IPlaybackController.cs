using System;
using System.Collections.Generic;

namespace MediaBrowser.Library.RemoteControl {


    public interface IPlaybackController {
        string NowPlayingTitle { get; }
        void GoToFullScreen();
        bool IsPaused { get; }
        bool IsPlaying { get; }
        bool IsPlayingVideo { get; }
        bool IsStopped { get; }
        void PlayMedia(PlaybackArguments playInfo);
        void QueueMedia(PlaybackArguments playInfo);
        void Seek(long position);
        void Pause();
        void Stop();
        bool CanPlay(string filename);
        void ProcessCommand(RemoteCommand command);
        bool CanPlay(IEnumerable<string> files);
        bool RequiresExternalPage{ get; }
        event EventHandler<PlaybackStateEventArgs> PlaybackFinished;
        event EventHandler<PlaybackStateEventArgs> Progress;
    }
}
