using System;
using System.Windows;
using Windows.Foundation;
using System.Windows.Media;
using System.Windows.Interop;
using System.Threading.Tasks;
using System.Windows.Controls;
using Windows.Management.Deployment;
using System.Runtime.InteropServices;
using System.Collections.ObjectModel;

sealed class Window : System.Windows.Window
{
    readonly Canvas _canvas = new() { Width = 381, Height = 115 };

    readonly TextBlock _textBlock1 = new() { Foreground = Brushes.White };

    readonly TextBlock _textBlock2 = new() { Text = "Preparing...", Foreground = Brushes.White };

    readonly ProgressBar _progressBar = new()
    {
        Width = 359,
        Height = 23,
        BorderThickness = default,
        IsIndeterminate = true,
        Foreground = new SolidColorBrush(Color.FromRgb(0, 133, 66)),
        Background = new SolidColorBrush(Color.FromRgb(14, 14, 14))
    };

    readonly string _text;

    Request? _request = null;

    readonly Product[] _products;

    public Window(bool value)
    {
        Title = "Bedrock Updater";
        Icon = global::Resources.GetImageSource("Application.ico");

        UseLayoutRounding = true;
        ResizeMode = ResizeMode.NoResize;

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;

        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));

        Content = _canvas;

        _canvas.Children.Add(_textBlock1);
        _canvas.Children.Add(_textBlock2);
        _canvas.Children.Add(_progressBar);

        Canvas.SetLeft(_textBlock1, 11);
        Canvas.SetTop(_textBlock1, 15);
        Canvas.SetLeft(_textBlock2, 11);
        Canvas.SetTop(_textBlock2, 84);
        Canvas.SetLeft(_progressBar, 11);
        Canvas.SetTop(_progressBar, 46);

        _text = value ? "Updating Preview..." : "Updating Release...";
        _products = [Product.Xbox, value ? Product.Preview : Product.Release];
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (_request is not null)
            using (_request) _request.Cancel();

        Environment.Exit(0);
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        var length = _products.Length;
        for (var index = 0; index < length; index++)
        {
            _textBlock1.Text = $"{_text} {index + 1} / {length}";

            var product = _products[index];
            _request = await Store.GetAsync(product, Action);

            if (_request is not null)
            {
                using (_request) await _request;
                _request = null;
            }

            _progressBar.Value = 0;
            _textBlock2.Text = $"Preparing...";
            _progressBar.IsIndeterminate = true;
        }

        Close();
    }

    void Action(double _) => Dispatcher.Invoke(() =>
    {
        if (_progressBar.Value == _)
            return;

        _progressBar.Value = _;
        _textBlock2.Text = $"Preparing... {_}%";

        if (_progressBar.IsIndeterminate)
            _progressBar.IsIndeterminate = false;
    });
}