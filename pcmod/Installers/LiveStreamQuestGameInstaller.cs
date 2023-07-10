using System;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Managers;
using LiveStreamQuest.Managers.Network;
using Zenject;

namespace LiveStreamQuest.Installers;

internal class GameInstaller : Installer, IDisposable
{
    [Inject] private readonly GlobalStateManager _globalStateManager;

    public GameInstaller()
    {

    }

    public override void InstallBindings()
    {
        if (!_globalStateManager.StartingGameFromQuest) return;
        // TODO: Conditionally
        Container.BindInterfacesAndSelfTo<VRControllerManager>().AsSingle();
        Container.BindInterfacesAndSelfTo<GamePacketHandler>().AsSingle();
    }

    public void Dispose()
    {
        _globalStateManager.StartingGameFromQuest = false;
    }
}