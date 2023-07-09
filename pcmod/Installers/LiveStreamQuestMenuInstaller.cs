using LiveStreamQuest.Managers;
using Zenject;

namespace LiveStreamQuest.Installers;

internal class MenuInstaller : Installer
{
    public MenuInstaller()
    {

    }

    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<MenuPacketHandler>().AsSingle();
    }
}