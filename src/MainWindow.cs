using System;
using System.IO;
using System.Net;
using System.Windows;
using Windows.Foundation;
using System.Windows.Media;
using System.Windows.Controls;
using Windows.Management.Deployment;
using System.Runtime.InteropServices;

sealed class MainWindow : Window
{
    enum Unit { B, KB, MB, GB }

    public MainWindow(bool preview)
    {
        Icon = global::Resources.GetImageSource(".ico");
        UseLayoutRounding = true;
        Title = "Bedrock Updater";
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));

        Canvas canvas = new() { Width = 381, Height = 115 };
        Content = canvas;

        TextBlock block1 = new()
        {
            Text = "Updating Minecraft...",
            Foreground = Brushes.White
        };
        canvas.Children.Add(block1); Canvas.SetLeft(block1, 11); Canvas.SetTop(block1, 15);

        TextBlock block2 = new()
        {
            Text = "Preparing...",
            Foreground = Brushes.White
        };
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

        using WebClient client = new();
        string value = default;

        client.DownloadProgressChanged += (sender, e) => Dispatcher.Invoke(() =>
        {
            static string _(float _) { var unit = (int)Math.Log(_, 1024); return $"{_ / Math.Pow(1024, unit):0.00} {(Unit)unit}"; }
            if (bar.Value != e.ProgressPercentage)
            {
                block2.Text = $"Downloading {_(e.BytesReceived)} / {value ??= _(e.TotalBytesToReceive)}";
                bar.Value = e.ProgressPercentage;
            }
        });

        client.DownloadFileCompleted += (sender, e) => Dispatcher.Invoke(() =>
        {
            value = default;
            bar.Value = 0;
            block2.Text = "Installing...";
        });

        Uri packageUri = default;
        IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> operation = default;

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            client.CancelAsync();
            while (client.IsBusy) ;
            operation?.Cancel();
            DeleteFile(packageUri?.AbsolutePath);
            foreach (var package in Store.Manager.FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Framework)) _ = Store.Manager.RemovePackageAsync(package.Id.FullName);
        };

        ContentRendered += async (sender, e) => await System.Threading.Tasks.Task.Run(() =>
        {
            foreach (var array in Store.Products("9WZDNCRD1HKW", preview ? "9P5X4QVLC2XR" : "9NBLGGH2JHXJ"))
            {
                Dispatcher.Invoke(() => bar.IsIndeterminate = array.Length == 0);
                for (int index = 0; index < array.Length; index++)
                {
                    Dispatcher.Invoke(() =>
                    {
                        block1.Text = array.Length != 1 ? $"Updating Minecraft... - {index + 1} / {array.Length}" : "Updating Minecraft...";
                        block2.Text = "Downloading...";
                        bar.Value = 0;
                    });

                    try
                    {
                        client.DownloadFileTaskAsync(array[index], (packageUri = new(Path.GetTempFileName())).LocalPath).Wait();
                        operation = Store.Manager.AddPackageAsync(packageUri, null, DeploymentOptions.ForceApplicationShutdown);
                        operation.Progress += (sender, e) => Dispatcher.Invoke(() => { if (bar.Value != e.percentage) bar.Value = e.percentage; });
                        operation.AsTask().Wait();
                    }
                    finally { DeleteFile(packageUri.LocalPath); }
                }
                Dispatcher.Invoke(() => { block1.Text = "Updating Minecraft..."; block2.Text = "Preparing..."; bar.Value = 0; ; bar.IsIndeterminate = true; });
            }
            Dispatcher.Invoke(Close);
        });
    }

    [DllImport("Kernel32", CharSet = CharSet.Auto, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern bool DeleteFile(string lpFileName);
}