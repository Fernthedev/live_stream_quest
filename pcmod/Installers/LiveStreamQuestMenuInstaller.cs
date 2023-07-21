using LiveStreamQuest.Managers;
using LiveStreamQuest.Managers.Network;
using LiveStreamQuest.UI;
using Zenject;

namespace LiveStreamQuest.Installers;

internal class MenuInstaller : Installer, IInitializable
{
    [Inject] private readonly GlobalStateManager _globalStateManager;
    
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<MenuPacketHandler>().AsSingle();
        Container.BindInterfacesAndSelfTo<LiveStreamQuestViewController>().FromNewComponentAsViewController().AsSingle();
    }

    public void Initialize()
    {
        _globalStateManager.StartingGameFromQuest = false;
    }
}