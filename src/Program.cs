using System.Runtime.InteropServices;
using System.Windows;
using System;

static class Program
{
    [DllImport("Shlwapi")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern bool IsOS(int dwOS);

    [STAThread]
    static void Main(string[] args)
    {
        using System.Threading.Mutex mutex = new(true, "C7B58EAD-356C-40A1-A145-7262C3C04D00", out bool createdNew);
        if (Environment.OSVersion.Version < new Version(10, 0, 19041, 0) || IsOS(29) || !createdNew) return;
        new Application().Run(new MainWindow(args));
    }
}