using System;
using System.Linq;
using System.Windows;
using System.Threading;
using System.Globalization;

static class Program
{

    [STAThread]
    static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        using Mutex mutex = new(true, "C7B58EAD-356C-40A1-A145-7262C3C04D00", out bool createdNew); if (!createdNew) return;
        new Application().Run(new MainWindow(args.FirstOrDefault()?.Equals("/preview", StringComparison.OrdinalIgnoreCase) ?? false));
    }
}