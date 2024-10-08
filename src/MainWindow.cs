using System;
using System.Windows;
using Windows.Foundation;
using System.Windows.Media;
using System.Windows.Controls;
using Windows.Management.Deployment;

sealed class MainWindow : Window
{
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

        Canvas canvas = new() { Width = 381, Height = 115 };
        Content = canvas;

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

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            operation?.Cancel();
            foreach (var package in Store.PackageManager.FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Framework)) _ = Store.PackageManager.RemovePackageAsync(package.Id.FullName);
        };

        ContentRendered += async (sender, e) => await System.Threading.Tasks.Task.Run(() =>
        {
            AddPackageOptions options = new() { ForceAppShutdown = true };
            foreach (var array in Store.Products("9WZDNCRD1HKW", preview ? "9P5X4QVLC2XR" : "9NBLGGH2JHXJ"))
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

                    operation = Store.PackageManager.AddPackageByUriAsync(new(array[index]), options);
                    operation.Progress += (sender, e) => Dispatcher.Invoke(() =>
                    {
                        if (bar.Value != e.percentage)
                        {
                            if (bar.IsIndeterminate) bar.IsIndeterminate = false;
                            block2.Text = $"{e.state}... {bar.Value = e.percentage}%";
                        }
                    });
                    operation.AsTask().Wait();
                }
                Dispatcher.Invoke(() => { block1.Text = text; block2.Text = "Preparing..."; bar.Value = 0; ; bar.IsIndeterminate = true; });
            }
            Dispatcher.Invoke(Close);
        });
    }
}