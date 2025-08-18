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
    static Program() => AppDomain.CurrentDomain.UnhandledException += UnhandledException;

    [SecurityCritical, HandleProcessCorruptedStateExceptions]
    static void UnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        nint handle = 0;

        var application = Application.Current;
        if (application?.MainWindow is Window window)
        {
            WindowInteropHelper helper = new(window);
            handle = helper.Handle;
        }

        var exception = (Exception)args.ExceptionObject;
        while (exception.InnerException is not null)
            exception = exception.InnerException;

        var title = handle > 0 ? "Error" : "Bedrock Updater";
        ShellMessageBox(0, handle, exception.Message, title, MB_ICONERROR);

        Environment.Exit(1);
    }

    [STAThread]
    static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        using Mutex mutex = new(false, "C7B58EAD-356C-40A1-A145-7262C3C04D00", out bool value);
        if (!value) return;

        value = args.Any(_ => _.Equals("/preview", StringComparison.OrdinalIgnoreCase));
        new Application().Run(new Window(value));
    }
}