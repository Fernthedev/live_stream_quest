using LiveStreamQuest.Configuration;
using LiveStreamQuest.Managers;
using Zenject;

namespace LiveStreamQuest.Installers;

internal class GameInstaller : Installer
{


    public GameInstaller()
    {

    }

    public override void InstallBindings()
    {
        // TODO: Conditionally
        Container.BindInterfacesAndSelfTo<GamePacketHandler>().AsSingle();
    }
}