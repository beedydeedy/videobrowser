using System;
using System.Collections.Generic;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Playables;

namespace MediaBrowser.Library.Factories
{
    /// <summary>
    /// This is used to create PlayableItems
    /// </summary>
    public class PlayableItemFactory
    {
        public static PlayableItemFactory Instance = new PlayableItemFactory();

        private List<KeyValuePair<PlayableItem, Type>> RegisteredTypes = new List<KeyValuePair<PlayableItem, Type>>();

        private PlayableItemFactory()
        {
            // Add the externals
            RegisterExternals();

            RegisterType<PlayableIso>();
            RegisterType<PlayableInternal>();
        }

        /// <summary>
        /// Registers a new type of PlayableItem to be utilized by the Create methods
        /// </summary>
        public void RegisterType<T>()
            where T : PlayableItem, new()
        {
            RegisterType<T>(false);
        }

        /// <summary>
        /// Registers a new type of PlayableItem to be utilized by the Create methods
        /// </summary>
        public void RegisterType<T>(bool prioritize)
            where T : PlayableItem, new()
        {
            RegisterType(new T(), prioritize);
        }

        /// <summary>
        /// Registers a new type of PlayableItem to be utilized by the Create methods
        /// </summary>
        private void RegisterType<T>(T playable, bool prioritize)
            where T : PlayableItem, new()
        {
            if (prioritize)
            {
                RegisteredTypes.Insert(0, new KeyValuePair<PlayableItem, Type>(playable, typeof(T)));
            }
            else
            {
                RegisteredTypes.Add(new KeyValuePair<PlayableItem, Type>(playable, typeof(T)));
            }
        }

        private void RegisterExternals()
        {
            // Important - need to register them in the order they are added in configuration
            foreach (ConfigData.ExternalPlayer externalPlayer in Config.Instance.ExternalPlayers)
            {
                if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.MpcHc)
                {
                    RegisterType(new PlayableMpcHc() { ExternalPlayerConfiguration = externalPlayer }, false);
                }
                else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.TMT)
                {
                    RegisterType(new PlayableTMT() { ExternalPlayerConfiguration = externalPlayer }, false);
                }
                else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.TMTAddInForWMC)
                {
                    RegisterType(new PlayableTMTAddInForWMC() { ExternalPlayerConfiguration = externalPlayer }, false);
                }
                else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.VLC)
                {
                    RegisterType(new PlayableVLC() { ExternalPlayerConfiguration = externalPlayer }, false);
                }
                else
                {
                    RegisterType(new PlayableExternal() { ExternalPlayerConfiguration = externalPlayer }, false);
                }
            }
        }

        /// <summary>
        /// Creates a PlayableItem based on a Media object
        /// </summary>
        public PlayableItem Create(Media media)
        {
            PlayableItem playable = null;

            foreach (KeyValuePair<PlayableItem, Type> type in RegisteredTypes)
            {
                if (type.Key.CanPlay(media))
                {
                    playable = InstantiatePlayableItem(type);
                    break;
                }
            }

            if (playable == null) playable = GetDefaultPlayableItem();
            playable.AddMedia(media);
            return playable;
        }

        /// <summary>
        /// Creates a PlayableItem based on a list of files
        /// </summary>
        public PlayableItem Create(IEnumerable<string> paths)
        {
            PlayableItem playable = null;

            foreach (KeyValuePair<PlayableItem, Type> type in RegisteredTypes)
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
            PlayableItem playable = null;

            foreach (KeyValuePair<PlayableItem, Type> type in RegisteredTypes)
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
            PlayableItem playable = null;

            foreach (KeyValuePair<PlayableItem, Type> type in RegisteredTypes)
            {
                if (type.Key.CanPlay(path))
                {
                    playable = InstantiatePlayableItem(type);
                    break;
                }
            }

            // Return default
            if (playable == null) playable = GetDefaultPlayableItem();
            playable.AddMedia(path);
            return playable;
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
    }
}
