using System;
using System.Linq;
using static PInvoke;
using System.Windows;
using System.Security;
using System.Threading;
using System.Globalization;
using System.Windows.Interop;
using System.Runtime.ExceptionServices;

static class Program
{
    static readonly Application _application = new();

    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += [SecurityCritical, HandleProcessCorruptedStateExceptions] (sender, args) =>
        {
            nint handle = new();
            var window = _application.MainWindow;

            if (window is not null)
            {
                WindowInteropHelper helper = new(window);
                handle = helper.EnsureHandle();
            }

            var exception = (Exception)args.ExceptionObject;
            while (exception.InnerException is not null) exception = exception.InnerException;

            var title = handle > 0 ? "Error" : "Bedrock Updater";
            ShellMessageBox(default, handle, exception.Message, title, MB_ICONERROR);
            Environment.Exit(0);
        };

        using Mutex mutex = new(true, "C7B58EAD-356C-40A1-A145-7262C3C04D00", out bool value); if (!value) return;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        value = args.Any(_ => _.Equals("/preview", StringComparison.OrdinalIgnoreCase));
        _application.Run(new Window(value));
    }
}