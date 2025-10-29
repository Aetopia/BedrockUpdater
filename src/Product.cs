sealed class Product
{
    internal Product(string productId, string packageFamilyName)
    {
        ProductId = productId;
        PackageFamilyName = packageFamilyName;
    }

    internal readonly string ProductId, PackageFamilyName;

    static Product()
    {
        MinecraftUWP = new("9NBLGGH2JHXJ", "Microsoft.MinecraftUWP_8wekyb3d8bbwe");
        GamingServices = new("9MWPM2CQNLHN", "Microsoft.GamingServices_8wekyb3d8bbwe");
        MinecraftWindowsBeta = new("9P5X4QVLC2XR", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe");
    }

    internal static readonly Product MinecraftUWP;

    internal static readonly Product GamingServices;

    internal static readonly Product MinecraftWindowsBeta;
}