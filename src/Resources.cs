using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class Resources
{
    static readonly Assembly assembly = Assembly.GetExecutingAssembly();

    internal static readonly string GetExtendedUpdateInfo2 = ToString("GetExtendedUpdateInfo2.xml");

    internal static readonly ImageSource Icon = ToImageSource(".ico");

    internal static ImageSource ToImageSource(string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);
        return BitmapFrame.Create(stream);
    }

    internal static string ToString(string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}