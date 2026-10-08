using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidBar.SDK;

namespace WidBarWidget1.ExtensionApp;

public sealed class MainPlugin :
    WidgetPluginBase,
    IWidgetFlyoutLifecycle
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    private readonly CancellationTokenSource _shutdown = new();
    private DispatcherTimer? _timer;
    private TextBlock? _previewText;
    private TextBlock? _flyoutText;

    private BatteryInfo? _mouse;
    private BatteryInfo? _headset;
    private bool _updating;
    private bool _flyoutVisible;
    private bool _disposed;

    public override string Id => "com.example.mywidget";
    public override string Name => "Logitech Battery";

    public override int PreviewLogicalWidth => 190;
    public override int FlyoutWidth => 360;
    public override int FlyoutHeight => 220;
    public override WidgetFlyoutBackdrop FlyoutBackdrop =>
        WidgetFlyoutBackdrop.Acrylic;

    private sealed record BatteryInfo(
        decimal Percent,
        bool Charging,
        string LastUpdate);

    public override async Task InitializeAsync(IWidgetContext context)
    {
        await base.InitializeAsync(context);
        context.PreviewVisibilityChanged += OnPreviewVisibilityChanged;
    }

    public override UIElement? CreatePreviewContent()
    {
        _previewText = new TextBlock
        {
            Text = PreviewText(),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var root = new Grid { Background = null };
        root.Children.Add(_previewText);

        if (_timer is null)
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _timer.Tick += OnTimerTick;
        }

        UpdateTimer();
        _ = RefreshAsync();
        return root;
    }

    public override UIElement? CreateFlyoutContent()
    {
        _flyoutText = new TextBlock
        {
            Text = FlyoutText(),
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap
        };

        var panel = new StackPanel
        {
            Spacing = 12,
            Padding = new Thickness(20)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Logitech Battery",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(_flyoutText);

        return panel;
    }

    private static string Percentage(BatteryInfo? battery)
    {
        if (battery is null)
            return "—";

        string value = Math.Round(battery.Percent)
            .ToString("0", CultureInfo.InvariantCulture);

        return value + "%" + (battery.Charging ? " ⚡" : "");
    }

    private string PreviewText() =>
        $"🖱 {Percentage(_mouse)}   🎧 {Percentage(_headset)}";

    private string FlyoutText() =>
        $"🖱 G502 X PLUS: {Percentage(_mouse)}\n" +
        $"Обновление источника: {_mouse?.LastUpdate ?? "нет данных"}\n\n" +
        $"🎧 PRO X Wireless: {Percentage(_headset)}\n" +
        $"Обновление источника: {_headset?.LastUpdate ?? "нет данных"}";

    private async Task<BatteryInfo?> ReadBatteryAsync(string deviceId)
    {
        try
        {
            string xml = await _http.GetStringAsync(
                $"http://localhost:12321/device/{deviceId}",
                _shutdown.Token);

            var root = XDocument.Parse(xml).Root;
            if (root is null)
                return null;

            if (!decimal.TryParse(
                root.Element("battery_percent")?.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal percent))
            {
                return null;
            }

            if (percent < 0 || percent > 100)
                return null;

            bool.TryParse(
                root.Element("charging")?.Value,
                out bool charging);

            return new BatteryInfo(
                percent,
                charging,
                root.Element("last_update")?.Value ?? "неизвестно");
        }
        catch
        {
            return null;
        }
    }

    private async Task RefreshAsync()
    {
        if (_updating || _disposed)
            return;

        _updating = true;
        try
        {
            var mouseTask = ReadBatteryAsync("dev00000004");
            var headsetTask = ReadBatteryAsync("dev00000003");

            await Task.WhenAll(mouseTask, headsetTask);

            if (_disposed)
                return;

            _mouse = await mouseTask;
            _headset = await headsetTask;
            UpdateText();
        }
        finally
        {
            _updating = false;
        }
    }

    private void UpdateText()
    {
        if (_previewText is not null)
            _previewText.Text = PreviewText();

        if (_flyoutText is not null)
            _flyoutText.Text = FlyoutText();
    }

    private void UpdateTimer()
    {
        if (_disposed || _timer is null)
            return;

        if ((Context?.IsPreviewVisible ?? true) || _flyoutVisible)
            _timer.Start();
        else
            _timer.Stop();
    }

    private void OnPreviewVisibilityChanged(object? sender, bool isVisible)
    {
        UpdateTimer();
        if (isVisible)
            _ = RefreshAsync();
    }

    private async void OnTimerTick(object? sender, object e)
    {
        await RefreshAsync();
    }

    public void OnFlyoutShown()
    {
        _flyoutVisible = true;
        UpdateText();
        UpdateTimer();
        _ = RefreshAsync();
    }

    public void OnFlyoutHidden()
    {
        _flyoutVisible = false;
        UpdateTimer();
    }

    public override ValueTask DisposeAsync()
    {
        _disposed = true;

        if (Context is not null)
            Context.PreviewVisibilityChanged -= OnPreviewVisibilityChanged;

        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
        }

        _shutdown.Cancel();
        _http.Dispose();
        _timer = null;
        _previewText = null;
        _flyoutText = null;

        return ValueTask.CompletedTask;
    }
}
