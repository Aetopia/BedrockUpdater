using System;
using System.Windows;
using System.Threading;
using Windows.Foundation;
using System.Windows.Media;
using System.Windows.Interop;
using System.Threading.Tasks;
using System.Windows.Controls;
using Windows.Management.Deployment;
using System.Runtime.InteropServices;

sealed class MainWindow : Window
{
    [DllImport("Shell32", CharSet = CharSet.Auto, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int ShellMessageBox(nint hAppInst = default, nint hWnd = default, string lpcText = default, string lpcTitle = "Bedrock Updater", int fuStyle = 0x00000010);

    public MainWindow(bool preview)
    {
        Icon = global::Resources.Get<ImageSource>(".ico");
        UseLayoutRounding = true;
        Title = "Bedrock Updater";
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        var text = preview ? "Updating Preview..." : "Updating Release...";

        Canvas canvas = new() { Width = 381, Height = 115 }; Content = canvas;

        TextBlock block1 = new() { Text = text, Foreground = Brushes.White };
        canvas.Children.Add(block1); Canvas.SetLeft(block1, 11); Canvas.SetTop(block1, 15);

        TextBlock block2 = new() { Text = "Preparing...", Foreground = Brushes.White };
        canvas.Children.Add(block2); Canvas.SetLeft(block2, 11); Canvas.SetTop(block2, 84);

        ProgressBar bar = new()
        {
            Width = 359,
            Height = 23,
            BorderThickness = default,
            IsIndeterminate = true,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 133, 66)),
            Background = new SolidColorBrush(Color.FromRgb(14, 14, 14))
        };
        canvas.Children.Add(bar); Canvas.SetLeft(bar, 11); Canvas.SetTop(bar, 46);

        IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> operation = default;

        Application.Current.Exit += (_, _) =>
        {
            if (operation is not null) { operation.Cancel(); SpinWait.SpinUntil(() => operation.Status != AsyncStatus.Started); }
            foreach (var package in Store.PackageManager.FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Framework)) _ = Store.PackageManager.RemovePackageAsync(package.Id.FullName);
        };

        Application.Current.Dispatcher.UnhandledException += (_, e) =>
        {
            e.Handled = true; var exception = e.Exception;
            while (exception.InnerException != null) exception = exception.InnerException;
            ShellMessageBox(hWnd: new WindowInteropHelper(this).Handle, lpcText: exception.Message);
            Application.Current.Shutdown();
        };

        ContentRendered += async (_, _) => await Task.Run(() =>
        {
            AddPackageOptions options = new() { ForceAppShutdown = true };
            Progress<DeploymentProgress> progress = new((_) => Dispatcher.Invoke(() =>
            {
                if (bar.Value != _.percentage && _.state == DeploymentProgressState.Processing)
                {
                    if (bar.IsIndeterminate) bar.IsIndeterminate = false;
                    block2.Text = $"{_.state}... {bar.Value = _.percentage}%";
                }
            }));
            foreach (var array in Store.Get("9WZDNCRD1HKW", preview ? "9P5X4QVLC2XR" : "9NBLGGH2JHXJ"))
            {
                for (int index = 0; index < array.Length; index++)
                {
                    Dispatcher.Invoke(() =>
                    {
                        block1.Text = array.Length != 1 ? $"{text} {index + 1} / {array.Length}" : text;
                        block2.Text = "Preparing...";
                        bar.IsIndeterminate = true;
                        bar.Value = 0;
                    });
                    (operation = Store.PackageManager.AddPackageByUriAsync(new(array[index]), options)).AsTask(progress).Wait();
                }
                Dispatcher.Invoke(() => { block1.Text = text; block2.Text = "Preparing..."; bar.Value = 0; ; bar.IsIndeterminate = true; });
            }
            Dispatcher.Invoke(Close);
        });
    }
}