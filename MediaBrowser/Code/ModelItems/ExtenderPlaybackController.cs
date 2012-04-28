using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                string file = CreateWPLPlaylist(playable.Id.ToString(), playable.Files);
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

            filePlaylistPosition = 0;
            currentMediaIndex = 0;

            metadataTitle = metadataTitle.ToLower();

            foreach (PlayableItem playable in CurrentPlayableItems)
            {
                for (int i = 0; i < playable.Files.Count(); i++)
                {
                    string file = playable.Files.ElementAt(i).ToLower();
                    string normalized = file.Replace('\\', '/');

                    if (metadataTitle.EndsWith(normalized) || metadataTitle == Path.GetFileNameWithoutExtension(file))
                    {
                        filePlaylistPosition = i;
                        return playable;
                    }
                }
            }

            return null;
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
    }
}
