using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class Resources
{
    static readonly Assembly _assembly = Assembly.GetExecutingAssembly();

    internal static ImageSource GetImageSource(string name)
    {
        using var stream = _assembly.GetManifestResourceStream(name);
        return BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    }
}