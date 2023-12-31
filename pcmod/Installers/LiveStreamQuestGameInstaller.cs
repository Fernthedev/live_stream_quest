﻿using System;
using LiveStreamQuest.Managers;
using LiveStreamQuest.Managers.Network;
using SiraUtil.Tools.FPFC;
using Zenject;

namespace LiveStreamQuest.Installers;

internal class GameInstaller : Installer, IDisposable
{
    [Inject] private readonly GlobalStateManager _globalStateManager;
    [Inject] private readonly IFPFCSettings _fpfcSettings;

    public override void InstallBindings()
    {
        if (!_globalStateManager.StartingGameFromQuest) return;
        
        Container.BindInterfacesAndSelfTo<VRControllerManager>().AsSingle();
        Container.BindInterfacesAndSelfTo<GamePacketHandler>().AsSingle().NonLazy();
        
        // Only enable if smooth camera is enabled
        
        // Delegate control to FpfcManager
        // _fpfcSettings.Enabled = false;
        Container.BindInterfacesAndSelfTo<FpfcManager>().AsSingle().NonLazy();
        Container.BindInterfacesAndSelfTo<TimeDesyncFixManager>().AsSingle().NonLazy();
    }

    public void Dispose()
    {
        _globalStateManager.StartingGameFromQuest = false;
    }
}