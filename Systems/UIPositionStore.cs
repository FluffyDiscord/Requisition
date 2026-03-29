using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace TerraStorage.Systems
{
    // Persists UI window positions and sizes across sessions. Saved to a client-side file
    // in the tModLoader save directory; never sent to the server.
    public class UIPositionStore : ModSystem
    {
        private static readonly Dictionary<string, (float x, float y)> _positions = new();
        private static readonly Dictionary<string, (float w, float h)> _sizes     = new();
        private static bool _loaded = false;

        private static string SavePath => Path.Combine(
            Main.SavePath, "ModLoader", "TerraStorage", "ui_positions.dat");

        public override void Load()
        {
            if (Main.dedServ) return;
            EnsureLoaded();
        }

        public override void Unload()
        {
            _positions.Clear();
            _sizes.Clear();
            _loaded = false;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Load_();
        }

        public static void Save(string key, float x, float y)
        {
            EnsureLoaded();
            _positions[key] = (x, y);
            Write();
        }

        public static void SaveWithSize(string key, float x, float y, float w, float h)
        {
            EnsureLoaded();
            _positions[key] = (x, y);
            _sizes[key]     = (w, h);
            Write();
        }

        public static bool TryGet(string key, out float x, out float y)
        {
            EnsureLoaded();
            if (_positions.TryGetValue(key, out var pos))
            {
                x = pos.x;
                y = pos.y;
                return true;
            }
            x = y = 0f;
            return false;
        }

        public static bool TryGetSize(string key, out float w, out float h)
        {
            EnsureLoaded();
            if (_sizes.TryGetValue(key, out var sz))
            {
                w = sz.w;
                h = sz.h;
                return true;
            }
            w = h = 0f;
            return false;
        }

        private static readonly string[] KnownKeys = { "terminal", "drivebay", "craftingcore", "craftingtree", "encyclopedia", "favbutton", "favpanel" };

        private static void Load_()
        {
            try
            {
                if (!File.Exists(SavePath)) return;
                var tag = TagIO.FromFile(SavePath);
                foreach (string key in KnownKeys)
                {
                    if (!tag.ContainsKey(key)) continue;
                    var sub = tag.Get<TagCompound>(key);
                    _positions[key] = (sub.GetFloat("x"), sub.GetFloat("y"));
                    if (sub.ContainsKey("w"))
                        _sizes[key] = (sub.GetFloat("w"), sub.GetFloat("h"));
                }
            }
            catch (Exception) { _positions.Clear(); _sizes.Clear(); }
        }

        private static void Write()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                var tag = new TagCompound();
                foreach (var kvp in _positions)
                {
                    var sub = new TagCompound { ["x"] = kvp.Value.x, ["y"] = kvp.Value.y };
                    if (_sizes.TryGetValue(kvp.Key, out var sz))
                    {
                        sub["w"] = sz.w;
                        sub["h"] = sz.h;
                    }
                    tag[kvp.Key] = sub;
                }
                TagIO.ToFile(tag, SavePath);
            }
            catch (Exception) { }
        }
    }
}
