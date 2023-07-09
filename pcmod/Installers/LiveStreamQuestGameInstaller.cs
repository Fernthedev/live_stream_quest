using System;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Managers;
using Zenject;

namespace LiveStreamQuest.Installers;

internal class GameInstaller : Installer, IDisposable
{
    [Inject] private readonly StateManager _stateManager;

    public GameInstaller()
    {

    }

    public override void InstallBindings()
    {
        if (!_stateManager.StartingGameFromQuest) return;
        // TODO: Conditionally
        Container.BindInterfacesAndSelfTo<GamePacketHandler>().AsSingle();
    }

    public void Dispose()
    {
        _stateManager.StartingGameFromQuest = false;
    }
}