using System;
using System.Linq;
using System.Windows;
using System.Threading;
using System.Globalization;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Security;

static class Program
{
    const uint MB_ICONERROR = 0x00000010;

    [SuppressUnmanagedCodeSecurity]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("Shlwapi", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int ShellMessageBox(nint hAppInst, nint hWnd, string lpcText, string lpcTitle, uint fuStyle);

    static Program() => AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
    {
        nint handle = 0; var exception = (Exception)args.ExceptionObject;
        if (Application.Current?.MainWindow is { } window) handle = new WindowInteropHelper(window).Handle;

        while (exception.InnerException is not null) exception = exception.InnerException;
        ShellMessageBox(0, handle, exception.Message, handle > 0 ? "Error" : "Bedrock Updater", MB_ICONERROR);

        Environment.Exit(1);
    };

    [STAThread]
    static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        using Mutex mutex = new(false, "C7B58EAD-356C-40A1-A145-7262C3C04D00", out var value);
        if (!value) return;

        value = args.Any(_ => _.Equals("/preview", StringComparison.OrdinalIgnoreCase));
        new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }.Run(new Window(value));
    }
}