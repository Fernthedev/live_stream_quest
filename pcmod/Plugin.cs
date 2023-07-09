using System;
using System.Linq;
using BeatSaverSharp;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
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

        private readonly BeatSaver _beatSaver = new(new BeatSaverOptions("LiveStreamQuest", new Version(0, 1, 0)));

        [Init]
        public void Init(Zenjector zenjector, IPALogger logger, Config config)
        {
            Instance = this;
            Log = logger;

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