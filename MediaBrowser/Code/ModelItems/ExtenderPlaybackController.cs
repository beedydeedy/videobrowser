using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using MediaBrowser.Library.Configuration;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Playables;
using MediaBrowser.LibraryManagement;

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

        protected override PlayableItem GetCurrentPlaybackItemFromPlayerState(string metadataTitle, out int filePlaylistPosition, out int currentMediaIndex)
        {
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

        public static string CreateWPLPlaylist(string name, IEnumerable<string> files)
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
                xml.WriteStartElement("media");
                xml.WriteAttributeString("src", file);
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
