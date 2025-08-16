using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.IO.Compression;
using System.Windows.Media.Imaging;

static class Resources
{
    static readonly Assembly Assembly = Assembly.GetExecutingAssembly();

    internal static ImageSource GetImageSource(string name)
    {
        using var stream = Assembly.GetManifestResourceStream(name);
        return BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    }
}