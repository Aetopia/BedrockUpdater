using System;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;

static class Program
{
    [DllImport("Shlwapi")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern bool IsOS(int dwOS);

    [DllImport("Shell32", CharSet = CharSet.Auto, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int ShellMessageBox(IntPtr hAppInst = default, IntPtr hWnd = default, string lpcText = default, string lpcTitle = "Bedrock Updater", int fuStyle = 0x00000010);

    [STAThread]
    static void Main(string[] args)
    {
        using Mutex mutex = new(true, "C7B58EAD-356C-40A1-A145-7262C3C04D00", out bool createdNew);
        if (Environment.OSVersion.Version < new Version(10, 0, 19041, 0) || IsOS(29) || !createdNew) return;
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var exception = (Exception)e.ExceptionObject;
            while (exception.InnerException != null) exception = exception.InnerException;
            ShellMessageBox(lpcText: exception.Message);
            Environment.Exit(0);
        };
        new MainWindow(args.FirstOrDefault()?.Equals("/preview", StringComparison.OrdinalIgnoreCase) ?? false).ShowDialog();
    }
}