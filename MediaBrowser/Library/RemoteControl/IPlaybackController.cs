using System;
using MediaBrowser.Library.Playables;

namespace MediaBrowser.Library.RemoteControl {


    public interface IPlaybackController {
        string NowPlayingTitle { get; }
        void GoToFullScreen();
        bool IsPaused { get; }
        bool IsPlaying { get; }
        bool IsPlayingVideo { get; }
        bool IsStopped { get; }
        void PlayMedia(PlayableItem item);
        void QueueMedia(PlayableItem item);
        void Seek(long position);
        void Pause();
        void Stop();
        void ProcessCommand(RemoteCommand command);
        bool RequiresExternalPage{ get; }
        event EventHandler<PlaybackStateEventArgs> PlaybackFinished;
        event EventHandler<PlaybackStateEventArgs> Progress;
    }
}
