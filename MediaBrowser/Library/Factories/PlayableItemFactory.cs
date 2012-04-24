using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Library.Entities;
using MediaBrowser.Library.Playables;
using MediaBrowser.Library.Playables.ExternalPlayer;
using MediaBrowser.Library.Playables.MpcHc;
using MediaBrowser.Library.Playables.TMT;
using MediaBrowser.Library.Playables.VLC;

namespace MediaBrowser.Library.Factories
{
    /// <summary>
    /// This is used to create PlayableItems
    /// </summary>
    public class PlayableItemFactory
    {
        public static PlayableItemFactory Instance = new PlayableItemFactory();

        private List<Type> RegisteredTypes = new List<Type>();
        private List<KeyValuePair<Type, Type>> RegisteredExternalPlayerTypes = new List<KeyValuePair<Type, Type>>();

        private PlayableItemFactory()
        {
            // Add the externals
            RegisterExternalPlayerType<PlayableMpcHc, PlayableMpcHcConfigurator>();
            RegisterExternalPlayerType<PlayableVLC, PlayableVLCConfigurator>();
            RegisterExternalPlayerType<PlayableTMT, PlayableTMTConfigurator>();
            RegisterExternalPlayerType<PlayableTMTAddInForWMC, PlayableTMTAddInForWMCConfigurator>();
            RegisterExternalPlayerType<PlayableExternal, PlayableExternalConfigurator>();
        }

        /// <summary>
        /// Registers a new type of PlayableItem to be utilized by the Create methods
        /// </summary>
        public void RegisterType<T>()
            where T : PlayableItem, new()
        {
            RegisteredTypes.Add(typeof(T));
        }

        /// <summary>
        /// Registers a new type of PlayableExternal to be utilized by the Create methods AND show up in the extenral player section of the configurator
        /// </summary>
        public void RegisterExternalPlayerType<TPlayableExternalType, TConfiguratorType>()
            where TPlayableExternalType : PlayableExternal, new()
            where TConfiguratorType : PlayableExternalConfigurator, new()
        {
            RegisteredExternalPlayerTypes.Add(new KeyValuePair<Type, Type>(typeof(TPlayableExternalType), typeof(TConfiguratorType)));
        }

        /// <summary>
        /// Creates a PlayableItem based on a media path
        /// </summary>
        public PlayableItem Create(string path)
        {
            return Create(new string[] { path });
        }

        /// <summary>
        /// Creates a PlayableItem based on a list of files
        /// </summary>
        public PlayableItem Create(IEnumerable<string> paths)
        {
            return Create(paths, true);
        }

        /// <summary>
        /// Creates a PlayableItem based on a list of files
        /// </summary>
        public PlayableItem Create(IEnumerable<string> paths, bool allowExternalPlayers)
        {
            IEnumerable<PlayableItem> playables = GetAllKnownPlayables();

            // Filter out external players if specified to do so
            if (!allowExternalPlayers)
            {
                playables = playables.Where(p => !(p is PlayableExternal));
            }

            PlayableItem playable = playables.FirstOrDefault(p => p.CanPlay(paths)) ?? new PlayableInternal();

            playable.AddMedia(paths);

            return playable;
        }

        /// <summary>
        /// Creates a PlayableItem based on a Media object
        /// </summary>
        public PlayableItem Create(Media media)
        {
            Video video = media as Video;

            if (video != null && video.MediaType == MediaType.ISO && !CanPlayIsoDirectly(GetAllKnownPlayables(), video))
            {
                MountAndUpdateMediaPath(video);
            }
            
            return Create(new Media[] { media });
        }

        /// <summary>
        /// Creates a PlayableItem based on a list of Media objects
        /// </summary>
        public PlayableItem Create(IEnumerable<Media> mediaList)
        {
            PlayableItem playable = GetAllKnownPlayables().FirstOrDefault(p => p.CanPlay(mediaList)) ?? new PlayableInternal();
            
            playable.AddMedia(mediaList);

            return playable;
        }

        /// <summary>
        /// Creates a PlayableItem based on an Item
        /// </summary>
        public PlayableItem Create(Item item)
        {
            if (item.IsFolder)
            {
                return Create(item.BaseItem as Folder);
            }
            else if (item.IsPlayable)
            {
                return Create(item.BaseItem as Media);
            }
            else
            {
                return null;
            }
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

        private List<PlayableItem> GetAllKnownPlayables()
        {
            List<PlayableItem> playables = new List<PlayableItem>();

            foreach (Type type in RegisteredTypes)
            {
                playables.Add(Activator.CreateInstance(type) as PlayableItem);
            }

            playables.AddRange(GetConfiguredExternalPlayerTypes());

            return playables;
        }

        private List<PlayableItem> GetConfiguredExternalPlayerTypes()
        {
            List<PlayableItem> playables = new List<PlayableItem>();

            IEnumerable<KeyValuePair<PlayableExternal, PlayableExternalConfigurator>> allPlayableExternals =
                RegisteredExternalPlayerTypes.Select(t => new KeyValuePair<PlayableExternal, PlayableExternalConfigurator>(Activator.CreateInstance(t.Key) as PlayableExternal, Activator.CreateInstance(t.Value) as PlayableExternalConfigurator));

            // Important - need to add them in the order they appear in configuration
            foreach (ConfigData.ExternalPlayer externalPlayerConfiguration in Config.Instance.ExternalPlayers)
            {
                PlayableExternal playable = allPlayableExternals.First(p => p.Value.ExternalPlayerName == externalPlayerConfiguration.ExternalPlayerName).Key;

                playable.ExternalPlayerConfiguration = externalPlayerConfiguration;

                playables.Add(playable);
            }

            return playables;
        }

        /// <summary>
        /// Gets all external players that should be exposed in the configurator
        /// </summary>
        public IEnumerable<PlayableExternalConfigurator> GetAllPlayableExternalConfigurators()
        {
            return RegisteredExternalPlayerTypes.Select(t => Activator.CreateInstance(t.Value) as PlayableExternalConfigurator);
        }

        /// <summary>
        /// Gets an external player configurator based on the name of the external player
        /// </summary>
        public PlayableExternalConfigurator GetPlayableExternalConfiguratorByName(string name)
        {
            return GetAllPlayableExternalConfigurators().First(p => p.ExternalPlayerName == name);
        }

        /// <summary>
        /// Determines if there is a PlayableItem configured to play an ISO-based entity directly without mounting
        /// </summary>
        private bool CanPlayIsoDirectly(List<PlayableItem> allKnownPlayables, Video video)
        {
            return allKnownPlayables.Where(p => p.CanPlay(video)).Count() > 0;
        }

        /// <summary>
        /// Mounts an iso based Video and updates it's path
        /// </summary>
        private void MountAndUpdateMediaPath(Video video)
        {
            string mountedPath = Application.CurrentInstance.MountISO(video.IsoFiles.First());
            video.Path = mountedPath;

            video.MediaType = MediaTypeResolver.DetermineType(mountedPath);
            video.DisplayMediaType = video.MediaType.ToString();
        }
    }
}
