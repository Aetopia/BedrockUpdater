using System;
using System.IO;
using System.Net;
using System.Windows;
using Windows.Foundation;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Windows.Controls;
using Windows.Management.Deployment;
using System.Windows.Forms.Integration;

class MainWindow : Window
{
    enum Units { B, KB, MB, GB }

    static string Format(float bytes)
    {
        int value = 0;
        while (bytes >= 1024f) { bytes /= 1024f; value++; }
        return string.Format($"{bytes:0.00} {(Units)value}");
    }

    internal MainWindow(bool preview)
    {
        UseLayoutRounding = true;
        Icon = global::Resources.LoadImageSource(".ico");
        Title = preview ? "Bedrock Updater Preview" : "Bedrock Updater";
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Closed += (sender, e) => Environment.Exit(0);

        Grid grid1 = new() { Width = 1000, Height = 600 };
        Content = grid1;

        WindowsFormsHost host = new()
        {
            Child = new System.Windows.Forms.WebBrowser { ScrollBarsEnabled = false, DocumentText = global::Resources.LoadString("Document.html") },
            IsEnabled = false
        };
        Grid.SetRow(host, 0);
        grid1.RowDefinitions.Add(new());
        grid1.Children.Add(host);

        Grid grid2 = new() { Margin = new(10, 0, 10, 10) };
        Grid.SetRow(grid2, 1);
        grid1.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid1.Children.Add(grid2);

        ProgressBar progressBar = new()
        {
            Height = 32,
            BorderThickness = default,
            IsIndeterminate = true,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 133, 66)),
            Background = new SolidColorBrush(Color.FromRgb(14, 14, 14))
        };
        grid2.Children.Add(progressBar);

        TextBlock textBlock1 = new()
        {
            Text = "Connecting...",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(16, 0, 0, 1),
            Foreground = Brushes.White
        };
        grid2.Children.Add(textBlock1);

        TextBlock textBlock2 = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 0, 16, 1),
            Foreground = Brushes.White
        };
        grid2.Children.Add(textBlock2);

        using WebClient client = new();
        string value = default;

        client.DownloadProgressChanged += (sender, e) =>
        {
            var text = $"Downloading {Format(e.BytesReceived)} / {value ??= Format(e.TotalBytesToReceive)}";
            Dispatcher.Invoke(() =>
            {
                textBlock1.Text = text;
                if (progressBar.Value != e.ProgressPercentage) progressBar.Value = e.ProgressPercentage;
            });
        };

        client.DownloadFileCompleted += (sender, e) =>
        {
            value = default;
            Dispatcher.Invoke(() =>
            {
                progressBar.Value = 0;
                textBlock1.Text = "Installing...";
            });
        };

        Uri packageUri = default;
        IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> operation = default;

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            client.CancelAsync();
            while (client.IsBusy) ;
            operation?.Cancel();
            NativeMethods.DeleteFile(packageUri?.AbsolutePath);
            foreach (var package in Store.PackageManager.FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Framework))
                _ = Store.PackageManager.RemovePackageAsync(package.Id.FullName);
        };

        ContentRendered += async (sender, e) => await Task.Run(() =>
        {
            foreach (var product in Store.GetProducts("9WZDNCRD1HKW", preview ? "9P5X4QVLC2XR" : "9NBLGGH2JHXJ"))
            {
                Dispatcher.Invoke(() =>
                {
                    progressBar.IsIndeterminate = true;
                    textBlock1.Text = $"Updating {product.Title}...";
                    textBlock2.Text = default;
                });
                var updates = Store.GetUpdates(product);

                if (updates is null) continue;
                Dispatcher.Invoke(() => progressBar.IsIndeterminate = false);
                for (int i = 0; i < updates.Count; i++)
                {
                    Dispatcher.Invoke(() =>
                    {
                        textBlock1.Text = "Downloading...";
                        textBlock2.Text = $"{i + 1} of {updates.Count}";
                        progressBar.Value = 0;
                    });

                    try
                    {
                        client.DownloadFileTaskAsync(Store.GetUrl(updates[i]), (packageUri = new(Path.GetTempFileName())).AbsolutePath).Wait();
                        operation = Store.PackageManager.AddPackageAsync(packageUri, null, DeploymentOptions.ForceApplicationShutdown);
                        operation.Progress += (sender, e) => Dispatcher.Invoke(() => { if (progressBar.Value != e.percentage) textBlock1.Text = $"Installing {progressBar.Value = e.percentage}%"; });
                        operation.AsTask().Wait();
                    }
                    finally { NativeMethods.DeleteFile(packageUri.AbsolutePath); }
                }
            }

            NativeMethods.ShellExecute(lpFile: @$"shell:AppsFolder\Microsoft.Minecraft{(preview ? "WindowsBeta" : "UWP")}_8wekyb3d8bbwe!App");
            Dispatcher.Invoke(Close);
        });
    }
}