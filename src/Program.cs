using System;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Runtime.InteropServices;

static class Program
{
    [DllImport("Shell32", CharSet = CharSet.Auto, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int ShellMessageBox(nint hAppInst = default, nint hWnd = default, string lpcText = default, string lpcTitle = "Bedrock Updater", int fuStyle = 0x00000010);

    [STAThread]
    static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var exception = (Exception)e.ExceptionObject;
            while (exception.InnerException != null) exception = exception.InnerException;
            ShellMessageBox(lpcText: exception.Message);
            Environment.Exit(0);
        };
        using Mutex mutex = new(true, "C7B58EAD-356C-40A1-A145-7262C3C04D00", out bool createdNew); if (!createdNew) return;
        new MainWindow(args.FirstOrDefault()?.Equals("/preview", StringComparison.OrdinalIgnoreCase) ?? false).ShowDialog();
    }
}