using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using MediaBrowser.Library.Configuration;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Playables;
using MediaBrowser.LibraryManagement;
using Microsoft.MediaCenter;
using Microsoft.MediaCenter.Hosting;

namespace MediaBrowser.Code.ModelItems
{
    /// <summary>
    /// Represents a PlaybackController for extenders
    /// </summary>
    public class ExtenderPlaybackController : PlaybackController
    {
        static MediaBrowser.Library.Transcoder transcoder;

        internal override IEnumerable<string> GetPlayableFiles(Media media)
        {
            if (!Config.Instance.EnableTranscode360)
            {
                return base.GetPlayableFiles(media);
            }

            return base.GetPlayableFiles(media).Select(f => GetTranscodedPath(f));
        }

        private string GetTranscodedPath(string path)
        {
            // Can't transcode this
            if (Directory.Exists(path))
            {
                return path;
            }

            if (Helper.IsExtenderNativeVideo(path))
            {
                return path;
            }
            else
            {
                if (transcoder == null)
                {
                    transcoder = new MediaBrowser.Library.Transcoder();
                }

                string bufferpath = transcoder.BeginTranscode(path);

                // if bufferpath comes back null, that means the transcoder i) failed to start or ii) they
                // don't even have it installed
                if (string.IsNullOrEmpty(bufferpath))
                {
                    Application.DisplayDialog("Could not start transcoding process", "Transcode Error");
                    throw new Exception("Could not start transcoding process");
                }

                return bufferpath;
            }
        }

        protected override void PlayPaths(PlayableItem playable)
        {
            if (playable.QueueItem)
            {
                Queue(playable);
                return;
            }

            // Need to create a playlist
            if (RequiresWPL(playable))
            {
                IEnumerable<string> files = playable.Files;

                if (playable.Resume)
                {
                    files = files.Skip(playable.MediaItems.First().PlaybackStatus.PlaylistPosition);
                }

                string file = CreateWPLPlaylist(playable.Id.ToString(), files);
                Microsoft.MediaCenter.MediaType type = Helper.IsVideo(playable.Files.First()) ? Microsoft.MediaCenter.MediaType.Video : Microsoft.MediaCenter.MediaType.Audio;
                CallPlayMedia(type, file, false);
            }
            else if (playable.HasMediaItems)
            {
                // Play single media item
                string file = GetPlayableFiles(playable.MediaItems.First()).First();
                Microsoft.MediaCenter.MediaType type = playable.MediaItems.First() is Video ? Microsoft.MediaCenter.MediaType.Video : Microsoft.MediaCenter.MediaType.Audio;
                CallPlayMedia(type, file, false);
            }
            else
            {
                // Play single file
                string file = playable.Files.First();
                Microsoft.MediaCenter.MediaType type = Helper.IsVideo(file) ? Microsoft.MediaCenter.MediaType.Video : Microsoft.MediaCenter.MediaType.Audio;

                if (Config.Instance.EnableTranscode360)
                {
                    file = GetTranscodedPath(file);
                }

                CallPlayMedia(type, file, false);
            }

            if (playable.GoFullScreen)
            {
                GoToFullScreen();
            }

            if (playable.Resume)
            {
                long position = playable.MediaItems.First().PlaybackStatus.PositionTicks;

                if (position > 0)
                {
                    Seek(position);
                }
            }

            // Get this again as I've seen issues where it gets reset after the above call
            var mediaExperience = MediaExperience ?? GetMediaExperienceUsingReflection();

            // Attach event handler
            MediaTransport transport = mediaExperience.Transport;

            transport.PropertyChanged -= MediaTransport_PropertyChanged;
            transport.PropertyChanged += MediaTransport_PropertyChanged;

        }

        private bool RequiresWPL(PlayableItem playable)
        {
            if (playable.HasMediaItems)
            {
                if (playable.MediaItems.Count() > 1)
                {
                    return true;
                }

                return playable.MediaItems.First().Files.Count() > 1;
            }

            return playable.Files.Count() > 1;
        }

        private void Queue(PlayableItem playable)
        {
            if (playable.HasMediaItems)
            {
                foreach (Media media in playable.MediaItems)
                {
                    Microsoft.MediaCenter.MediaType type = media is Video ? Microsoft.MediaCenter.MediaType.Video : Microsoft.MediaCenter.MediaType.Audio;

                    foreach (string file in GetPlayableFiles(media))
                    {
                        CallPlayMedia(type, file, true);
                    }
                }
            }
            else
            {
                foreach (string file in playable.Files)
                {
                    Microsoft.MediaCenter.MediaType type = Helper.IsVideo(file) ? Microsoft.MediaCenter.MediaType.Video : Microsoft.MediaCenter.MediaType.Audio;

                    string fileToPlay = Config.Instance.EnableTranscode360 ? GetTranscodedPath(file) : file;

                    CallPlayMedia(type, fileToPlay, true);
                }
            }
        }

        private void CallPlayMedia(Microsoft.MediaCenter.MediaType type, string path, bool queue)
        {
            MediaCenterEnvironment mediaCenterEnvironment = AddInHost.Current.MediaCenterEnvironment;

            if (!mediaCenterEnvironment.PlayMedia(type, path, queue))
            {
                Logger.ReportInfo("PlayMedia returned false");
            }
        }

        protected override PlayableItem GetCurrentPlaybackItemFromPlayerState(MediaMetadata metadata, out int filePlaylistPosition, out int currentMediaIndex)
        {
            string metadataTitle = GetTitleOfCurrentlyPlayingMedia(metadata);

            filePlaylistPosition = -1;
            currentMediaIndex = -1;

            metadataTitle = metadataTitle.ToLower();

            // Loop through each PlayableItem and try to find a match
            foreach (PlayableItem playable in CurrentPlayableItems)
            {
                if (playable.HasMediaItems)
                {
                    // The PlayableItem has Media items, so loop through each one and look for a match

                    int totalFileCount = 0;
                    int numMediaItems = playable.MediaItems.Count();

                    for (int i = 0; i < numMediaItems; i++)
                    {
                        Media media = playable.MediaItems.ElementAt(i);

                        IEnumerable<string> files = GetPlayableFiles(media);

                        int index = GetPlaylistIndex(files, metadataTitle);

                        if (index != -1)
                        {
                            filePlaylistPosition = index + totalFileCount;
                            currentMediaIndex = i;
                            return playable;
                        }

                        totalFileCount += files.Count();
                    }
                }
                else
                {
                    // There are no Media items so just find the index using the Files property
                    int index = GetPlaylistIndex(playable.Files, metadataTitle);

                    if (index != -1)
                    {
                        filePlaylistPosition = index;
                        return playable;
                    }
                }
            }

            return null;
        }

        private int GetPlaylistIndex(IEnumerable<string> files, string metadataTitle)
        {
            int numFiles = files.Count();

            for (int i = 0; i < numFiles; i++)
            {
                string file = files.ElementAt(i).ToLower();
                string normalized = file.Replace('\\', '/');

                if (metadataTitle.EndsWith(normalized) || metadataTitle == Path.GetFileNameWithoutExtension(file))
                {
                    return i;
                }
            }

            return -1;
        }

        private string CreateWPLPlaylist(string name, IEnumerable<string> files)
        {

            // we need to filter out all invalid chars 
            name = new string(name
                .ToCharArray()
                .Where(e => !Path.GetInvalidFileNameChars().Contains(e))
                .ToArray());

            var playListFile = Path.Combine(ApplicationPaths.AutoPlaylistPath, name + ".wpl");


            StringWriter writer = new StringWriter();
            XmlTextWriter xml = new XmlTextWriter(writer);

            xml.Indentation = 2;
            xml.IndentChar = ' ';

            xml.WriteStartElement("smil");
            xml.WriteStartElement("body");
            xml.WriteStartElement("seq");

            foreach (string file in files)
            {
                string fileToPlay = Config.Instance.EnableTranscode360 ? GetTranscodedPath(file) : file;

                xml.WriteStartElement("media");
                xml.WriteAttributeString("src", fileToPlay);
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();

            File.WriteAllText(playListFile, @"<?wpl version=""1.0""?>" + writer.ToString());

            return playListFile;
        }

        public override void Seek(long position)
        {
            var mce = AddInHost.Current.MediaCenterEnvironment;
            Logger.ReportVerbose("Trying to seek position :" + new TimeSpan(position).ToString());
            WaitForStream(mce);
            mce.MediaExperience.Transport.Position = new TimeSpan(position);
        }

        private static void WaitForStream(MediaCenterEnvironment mce)
        {
            int i = 0;
            while ((i++ < 15) && (mce.MediaExperience.Transport.PlayState != Microsoft.MediaCenter.PlayState.Playing))
            {
                // settng the position only works once it is playing and on fast multicore machines we can get here too quick!
                Thread.Sleep(100);
            }
        }
    }
}
