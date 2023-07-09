using LiveStreamQuest.Configuration;
using LiveStreamQuest.Network;
using Zenject;

namespace LiveStreamQuest.Installers;

internal class AppInstaller : Installer
{
    private readonly PluginConfig _config;

    public AppInstaller(PluginConfig config)
    {
        _config = config;
    }

    public override void InstallBindings()
    {
        Container.BindInstance(_config);
        Container.BindInterfacesAndSelfTo<NetworkManager>();
    }
}