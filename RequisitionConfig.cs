using System.ComponentModel;
using Newtonsoft.Json;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using TerraStorage.Content.UI.Elements;

namespace TerraStorage
{
    public class RequisitionConfig : ModConfig
    {
        public static RequisitionConfig Instance { get; private set; }

        public override ConfigScope Mode => ConfigScope.ServerSide;

        public override void OnLoaded() => Instance = this;

        [JsonIgnore]
        [ShowDespiteJsonIgnore]
        [CustomModConfigItem(typeof(VersionConfigElement))]
        public string Version => ModContent.GetInstance<Requisition>().Version.ToString();

        [DefaultValue(false)]
        public bool QuickStarterPack { get; set; }

        [DefaultValue(false)]
        public bool EasierRemoteTerminal { get; set; }
    }
}
