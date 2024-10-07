using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.IO.Compression;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;

static class Resources
{
    static readonly Assembly assembly = Assembly.GetExecutingAssembly();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    unsafe static U _<T, U>(T _) => *(U*)&_;

    internal static T Get<T>(string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);
        switch (typeof(T))
        {
            case var @_ when _ == typeof(string): using (StreamReader reader = new(new GZipStream(stream, CompressionMode.Decompress))) return _<string, T>(reader.ReadToEnd());

            case var @_ when _ == typeof(ImageSource): return _<ImageSource, T>(BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad));

            default: throw new TypeAccessException();
        }
    }
}