using System.ComponentModel;
using Newtonsoft.Json;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using TerraStorage.Content.UI.Elements;

namespace TerraStorage
{
    public class TerraStorageClientConfig : ModConfig
    {
        public static TerraStorageClientConfig Instance { get; private set; }

        public override ConfigScope Mode => ConfigScope.ClientSide;

        public override void OnLoaded() => Instance = this;

        [JsonIgnore]
        [ShowDespiteJsonIgnore]
        [CustomModConfigItem(typeof(VersionConfigElement))]
        public string Version => ModContent.GetInstance<TerraStorage>().Version.ToString();

        [Header("General")]

        [DefaultValue(true)]
        public bool RememberSearchQuery { get; set; }

        [DefaultValue(true)]
        public bool InStorageTooltip { get; set; }

        [Header("CraftingTree")]

        [DefaultValue(1.0f)]
        [Range(0.3f, 2.0f)]
        [Increment(0.1f)]
        public float CraftingTreeDefaultZoom { get; set; }

        [Header("Appearance")]

        [DefaultValue(85)]
        [Range(0, 100)]
        [Increment(5)]
        [Slider]
        public int PanelUnderlayOpacity { get; set; }

        [Header("Debug")]

        [DefaultValue(false)]
        public bool DebugTooltips { get; set; }

        [JsonIgnore]
        [ShowDespiteJsonIgnore]
        [CustomModConfigItem(typeof(BackupRestoreConfigElement))]
        public object BackupRestore => null;
    }
}
