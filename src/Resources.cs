using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.IO.Compression;
using System.Windows.Media.Imaging;

static class Resources
{
    static readonly Assembly assembly = Assembly.GetExecutingAssembly();

    unsafe static U _<T, U>(T _) => *(U*)&_;

    internal static T Get<T>(string name)
    {
        switch (typeof(T))
        {
            case var @_ when _ == typeof(string):
                using (GZipStream stream = new(assembly.GetManifestResourceStream(name), CompressionMode.Decompress))
                using (StreamReader reader = new(stream)) return _<string, T>(reader.ReadToEnd());

            case var @_ when _ == typeof(ImageSource):
                using (var stream = assembly.GetManifestResourceStream(name)) return _<ImageSource, T>(BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad));

            default: throw new TypeAccessException();
        }
    }
}