using System;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Requisition.Systems;

namespace Requisition.Commands
{
    // Server console command for listing and immediately restoring Requisition disk backups.
    // Usage:
    //   tsrestore list        — show available backups for the current world
    //   tsrestore &lt;0|1|2&gt;    — restore from that slot immediately (no world reload needed)
    public class StorageRestoreCommand : ModCommand
    {
        public override string Command => "tsrestore";
        public override CommandType Type => CommandType.Console | CommandType.World;
        public override string Usage => "tsrestore list  |  tsrestore <0|1|2>";
        public override string Description => "List or restore Requisition disk backups for the current world.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                caller.Reply("tsrestore must be run on the server.");
                return;
            }

            if (string.IsNullOrEmpty(Main.worldPathName))
            {
                caller.Reply("No world is loaded.");
                return;
            }

            if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                ListBackups(caller);
                return;
            }

            if (int.TryParse(args[0], out int slot) && slot >= 0 && slot < BackupSystem.BackupCount)
            {
                DoRestore(caller, slot);
                return;
            }

            caller.Reply($"Unknown argument '{args[0]}'. Usage: {Usage}");
        }

        private static void ListBackups(CommandCaller caller)
        {
            string[] labels = { "Recent", "Previous", "Oldest" };
            caller.Reply($"Backups for: {Path.GetFileNameWithoutExtension(Main.worldPathName)}");

            bool any = false;
            for (int i = 0; i < BackupSystem.BackupCount; i++)
            {
                if (!BackupSystem.BackupExists(Main.worldPathName, i))
                {
                    caller.Reply($"  [{i}] {labels[i]}: —");
                }
                else
                {
                    DateTime t = BackupSystem.GetBackupTime(Main.worldPathName, i);
                    string ts = t != default ? t.ToString("yyyy-MM-dd HH:mm:ss") : "unknown time";
                    caller.Reply($"  [{i}] {labels[i]}: {ts}");
                    any = true;
                }
            }

            if (!any)
                caller.Reply("  No backups exist yet.");

            if (BackupSystem.RestorePending(Main.worldPathName))
                caller.Reply("  * A restore is already queued (will apply on next world load).");
        }

        private static void DoRestore(CommandCaller caller, int slot)
        {
            if (!BackupSystem.BackupExists(Main.worldPathName, slot))
            {
                caller.Reply($"Slot {slot} has no backup.");
                return;
            }

            try
            {
                TagCompound tag;
                using (var stream = File.OpenRead(BackupSystem.GetBackupPath(Main.worldPathName, slot)))
                    tag = TagIO.FromStream(stream);

                var sys = StorageWorldSystem.Instance;
                sys.RestoreFromTag(tag);

                // In MP, push all restored disk data to connected clients.
                if (Main.netMode == NetmodeID.Server)
                {
                    var mod = ModLoader.GetMod("Requisition");
                    var allIds = sys.GetAllDiskData().Select(d => d.DiskId).ToList();
                    NetworkHandler.BroadcastDiskData(mod, allIds, ignoreClient: -1);
                }

                string ts = BackupSystem.GetBackupTime(Main.worldPathName, slot).ToString("yyyy-MM-dd HH:mm:ss");
                caller.Reply($"Storage restored from slot {slot} ({ts}). Drive Bay contents reflect backup; terminals will refresh on next open.");
            }
            catch (Exception ex)
            {
                caller.Reply($"Restore failed: {ex.Message}");
            }
        }
    }
}
