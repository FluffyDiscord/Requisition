using System.ComponentModel;
using Newtonsoft.Json;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using TerraStorage.Content.UI.Elements;

namespace TerraStorage
{
    public class TerraStorageConfig : ModConfig
    {
        public static TerraStorageConfig Instance { get; private set; }

        public override ConfigScope Mode => ConfigScope.ServerSide;

        public override void OnLoaded() => Instance = this;

        [JsonIgnore]
        [ShowDespiteJsonIgnore]
        [CustomModConfigItem(typeof(VersionConfigElement))]
        public string Version => ModContent.GetInstance<TerraStorage>().Version.ToString();

        [DefaultValue(false)]
        public bool QuickStarterPack { get; set; }

        [DefaultValue(false)]
        public bool EasierRemoteTerminal { get; set; }

        [DefaultValue(false)]
        public bool PredictiveSyncMode { get; set; }

        /// <summary>Convenience check for whether predictive delta sync is active.</summary>
        public static bool IsPredictiveSync => Instance?.PredictiveSyncMode ?? false;
    }
}
