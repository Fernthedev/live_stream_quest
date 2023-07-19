using System.Runtime.CompilerServices;
using IPA.Config.Stores;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace LiveStreamQuest.Configuration
{
    public class PluginConfig
    {
        public static PluginConfig Instance { get; set; }

        /// A value for the config has to be virtual if you want BSIPA
        /// to detect a value change and save the config automatically
        // public virtual int MeaningofLife = 42 { get; set; } 
        public virtual string Address { get; set; } = "192.168.0.24";
        public virtual int Port { get; set; } = 9542;
        public virtual bool ConnectOnStartup { get; set; } = false;
        public virtual int ReconnectionAttempts { get; set; } = 5;
        
        // Seconds
        public virtual int ConnectionTimeoutSeconds { get; set; } = 180;
        public virtual bool DontShowAgain { get; set; } = false;
    }
}