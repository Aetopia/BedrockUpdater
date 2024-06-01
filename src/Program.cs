using System.Runtime.InteropServices;
using System;
using System.Windows.Forms;
using System.Collections.Specialized;
using System.Configuration;

static class Program
{
    [DllImport("Shlwapi")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern bool IsOS(int dwOS);

    static void Main(string[] args)
    {
        using System.Threading.Mutex mutex = new(true, "C7B58EAD-356C-40A1-A145-7262C3C04D00", out bool createdNew);
        if (Environment.OSVersion.Version < new Version(10, 0, 19041, 0) || IsOS(29) || !createdNew) return;
       ((NameValueCollection)ConfigurationManager.GetSection("System.Windows.Forms.ApplicationConfigurationSection"))["DpiAwareness"] = "PerMonitorV2";

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.Run(new MainForm(args));
    }
}