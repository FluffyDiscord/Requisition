using System;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace TerraStorage.Systems
{
    // Manages rolling backups of StorageWorldSystem disk data per world.
    // Keeps up to 3 backup slots; slot 0 is continuously updated during a play session,
    // and slots are rotated on each new world load (0→1→2, oldest dropped).
    // Supports queuing a wholesale restore that takes effect on next world load.
    public class BackupSystem : ModSystem
    {
        public const int BackupCount = 3;
        private const int WriteDebounceFrames = 600; // 10 s at 60 fps

        private static bool _isDirty;
        private static int _writeTimer;

        public static void MarkDirty()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient) return;
            _isDirty = true;
            _writeTimer = 0;
        }

        public override void PostUpdateEverything()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient) return;
            if (!_isDirty) return;
            if (++_writeTimer >= WriteDebounceFrames)
                Flush();
        }

        public override void OnWorldLoad()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient) return;
            _isDirty = false;
            _writeTimer = 0;
            RotateBackups();
        }

        public override void OnWorldUnload()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient) return;
            if (_isDirty) Flush();
            _isDirty = false;
            _writeTimer = 0;
        }

        // ─── Write ──────────────────────────────────────────────────────────

        private static void Flush()
        {
            try
            {
                var sys = StorageWorldSystem.Instance;
                if (sys == null) return;

                string path = GetBackupPath(Main.worldPathName, 0);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var tag = new TagCompound
                {
                    ["disks"] = sys.GetAllDiskData().Select(d => d.Save()).ToList(),
                    ["insertionCounter"] = sys.InsertionCounter
                };

                using var stream = File.Open(path, FileMode.Create);
                TagIO.ToStream(tag, stream);

                _isDirty = false;
                _writeTimer = 0;
            }
            catch (Exception ex)
            {
                ModContent.GetInstance<TerraStorage>()?.Logger.Warn($"[TerraStorage] BackupSystem.Flush: {ex.Message}");
            }
        }

        private static void RotateBackups()
        {
            try
            {
                if (string.IsNullOrEmpty(Main.worldPathName)) return;
                if (!File.Exists(GetBackupPath(Main.worldPathName, 0))) return;

                // Shift: backup_(N-1) → backup_N, oldest dropped
                for (int i = BackupCount - 1; i >= 1; i--)
                {
                    string dst = GetBackupPath(Main.worldPathName, i);
                    string src = GetBackupPath(Main.worldPathName, i - 1);
                    if (File.Exists(dst)) File.Delete(dst);
                    if (File.Exists(src)) File.Move(src, dst);
                }
            }
            catch (Exception ex)
            {
                ModContent.GetInstance<TerraStorage>()?.Logger.Warn($"[TerraStorage] BackupSystem.RotateBackups: {ex.Message}");
            }
        }

        // ─── Restore ────────────────────────────────────────────────────────

        // Called from StorageWorldSystem.LoadWorldData. If a restore file is present,
        // consumes it and returns the tag to load from instead of the world file.
        public static TagCompound TryConsumeRestoreOverride()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient) return null;
            try
            {
                string path = GetRestorePath(Main.worldPathName);
                if (!File.Exists(path)) return null;

                TagCompound tag;
                using (var stream = File.OpenRead(path))
                    tag = TagIO.FromStream(stream);

                File.Delete(path);
                return tag;
            }
            catch (Exception ex)
            {
                ModContent.GetInstance<TerraStorage>()?.Logger.Warn($"[TerraStorage] BackupSystem.TryConsumeRestoreOverride: {ex.Message}");
                return null;
            }
        }

        // Queues a restore for the given world and slot. Takes effect on next world load.
        public static bool QueueRestore(string worldPath, int slot)
        {
            try
            {
                string src = GetBackupPath(worldPath, slot);
                if (!File.Exists(src)) return false;

                string dst = GetRestorePath(worldPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                ModContent.GetInstance<TerraStorage>()?.Logger.Warn($"[TerraStorage] BackupSystem.QueueRestore: {ex.Message}");
                return false;
            }
        }

        // ─── Query ──────────────────────────────────────────────────────────

        public static bool BackupExists(string worldPath, int slot) =>
            File.Exists(GetBackupPath(worldPath, slot));

        public static DateTime GetBackupTime(string worldPath, int slot)
        {
            try { return File.GetLastWriteTime(GetBackupPath(worldPath, slot)); }
            catch { return default; }
        }

        public static bool RestorePending(string worldPath) =>
            File.Exists(GetRestorePath(worldPath));

        public static string[] GetWorldFiles()
        {
            try
            {
                string dir = Path.Combine(Main.SavePath, "Worlds");
                return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.wld") : Array.Empty<string>();
            }
            catch { return Array.Empty<string>(); }
        }

        // ─── Paths ──────────────────────────────────────────────────────────

        public static string GetBackupDir(string worldPath) =>
            Path.Combine(Path.GetDirectoryName(worldPath)!, Path.GetFileNameWithoutExtension(worldPath) + "_TerraStorage");

        public static string GetBackupPath(string worldPath, int slot) =>
            Path.Combine(GetBackupDir(worldPath), $"backup_{slot}.dat");

        private static string GetRestorePath(string worldPath) =>
            Path.Combine(GetBackupDir(worldPath), "restore.dat");
    }
}
