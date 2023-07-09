using LiveStreamQuest.Managers;
using Zenject;

namespace LiveStreamQuest.Installers;

internal class MenuInstaller : Installer, IInitializable
{
    [Inject] private readonly StateManager _stateManager;

    
    public MenuInstaller()
    {

    }

    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<MenuPacketHandler>().AsSingle();
    }

    public void Initialize()
    {
        _stateManager.StartingGameFromQuest = false;
    }
}