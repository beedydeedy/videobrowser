using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Playables;
using MediaBrowser.LibraryManagement;

namespace MediaBrowser.Library.Factories
{
    /// <summary>
    /// This is used to create PlayableItems
    /// </summary>
    public class PlayableItemFactory
    {
        public static PlayableItemFactory Instance = new PlayableItemFactory();

        private List<KeyValuePair<PlayableItem, Type>> RegisteredTypes = new List<KeyValuePair<PlayableItem, Type>>();
        private List<KeyValuePair<PlayableItem, Type>> RegisteredExternalPlayerTypes = new List<KeyValuePair<PlayableItem, Type>>();

        private PlayableItemFactory()
        {
            // Add the externals
            RegisterExternalPlayerTypes();
        }

        /// <summary>
        /// Registers a new type of PlayableItem to be utilized by the Create methods
        /// </summary>
        public void RegisterType<T>()
            where T : PlayableItem, new()
        {
            RegisteredTypes.Add(new KeyValuePair<PlayableItem, Type>(new T(), typeof(T)));
        }

        /// <summary>
        /// Registers a new type of PlayableExternal to be utilized by the Create methods AND show up in the extenral player section of the configurator
        /// </summary>
        public void RegisterExternalPlayerType<T>()
            where T : PlayableItem, new()
        {
            RegisteredExternalPlayerTypes.Add(new KeyValuePair<PlayableItem, Type>(new T(), typeof(T)));
        }

        private void RegisterExternalPlayerTypes()
        {
            RegisterExternalPlayerType<PlayableMpcHc>();
            RegisterExternalPlayerType<PlayableVLC>();
            RegisterExternalPlayerType<PlayableTMT>();
            RegisterExternalPlayerType<PlayableTMTAddInForWMC>();
            RegisterExternalPlayerType<PlayableExternal>();
        }

        /// <summary>
        /// Creates a PlayableItem based on a Media object
        /// </summary>
        public PlayableItem Create(Media media)
        {
            return Create(new Media[] { media });
        }

        /// <summary>
        /// Creates a PlayableItem based on a list of files
        /// </summary>
        public PlayableItem Create(IEnumerable<string> paths)
        {
            PlayableItem playable = null;

            foreach (KeyValuePair<PlayableItem, Type> type in GetAllKnownTypes())
            {
                if (type.Key.CanPlay(paths))
                {
                    playable = InstantiatePlayableItem(type);
                    break;
                }
            }

            if (playable == null) playable = GetDefaultPlayableItem();
            playable.AddMedia(paths);
            return playable;
        }

        /// <summary>
        /// Creates a PlayableItem based on a list of Media objects
        /// </summary>
        public PlayableItem Create(IEnumerable<Media> mediaList)
        {
            foreach (Media media in mediaList)
            {
                Video video = media as Video;

                if (video != null && video.MediaType == MediaType.ISO && !CanPlayIsoDirectly(video))
                {
                    MountAndUpdateMediaPath(video);
                }
            }
            
            PlayableItem playable = null;

            foreach (KeyValuePair<PlayableItem, Type> type in GetAllKnownTypes())
            {
                if (type.Key.CanPlay(mediaList))
                {
                    playable = InstantiatePlayableItem(type);
                    break;
                }
            }

            // Return default
            if (playable == null) playable = GetDefaultPlayableItem();
            playable.AddMedia(mediaList);
            return playable;
        }

        /// <summary>
        /// Creates a PlayableItem based on a media path
        /// </summary>
        public PlayableItem Create(string path)
        {
            return Create(new string[] { path });
        }

        /// <summary>
        /// Creates a PlayableItem based on a Folder object
        /// </summary>
        public PlayableItem Create(Folder folder) 
        {
            PlayableItem playable = Create(folder.RecursiveMedia);

            playable.Folder = folder;

            return playable;
        }

        private List<KeyValuePair<PlayableItem, Type>> GetAllKnownTypes()
        {
            List<KeyValuePair<PlayableItem, Type>> types = new List<KeyValuePair<PlayableItem, Type>>();

            types.AddRange(RegisteredTypes);

            types.AddRange(GetConfiguredExternalPlayerTypes());

            types.Add(new KeyValuePair<PlayableItem, Type>(new PlayableInternal(), typeof(PlayableInternal)));

            return types;
        }

        private List<KeyValuePair<PlayableItem, Type>> GetConfiguredExternalPlayerTypes()
        {
            List<KeyValuePair<PlayableItem, Type>> types = new List<KeyValuePair<PlayableItem, Type>>();

            // Important - need to register them in the order they are added in configuration
            foreach (ConfigData.ExternalPlayer externalPlayerConfiguration in Config.Instance.ExternalPlayers)
            {
                if (externalPlayerConfiguration.ExternalPlayerType == ConfigData.ExternalPlayerType.MpcHc)
                {
                    types.Add(new KeyValuePair<PlayableItem, Type>(new PlayableMpcHc() { ExternalPlayerConfiguration = externalPlayerConfiguration }, typeof(PlayableMpcHc)));
                }
                else if (externalPlayerConfiguration.ExternalPlayerType == ConfigData.ExternalPlayerType.TMT)
                {
                    types.Add(new KeyValuePair<PlayableItem, Type>(new PlayableTMT() { ExternalPlayerConfiguration = externalPlayerConfiguration }, typeof(PlayableTMT)));
                }
                else if (externalPlayerConfiguration.ExternalPlayerType == ConfigData.ExternalPlayerType.TMTAddInForWMC)
                {
                    types.Add(new KeyValuePair<PlayableItem, Type>(new PlayableTMTAddInForWMC() { ExternalPlayerConfiguration = externalPlayerConfiguration }, typeof(PlayableTMTAddInForWMC)));
                }
                else if (externalPlayerConfiguration.ExternalPlayerType == ConfigData.ExternalPlayerType.VLC)
                {
                    types.Add(new KeyValuePair<PlayableItem, Type>(new PlayableVLC() { ExternalPlayerConfiguration = externalPlayerConfiguration }, typeof(PlayableVLC)));
                }
                else
                {
                    types.Add(new KeyValuePair<PlayableItem, Type>(new PlayableExternal() { ExternalPlayerConfiguration = externalPlayerConfiguration }, typeof(PlayableExternal)));
                }
            }

            return types;
        }

        private PlayableItem GetDefaultPlayableItem()
        {
            return new PlayableInternal();
        }

        private PlayableItem InstantiatePlayableItem(KeyValuePair<PlayableItem, Type> type)
        {
            PlayableItem playable = (PlayableItem)Activator.CreateInstance(type.Value);

            // Attach configuration if it's an external player
            if (type.Key is PlayableExternal)
            {
                (playable as PlayableExternal).ExternalPlayerConfiguration = (type.Key as PlayableExternal).ExternalPlayerConfiguration;
            }

            return playable;
        }

        private bool CanPlayIsoDirectly(Video video)
        {
            return GetAllKnownTypes().Where(t => t.Key.CanPlay(video)).Count() > 0;
        }

        private void MountAndUpdateMediaPath(Video video)
        {
            string mountedPath = Application.CurrentInstance.MountISO(video.IsoFiles.First());
            video.Path = mountedPath;

            video.MediaType = MediaTypeResolver.DetermineType(mountedPath);
            video.DisplayMediaType = video.MediaType.ToString();
        }
    }
}
