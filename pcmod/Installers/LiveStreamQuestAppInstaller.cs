﻿using BeatSaverSharp;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Managers;
using LiveStreamQuest.Managers.Network;
using LiveStreamQuest.Protos;
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
        
        Container.BindInterfacesAndSelfTo<GlobalStateManager>().AsSingle();

        var networkManagerBind = Container.BindInterfacesAndSelfTo<NetworkManager>().AsSingle();
        if (_config.ConnectOnStartup)
        {
            networkManagerBind.NonLazy();
        }


#if BS_1_29
        Container.BindInstance(HMMainThreadDispatcher.instance);
#endif
    }
}