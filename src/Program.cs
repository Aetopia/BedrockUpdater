using System;
using System.Globalization;
using System.Linq;
using System.Threading;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        using Mutex mutex = new(true, "C7B58EAD-356C-40A1-A145-7262C3C04D00", out bool createdNew);
        if (Environment.OSVersion.Version < new Version(10, 0, 19041, 0) || NativeMethods.IsOS(29) || !createdNew) return;
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var exception = (Exception)e.ExceptionObject;
            while (exception.InnerException != null) exception = exception.InnerException;
            NativeMethods.ShellMessageBox(lpcText: exception.Message);
            Environment.Exit(0);
        };
        new MainWindow(args.FirstOrDefault()?.Equals("/preview", StringComparison.OrdinalIgnoreCase) ?? false).ShowDialog();
    }
}