using System;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.ModLoader.Config.UI;
using Terraria.UI;
using Requisition.Systems;

namespace Requisition.Content.UI.Elements
{
    // Config UI element that shows a world dropdown and per-slot restore buttons
    // for Requisition's rolling disk backups.
    public class BackupRestoreConfigElement : ConfigElement<object>
    {
        private static string GetSlotLabel(int i) => Language.GetTextValue(i switch {
            0 => "Mods.Requisition.UI.BackupRestore.SlotRecent",
            1 => "Mods.Requisition.UI.BackupRestore.SlotPrevious",
            _ => "Mods.Requisition.UI.BackupRestore.SlotOldest"
        });

        private string[] _worldPaths = Array.Empty<string>();
        private int _selectedWorld = -1;

        private UIText _worldNameText;
        private UIText[] _slotTexts;
        private UIText _statusText;

        private const float Indent = 16f;            // left indent for all content
        private const float RowH = 28f;
        private const float BtnW = 66f;      // Restore button width
        private const float NavW = 24f;      // < > button width
        private const float NameX = Indent + NavW + 8f;
        private const float NextX = NameX + 200f; // > button; leaves 200px for world name
        private const float TextX = Indent + BtnW + 8f;    // slot label starts after Restore button
        private const int MaxNameChars = 22;       // truncate world names beyond this

        public override void OnBind()
        {
            base.OnBind();

            _slotTexts = new UIText[BackupSystem.BackupCount];

            float y = 30f; // start below the base label row

            // World selector: [<]  World Name  [>]
            var prevBtn = MakeButton("<", () => StepWorld(-1), NavW);
            prevBtn.Top.Set(y, 0f);
            prevBtn.Left.Set(Indent, 0f);
            Append(prevBtn);

            _worldNameText = new UIText("---", 0.85f);
            _worldNameText.Left.Set(NameX, 0f);
            _worldNameText.Top.Set(y + 4f, 0f);
            Append(_worldNameText);

            var nextBtn = MakeButton(">", () => StepWorld(1), NavW);
            nextBtn.Left.Set(NextX, 0f);
            nextBtn.Top.Set(y, 0f);
            Append(nextBtn);

            y += RowH;

            // Backup slot rows: [Restore]  Label: timestamp
            for (int i = 0; i < BackupSystem.BackupCount; i++)
            {
                int slot = i;

                var restoreBtn = MakeButton(Language.GetTextValue("Mods.Requisition.UI.BackupRestore.Restore"), () => DoRestore(slot), BtnW);
                restoreBtn.Left.Set(Indent, 0f);
                restoreBtn.Top.Set(y, 0f);
                Append(restoreBtn);

                _slotTexts[i] = new UIText("", 0.75f);
                _slotTexts[i].Left.Set(TextX, 0f);
                _slotTexts[i].Top.Set(y + 4f, 0f);
                Append(_slotTexts[i]);

                y += RowH;
            }

            // Status line
            _statusText = new UIText("", 0.75f) { TextColor = Color.Yellow };
            _statusText.Left.Set(Indent, 0f);
            _statusText.Top.Set(y + 2f, 0f);
            Append(_statusText);
            y += 22f;

            Height.Set(y + 6f, 0f);

            RefreshWorlds();
            UpdateSlotDisplay();
        }

        // ─── World cycling ──────────────────────────────────────────────────

        private void RefreshWorlds()
        {
            _worldPaths = BackupSystem.GetWorldFiles();
            if (_worldPaths.Length == 0) { _selectedWorld = -1; return; }

            // Auto-select current world if one is loaded
            if (!string.IsNullOrEmpty(Main.worldPathName))
            {
                for (int i = 0; i < _worldPaths.Length; i++)
                {
                    if (string.Equals(_worldPaths[i], Main.worldPathName, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedWorld = i;
                        return;
                    }
                }
            }

            _selectedWorld = 0;
        }

        private void StepWorld(int dir)
        {
            if (_worldPaths.Length == 0) return;
            _selectedWorld = (_selectedWorld + dir + _worldPaths.Length) % _worldPaths.Length;
            _statusText?.SetText("");
            UpdateSlotDisplay();
        }

        // ─── Display ────────────────────────────────────────────────────────

        private void UpdateSlotDisplay()
        {
            if (_selectedWorld < 0 || _selectedWorld >= _worldPaths.Length)
            {
                _worldNameText?.SetText(Language.GetTextValue("Mods.Requisition.UI.BackupRestore.NoWorldsFound"));
                for (int i = 0; i < BackupSystem.BackupCount; i++)
                    _slotTexts[i]?.SetText("—");
                return;
            }

            string worldPath = _worldPaths[_selectedWorld];
            string worldName = Path.GetFileNameWithoutExtension(worldPath);
            string displayName = worldName.Length > MaxNameChars
                ? worldName[..MaxNameChars] + "…"
                : worldName;

            bool restorePending = BackupSystem.RestorePending(worldPath);
            _worldNameText?.SetText(displayName + (restorePending ? " *" : ""));

            for (int i = 0; i < BackupSystem.BackupCount; i++)
            {
                if (!BackupSystem.BackupExists(worldPath, i))
                {
                    _slotTexts[i]?.SetText($"{GetSlotLabel(i)}: —");
                }
                else
                {
                    DateTime t = BackupSystem.GetBackupTime(worldPath, i);
                    string ts = t != default ? t.ToString("MM/dd HH:mm") : "?";
                    _slotTexts[i]?.SetText($"{GetSlotLabel(i)}: {ts}");
                }
            }
        }

        // ─── Restore ────────────────────────────────────────────────────────

        private void DoRestore(int slot)
        {
            if (_selectedWorld < 0 || _selectedWorld >= _worldPaths.Length) return;

            string worldPath = _worldPaths[_selectedWorld];
            if (!BackupSystem.BackupExists(worldPath, slot))
            {
                _statusText?.SetText(Language.GetTextValue("Mods.Requisition.UI.BackupRestore.NoBackup"));
                return;
            }

            bool ok = BackupSystem.QueueRestore(worldPath, slot);
            _statusText?.SetText(ok
                ? Language.GetTextValue("Mods.Requisition.UI.BackupRestore.QueuedRestore")
                : Language.GetTextValue("Mods.Requisition.UI.BackupRestore.QueueFailed"));

            UpdateSlotDisplay(); // refresh the * indicator
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private static UIPanel MakeButton(string text, Action onClick, float width = 24f, float height = 19f)
        {
            var btn = new UIPanel();
            btn.Width.Set(width, 0f);
            btn.Height.Set(height, 0f);
            btn.SetPadding(0f);

            var label = new UIText(text, 0.7f);
            label.HAlign = 0.5f;
            label.VAlign = 0.5f;
            btn.Append(label);

            btn.OnLeftClick += (_, __) => onClick();
            return btn;
        }
    }
}
