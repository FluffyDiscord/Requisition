using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using TerraStorage.Common;

namespace TerraStorage.Systems
{
    // A refused storage operation has to say why - in singleplayer, where the crafting panel knows
    // the reason first-hand, and on a multiplayer client, where it arrives as a byte in the
    // server's response. Both speak through here so there is one vocabulary rather than two, and
    // so the network layer never has to reach into a UI element class.
    public static class StorageOperationReporter
    {
        private static readonly StorageOperationFailureThrottle Throttle = new();

        public static void ReportFailure(StorageOperationFailure failure)
        {
            if (!Throttle.ShouldReport(failure, Main.GameUpdateCount))
                return;

            string prefix = Language.GetTextValue(GetPrefixLocalizationKey());
            string reason = Language.GetTextValue(StorageOperationFailures.GetLocalizationKey(failure));

            var (red, green, blue) = GetDenialTextColor();
            Main.NewText(prefix + reason, red, green, blue);

            // Not MenuTick: the multiplayer craft path already ticks when the request is sent, and
            // the same sound for "sent" and "refused" carries no information.
            SoundEngine.PlaySound(SoundID.MenuClose);
        }

        private static string GetPrefixLocalizationKey()
            => "Mods.TerraStorage.UI.OperationFailed.Prefix";

        private static (byte red, byte green, byte blue) GetDenialTextColor() => (255, 100, 100);
    }
}
