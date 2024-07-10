using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class Resources
{
    static readonly Assembly assembly = Assembly.GetExecutingAssembly();

    internal static readonly string GetExtendedUpdateInfo2 = GetString("GetExtendedUpdateInfo2.xml.gz");

    internal static ImageSource GetImageSource(string name)
    {
        using var _ = assembly.GetManifestResourceStream(name);
        return BitmapFrame.Create(_, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    }

    internal static string GetString(string name)
    {
        using var _ = assembly.GetManifestResourceStream(name);
        using GZipStream stream = new(_, CompressionMode.Decompress);
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}