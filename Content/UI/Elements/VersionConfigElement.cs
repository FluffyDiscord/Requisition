using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader.Config.UI;

namespace TerraStorage.Content.UI.Elements
{
    // Read-only config element that displays a string value as plain text.
    // Used to show the mod version in the config UI without an editable field. 
    public class VersionConfigElement : ConfigElement<string>
    {
        public override void OnBind()
        {
            base.OnBind();
            Height.Set(30f, 0f);

            var label = new UIText(Value ?? "", 0.85f);
            label.VAlign = 0.5f;
            label.Left.Set(-150f, 1f);
            Append(label);
        }
    }
}
