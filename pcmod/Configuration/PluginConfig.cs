using System.Runtime.CompilerServices;
using IPA.Config.Stores;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]
[assembly: InternalsVisibleTo("LiveStreamQuest.Tests")]
namespace LiveStreamQuest.Configuration
{
    public enum NetworkTransport
    {
        Tcp = 0,
        Udp = 1,
    }

    public class PluginConfig
    {
        public virtual string Address { get; set; } = "192.168.0.24";
        public virtual int Port { get; set; } = 9542;
        public virtual int Transport { get; set; } = (int)NetworkTransport.Tcp;
        public virtual bool ConnectOnStartup { get; set; } = false;
        public virtual int ReconnectionAttempts { get; set; } = 5;

        /// <summary>
        /// Whether to sync the game time with the server.
        ///
        /// This can cause syncing issues or improve syncing. Experiment with it to see if it improves your experience.
        /// </summary>
        public virtual bool SyncTime { get; set; } = true;
        
        // Seconds
        public virtual int ConnectionTimeoutSeconds { get; set; } = 180;
        public virtual bool ShowMenuOnStartup { get; set; } = true;
    }
}
