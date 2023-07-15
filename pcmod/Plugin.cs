using System;
using System.Linq;
using BeatSaverSharp;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using IPA.Loader;
using JetBrains.Annotations;
using SiraUtil.Zenject;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Installers;
using IPALogger = IPA.Logging.Logger;

namespace LiveStreamQuest
{
    [Plugin(RuntimeOptions.DynamicInit),
     NoEnableDisable] // NoEnableDisable supresses the warnings of not having a OnEnable/OnStart
    // and OnDisable/OnExit methods
    public class Plugin
    {
        public const string ID = "LiveStreamQuest";
        internal static Plugin Instance { get; private set; }
        internal static IPALogger Log { get; private set; }
        internal PluginConfig _config;

        internal static Hive.Versioning.Version Version;

        private BeatSaver _beatSaver;

        [Init]
        [UsedImplicitly]
        public void Init(Zenjector zenjector, IPALogger logger, Config config, PluginMetadata metadata)
        {
            Instance = this;
            Log = logger;

            Version = metadata.HVersion;

            _beatSaver = new(new BeatSaverOptions("LiveStreamQuest",
                new Version((int)Version.Major, (int)Version.Minor, (int)Version.Patch)));

            zenjector.UseLogger(logger);
            zenjector.UseMetadataBinder<Plugin>();

            // This logic also goes for installing to Menu and Game. "Location." will give you a list of places to install to.
            zenjector.Install<AppInstaller>(Location.App, config.Generated<PluginConfig>(), _beatSaver);
            zenjector.Install<MenuInstaller>(Location.Menu);
            zenjector.Install<GameInstaller>(Location.GameCore);
            // zenjector.Install<{Menu|Game}Installer>(Location.{Menu|Game}>()); Remove the one you don't need and the { }.
        }
    }
}