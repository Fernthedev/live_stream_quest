using System;
using BeatSaverSharp;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Network;
using Zenject;

namespace LiveStreamQuest.Installers;

internal class AppInstaller : Installer
{
    private readonly PluginConfig _config;
    private readonly BeatSaver _beatSaver;

    public AppInstaller(PluginConfig config, BeatSaver beatSaver)
    {
        _config = config;
        _beatSaver = beatSaver;
    }

    public override void InstallBindings()
    {
        Container.BindInstance(_config);
        Container.BindInstance(_beatSaver);
        Container.BindInterfacesAndSelfTo<NetworkManager>().AsSingle();
    }
}