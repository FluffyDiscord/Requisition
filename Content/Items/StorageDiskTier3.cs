using TerraStorage.Common;

namespace TerraStorage.Content.Items
{
    /// <summary>Superior Storage Disk — holds up to 256 item stacks.</summary>
    public class StorageDiskTier3 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier3;
    }
}
