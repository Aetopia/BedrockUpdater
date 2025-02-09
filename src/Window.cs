using System;
using System.Windows;
using Windows.Foundation;
using System.Windows.Media;
using System.Windows.Interop;
using System.Threading.Tasks;
using System.Windows.Controls;
using Windows.Management.Deployment;
using System.Runtime.InteropServices;

sealed class Window : System.Windows.Window
{
    [DllImport("Shell32", CharSet = CharSet.Auto, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int ShellMessageBox(nint hAppInst = default, nint hWnd = default, string lpcText = default, string lpcTitle = "Bedrock Updater", int fuStyle = 0x00000010);

    public Window(bool value)
    {
        Icon = global::Resources.Get<ImageSource>(".ico");
        UseLayoutRounding = true;
        Title = "Bedrock Updater";
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        var text = value ? "Updating Preview..." : "Updating Release...";

        Canvas canvas = new() { Width = 381, Height = 115 };

        Content = canvas;

        TextBlock textBlock1 = new()
        {
            Text = text,
            Foreground = Brushes.White
        };

        canvas.Children.Add(textBlock1);
        Canvas.SetLeft(textBlock1, 11);
        Canvas.SetTop(textBlock1, 15);

        TextBlock textBlock2 = new()
        {
            Text = "Preparing...",
            Foreground = Brushes.White
        };

        canvas.Children.Add(textBlock2);
        Canvas.SetLeft(textBlock2, 11);
        Canvas.SetTop(textBlock2, 84);

        ProgressBar progressBar = new()
        {
            Width = 359,
            Height = 23,
            BorderThickness = default,
            IsIndeterminate = true,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 133, 66)),
            Background = new SolidColorBrush(Color.FromRgb(14, 14, 14))
        };

        canvas.Children.Add(progressBar);
        Canvas.SetLeft(progressBar, 11);
        Canvas.SetTop(progressBar, 46);

        Task<DeploymentResult> task = default;
        IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> operation = default;

        Closed += (_, _) =>
        {
            if (operation is not null)
                using (var handle = ((IAsyncResult)task).AsyncWaitHandle)
                {
                    operation.Cancel();
                    handle.WaitOne();
                }

            foreach (var package in Store.PackageManager.FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Framework))
                _ = Store.PackageManager.RemovePackageAsync(package.Id.FullName);
        };

        Dispatcher.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            var exception = e.Exception;

            while (exception.InnerException is not null)
                exception = exception.InnerException;

            ShellMessageBox(hWnd: new WindowInteropHelper(this).Handle, lpcText: exception.Message);

            Close();
        };

        ContentRendered += async (_, _) => await Task.Run(() =>
        {
            AddPackageOptions options = new() { ForceAppShutdown = true };
            Progress<DeploymentProgress> progress = new((_) => Dispatcher.Invoke(() =>
            {
                if (progressBar.Value != _.percentage && _.state is DeploymentProgressState.Processing)
                {
                    if (progressBar.IsIndeterminate)
                        progressBar.IsIndeterminate = false;
                    textBlock2.Text = $"Preparing... {progressBar.Value = _.percentage}%";
                }
            }));

            foreach (var array in Store.Get("9WZDNCRD1HKW", value ? "9P5X4QVLC2XR" : "9NBLGGH2JHXJ"))
            {
                for (int index = 0; index < array.Length; index++)
                {
                    Dispatcher.Invoke(() =>
                    {
                        textBlock1.Text = $"{text} {index + 1} / {array.Length}";
                        textBlock2.Text = "Preparing...";
                        progressBar.IsIndeterminate = true;
                        progressBar.Value = 0;
                    });

                    (task = (operation = Store.PackageManager.AddPackageByUriAsync(array[index].Value, options)).AsTask(progress)).Wait();
                }

                Dispatcher.Invoke(() =>
                {
                    textBlock1.Text = text;
                    textBlock2.Text = "Preparing...";
                    progressBar.Value = 0;
                    progressBar.IsIndeterminate = true;
                });
            }

            Dispatcher.Invoke(Close);
        });
    }
}