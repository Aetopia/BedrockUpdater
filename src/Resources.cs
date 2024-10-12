using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.IO.Compression;
using System.Windows.Media.Imaging;

static class Resources
{
    static readonly Assembly assembly = Assembly.GetExecutingAssembly();

    internal static T Get<T>(string name)
    {
        using var stream = assembly.GetManifestResourceStream(name); switch (typeof(T))
        {
            case var @_ when _ == typeof(string): using (StreamReader reader = new(new GZipStream(stream, CompressionMode.Decompress))) return (T)(object)reader.ReadToEnd();
            case var @_ when _ == typeof(ImageSource): return (T)(object)BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            default: throw new TypeAccessException();
        }
    }
}