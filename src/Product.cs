sealed class Product
{
    internal Product(string productId, string packageFamilyName)
    {
        ProductId = productId;
        PackageFamilyName = packageFamilyName;
    }

    static Product()
    {
        Release = new("9NBLGGH2JHXJ", "Microsoft.MinecraftUWP_8wekyb3d8bbwe");
        Xbox = new("9WZDNCRD1HKW", "Microsoft.XboxIdentityProvider_8wekyb3d8bbwe");
        Preview = new("9P5X4QVLC2XR", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe");
    }

    internal readonly string ProductId, PackageFamilyName;

    internal static readonly Product Xbox, Release, Preview;
}