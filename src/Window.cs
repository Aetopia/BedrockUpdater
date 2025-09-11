using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using Windows.ApplicationModel.Store.Preview.InstallControl;

sealed class Window : System.Windows.Window
{
    enum Unit { B, KB, MB, GB }

    readonly TextBlock _textBlock1 = new() { Foreground = Brushes.White };

    readonly TextBlock _textBlock2 = new() { Text = "Preparing...", Foreground = Brushes.White };

    readonly ProgressBar _progressBar = new()
    {
        Width = 359,
        Height = 23,
        IsIndeterminate = true,
        BorderThickness = new(),
        Foreground = new SolidColorBrush(Color.FromRgb(0, 133, 66)),
        Background = new SolidColorBrush(Color.FromRgb(14, 14, 14))
    };

    Request? _request;

    readonly string _text;

    readonly Product[] _products;

    internal Window(bool value)
    {
        _text = value ? "Updating Preview..." : "Updating Release...";
        _products = [value ? Product.GamingServices : Product.XboxIdentityProvider, value ? Product.MinecraftWindowsBeta : Product.MinecraftUWP];

        Title = "Bedrock Updater";
        Icon = global::Resources.GetImageSource("Application.ico");

        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        ResizeMode = ResizeMode.NoResize;

        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));

        Canvas _canvas = new() { Width = 381, Height = 115 };

        _canvas.Children.Add(_textBlock1);
        _canvas.Children.Add(_textBlock2);
        _canvas.Children.Add(_progressBar);

        Canvas.SetLeft(_textBlock1, 11);
        Canvas.SetTop(_textBlock1, 15);
        Canvas.SetLeft(_textBlock2, 11);
        Canvas.SetTop(_textBlock2, 84);
        Canvas.SetLeft(_progressBar, 11);
        Canvas.SetTop(_progressBar, 46);

        Content = _canvas;
        _textBlock1.Text = _text;
    }

    protected override async void OnContentRendered(EventArgs args)
    {
        base.OnContentRendered(args);

        for (var index = 0; index < _products.Length; index++)
        {
            _textBlock1.Text = $"{_text} {index + 1} / {_products.Length}";

            var product = _products[index];
            _request = await Store.GetAsync(product, Action);

            if (_request is null) continue;
            using (_request) await _request;
            _request = null;

            if (!_progressBar.IsIndeterminate)
                _progressBar.IsIndeterminate = true;

            _progressBar.Value = 0;
            _textBlock2.Text = $"Preparing...";
        }

        Close();
    }

    protected override void OnClosed(EventArgs args)
    {
        base.OnClosed(args);
        using (_request) _request?.Cancel();
        Environment.Exit(0);
    }

    public static string Stringify(double value)
    {
        var x = Math.Abs(value);
        var y = (int)Math.Log(x, 1024);
        return $"{x / Math.Pow(1024, y):#.##} {(Unit)y}";
    }

    void Action(AppInstallStatus args) => Dispatcher.Invoke(() =>
    {
        if (_progressBar.Value == args.PercentComplete) return;
        if (_progressBar.IsIndeterminate) _progressBar.IsIndeterminate = false;

        _progressBar.Value = args.PercentComplete;
        _textBlock2.Text = args.InstallState switch
        {
            AppInstallState.Downloading => $"Preparing... {Stringify(args.BytesDownloaded)} / {Stringify(args.DownloadSizeInBytes)}",
            _ => $"Preparing... {args.PercentComplete}%"
        };
    });
}