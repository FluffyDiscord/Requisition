namespace TerraStorage.Common
{
    // Represents the storage capacity tier of a Storage Disk, from Basic (64 stacks)
    // up to Terra (2048 stacks). Integer values are used for NBT serialization.
    public enum DiskTier
    {
        Tier1 = 0,
        Tier2 = 1,
        Tier3 = 2,
        Tier4 = 3,
        Tier5 = 4,
        Tier6 = 5
    }

    // Extension methods for <see cref="DiskTier"/> providing capacity, display name,
    // and UI color for each tier. 
    public static class DiskTierExtensions
    {
        // Indexed by (int)DiskTier — order must match the enum values.
        private static readonly int[] Capacities = { 64, 128, 256, 512, 1024, 2048 };

        //Returns the maximum number of item stacks this tier can hold.
        public static int GetCapacity(this DiskTier tier) => Capacities[(int)tier];

        public static string GetName(this DiskTier tier) => tier switch
        {
            DiskTier.Tier1 => "Basic",
            DiskTier.Tier2 => "Advanced",
            DiskTier.Tier3 => "Superior",
            DiskTier.Tier4 => "Elite",
            DiskTier.Tier5 => "Ultimate",
            DiskTier.Tier6 => "Terra",
            _ => "Unknown"
        };

        //Returns the UI accent color used to visually distinguish this tier.
        public static Microsoft.Xna.Framework.Color GetColor(this DiskTier tier) => tier switch
        {
            DiskTier.Tier1 => new(200, 200, 200),
            DiskTier.Tier2 => new(100, 200, 100),
            DiskTier.Tier3 => new(100, 100, 255),
            DiskTier.Tier4 => new(200, 50, 200),
            DiskTier.Tier5 => new(255, 165, 0),
            DiskTier.Tier6 => new(255, 50, 50),
            _ => Microsoft.Xna.Framework.Color.White
        };
    }
}
