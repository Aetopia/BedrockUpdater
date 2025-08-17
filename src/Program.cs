using System;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Security;
using System.Runtime.ExceptionServices;
using static PInvoke;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += [SecurityCritical, HandleProcessCorruptedStateExceptions] (sender, args) =>
        {
            var exception = (Exception)args.ExceptionObject;
            while (exception.InnerException is not null) exception = exception.InnerException; 

            ShellMessageBox(default, default, exception.Message, "Bedrock Updater", MB_ICONERROR);
            Environment.Exit(0);
        };

        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        using Mutex mutex = new(true, "C7B58EAD-356C-40A1-A145-7262C3C04D00", out bool value); if (!value) return;

        value = args.Any(_ => _.Equals("/preview", StringComparison.OrdinalIgnoreCase));
        new Application().Run(new Window(value));
    }
}