using TerraStorage.Common;

namespace TerraStorage.Content.Items
{
    /// <summary>Terra Storage Disk — holds up to 2048 item stacks.</summary>
    public class StorageDiskTier6 : StorageDiskBase
    {
        public override DiskTier Tier => DiskTier.Tier6;
    }
}
