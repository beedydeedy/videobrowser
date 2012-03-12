using System;
using System.Collections.Generic;
using System.Linq;
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

        private List<Type> RegisteredTypes = new List<Type>();

        private PlayableItemFactory()
        {
            // Add the externals
            RegisterExternals();

            RegisterType<PlayableIso>();
            RegisterType<PlayableDvd>();
            RegisterType<PlayableMultiMediaVideo>();
            RegisterType<PlayableVideo>();
        }

        /// <summary>
        /// Registers a new type of PlayableItem to be utilized by the Create methods
        /// </summary>
        public void RegisterType<T>()
            where T : PlayableItem, new()
        {
            RegisteredTypes.Add(typeof(T));
        }

        private void RegisterExternals()
        {
            // Important - need to register them in the order they are added in configuration
            foreach (ConfigData.ExternalPlayer externalPlayer in Config.Instance.ExternalPlayers)
            {
                if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.Generic)
                {
                    RegisterType<PlayableExternal>();
                }
                else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.MpcHc)
                {
                    RegisterType<PlayableMpcHc>();
                }
                else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.TMT)
                {
                    RegisterType<PlayableTMT>();
                }
                else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.TMTMcml)
                {
                    RegisterType<PlayableTMTMcml>();
                }
                else if (externalPlayer.ExternalPlayerType == ConfigData.ExternalPlayerType.VLC)
                {
                    RegisterType<PlayableVLC>();
                }
            }
        }

        /// <summary>
        /// Creates a PlayableItem based on a Media object
        /// </summary>
        public PlayableItem Create(Media media)
        {
            return Create(media, true);
        }

        /// <summary>
        /// Creates a PlayableItem based on a Media object
        /// </summary>
        public PlayableItem Create(Media media, bool allowExternalPlayers)
        {
            foreach (Type type in RegisteredTypes)
            {
                PlayableItem playable = (PlayableItem)Activator.CreateInstance(type);

                // Skip PlayableExternals if specified to do so
                if (!allowExternalPlayers && playable is PlayableExternal)
                {
                    continue;
                }

                if (playable.CanPlay(media))
                {
                    playable.AddMedia(media);
                    AttachPlaybackController(playable);
                    return playable;
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a PlayableItem based on a list of files
        /// </summary>
        public PlayableItem Create(IEnumerable<string> paths)
        {
            if (paths.Count() == 1)
            {
                return Create(paths.First());
            }

            foreach (Type type in RegisteredTypes)
            {
                PlayableItem playable = (PlayableItem)Activator.CreateInstance(type);

                if (playable.CanPlay(paths))
                {
                    playable.AddMedia(paths);
                    AttachPlaybackController(playable);
                    return playable;
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a PlayableItem based on a list of Media objects
        /// </summary>
        public PlayableItem Create(IEnumerable<Media> mediaList)
        {
            if (mediaList.Count() == 1)
            {
                return Create(mediaList.First());
            }

            foreach (Type type in RegisteredTypes)
            {
                PlayableItem playable = (PlayableItem)Activator.CreateInstance(type);

                if (playable.CanPlay(mediaList))
                {
                    playable.AddMedia(mediaList);
                    AttachPlaybackController(playable);
                    return playable;
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a PlayableItem based on a media path
        /// </summary>
        public PlayableItem Create(string path)
        {
            foreach (Type type in RegisteredTypes)
            {
                PlayableItem playable = (PlayableItem)Activator.CreateInstance(type);

                if (playable.CanPlay(path))
                {
                    playable.AddMedia(path);
                    AttachPlaybackController(playable);
                    return playable;
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a PlayableItem based on a Folder object
        /// </summary>
        public PlayableItem Create(Folder folder)
        {
            foreach (Type type in RegisteredTypes)
            {
                PlayableItem playable = (PlayableItem)Activator.CreateInstance(type);

                if (playable.CanPlay(folder))
                {
                    playable.AddMedia(folder);
                    AttachPlaybackController(playable);
                    return playable;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the appropiate PlayBackController for a PlayableItem and attaches it
        /// </summary>
        /// <param name="playable"></param>
        private void AttachPlaybackController(PlayableItem playable)
        {
            foreach (var controller in Kernel.Instance.PlaybackControllers)
            {
                if (playable.CanBePlayedByController(controller))
                {
                    playable.PlaybackController = controller;
                    break;
                }
            }
        }
    }
}
