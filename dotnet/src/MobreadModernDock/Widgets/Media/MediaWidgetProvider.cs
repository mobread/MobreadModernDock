using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

namespace MobreadModernDock.Widgets.Media;

public static class MediaWidgetSettings
{
    public const string ShowArt = "showArt";
    public const string ShowControls = "showControls";
    public const string ArtSize = "artSize";
    public const string MaxWidth = "maxWidth";
}

/// <summary>Now-playing card: album art, title/artist, prev/play-pause/next.</summary>
public sealed class MediaWidgetProvider : IWidgetProvider
{
    public string TypeKey => WidgetTypes.Media;
    public string DisplayNameKey => "widget.type.media";

    public Dictionary<string, string> DefaultSettings() => new()
    {
        [MediaWidgetSettings.ShowArt] = "true",
        [MediaWidgetSettings.ShowControls] = "true",
        [MediaWidgetSettings.ArtSize] = "48",
        [MediaWidgetSettings.MaxWidth] = "220",
    };

    public Control CreateView(WidgetDefinition definition, AppServices services)
    {
        var vm = new MediaWidgetViewModel(definition, services);

        var art = new Border
        {
            CornerRadius = new CornerRadius(6), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            [!Layoutable.WidthProperty] = new Avalonia.Data.Binding(nameof(MediaWidgetViewModel.ArtSize)),
            [!Layoutable.HeightProperty] = new Avalonia.Data.Binding(nameof(MediaWidgetViewModel.ArtSize)),
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(MediaWidgetViewModel.ShowArt)),
            Child = new Panel
            {
                Children =
                {
                    new TextBlock { Text = "♪", Foreground = Brushes.White, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    new Image { Stretch = Stretch.UniformToFill, [!Image.SourceProperty] = new Avalonia.Data.Binding(nameof(MediaWidgetViewModel.Art)) },
                }
            },
        };

        var title = new TextBlock
        {
            Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(MediaWidgetViewModel.Title)),
        };
        var artist = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(MediaWidgetViewModel.Artist)),
        };

        Button Btn(string glyph, Action click, string enabledProp) =>
            new()
            {
                Content = glyph, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 2), FontSize = 14, Cursor = new Cursor(StandardCursorType.Hand),
                [!InputElement.IsEnabledProperty] = new Avalonia.Data.Binding(enabledProp),
                Command = new Relay(click),
            };
        var play = Btn("⏯", vm.TogglePlayPause, nameof(MediaWidgetViewModel.CanPlayPause));
        play[!ContentControl.ContentProperty] = new Avalonia.Data.Binding(nameof(MediaWidgetViewModel.PlayGlyph));
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Left,
            [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(MediaWidgetViewModel.ShowControls)),
            Children = { Btn("⏮", vm.SkipPrevious, nameof(MediaWidgetViewModel.CanSkipPrevious)), play, Btn("⏭", vm.SkipNext, nameof(MediaWidgetViewModel.CanSkipNext)) },
        };

        var text = new StackPanel
        {
            Spacing = 1, VerticalAlignment = VerticalAlignment.Center,
            [!Layoutable.MaxWidthProperty] = new Avalonia.Data.Binding(nameof(MediaWidgetViewModel.MaxWidth)),
            Children = { title, artist, controls },
        };

        return new StackPanel
        {
            DataContext = vm, Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(6, 4),
            Children = { art, text },
        };
    }

    public Control? CreateSettingsView(WidgetDefinition definition, AppServices services, Action onChanged)
    {
        var loc = services.LocalizationService;
        var widgets = services.WidgetService;
        var panel = new StackPanel { Spacing = 8 };

        foreach (var (key, textKey) in new[] { (MediaWidgetSettings.ShowArt, "settings.widget.media.art"), (MediaWidgetSettings.ShowControls, "settings.widget.media.controls") })
        {
            var cb = new CheckBox { Content = loc.Text(textKey), IsChecked = definition.GetSettingBool(key, true), Foreground = Brushes.LightGray };
            string k = key;
            cb.IsCheckedChanged += (_, _) => { widgets.UpdateSetting(definition.Id, k, (cb.IsChecked == true).ToString()); onChanged(); };
            panel.Children.Add(cb);
        }

        var width = new Slider { Minimum = 140, Maximum = 400, TickFrequency = 10, IsSnapToTickEnabled = true, Value = definition.GetSettingInt(MediaWidgetSettings.MaxWidth, 220) };
        width.ValueChanged += (_, _) => { widgets.UpdateSetting(definition.Id, MediaWidgetSettings.MaxWidth, ((int)width.Value).ToString()); onChanged(); };
        panel.Children.Add(Text.TextWidgetProvider.Section(loc.Text("settings.widget.media.width"), null, width));

        var artSize = new Slider { Minimum = 32, Maximum = 96, TickFrequency = 4, IsSnapToTickEnabled = true, Value = definition.GetSettingInt(MediaWidgetSettings.ArtSize, 48) };
        artSize.ValueChanged += (_, _) => { widgets.UpdateSetting(definition.Id, MediaWidgetSettings.ArtSize, ((int)artSize.Value).ToString()); onChanged(); };
        panel.Children.Add(Text.TextWidgetProvider.Section(loc.Text("settings.widget.media.artSize"), null, artSize));
        return panel;
    }
}

/// <summary>Minimal ICommand so the buttons stay enabled/disabled via binding.</summary>
internal sealed class Relay : System.Windows.Input.ICommand
{
    private readonly Action _run;
    public Relay(Action run) => _run = run;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _run();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

public sealed class MediaWidgetViewModel : WidgetViewModelBase
{
    private readonly WidgetDefinition _definition;
    private readonly AppServices _services;
    private readonly IMediaSessionGateway _gateway;
    private byte[]? _lastArtBytes;

    private string _title = "";
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    private string _artist = "";
    public string Artist { get => _artist; set => SetProperty(ref _artist, value); }
    private Bitmap? _art;
    public Bitmap? Art { get => _art; set => SetProperty(ref _art, value); }
    private string _playGlyph = "▶";
    public string PlayGlyph { get => _playGlyph; set => SetProperty(ref _playGlyph, value); }
    private bool _canPlayPause, _canNext, _canPrev;
    public bool CanPlayPause { get => _canPlayPause; set => SetProperty(ref _canPlayPause, value); }
    public bool CanSkipNext { get => _canNext; set => SetProperty(ref _canNext, value); }
    public bool CanSkipPrevious { get => _canPrev; set => SetProperty(ref _canPrev, value); }
    private bool _showArt = true, _showControls = true;
    public bool ShowArt { get => _showArt; set => SetProperty(ref _showArt, value); }
    public bool ShowControls { get => _showControls; set => SetProperty(ref _showControls, value); }
    private double _artSize = 48, _maxWidth = 220;
    public double ArtSize { get => _artSize; set => SetProperty(ref _artSize, value); }
    public double MaxWidth { get => _maxWidth; set => SetProperty(ref _maxWidth, value); }

    public MediaWidgetViewModel(WidgetDefinition definition, AppServices services)
    {
        _definition = definition;
        _services = services;
        _gateway = services.MediaSessionGateway;
        _gateway.Changed += OnChanged;
        Refresh();
        OnChanged();
    }

    public override void Refresh()
    {
        ShowArt = _definition.GetSettingBool(MediaWidgetSettings.ShowArt, true);
        ShowControls = _definition.GetSettingBool(MediaWidgetSettings.ShowControls, true);
        ArtSize = _definition.GetSettingInt(MediaWidgetSettings.ArtSize, 48);
        MaxWidth = _definition.GetSettingInt(MediaWidgetSettings.MaxWidth, 220);
    }

    private void OnChanged()
    {
        var info = _gateway.GetCurrent();
        Dispatcher.UIThread.Post(() => Apply(info));
    }

    private void Apply(MediaSessionInfo? info)
    {
        if (info == null)
        {
            Title = _services.LocalizationService.Text("widget.media.nothingPlaying");
            Artist = "";
            Art = null; _lastArtBytes = null;
            CanPlayPause = CanSkipNext = CanSkipPrevious = false;
            PlayGlyph = "▶";
            return;
        }
        Title = string.IsNullOrWhiteSpace(info.Title) ? info.SourceApp : info.Title;
        Artist = string.IsNullOrWhiteSpace(info.Album) || info.Album == info.Artist ? info.Artist : $"{info.Artist} — {info.Album}";
        PlayGlyph = info.IsPlaying ? "⏸" : "▶";
        CanPlayPause = info.CanPlayPause;
        CanSkipNext = info.CanSkipNext;
        CanSkipPrevious = info.CanSkipPrevious;

        if (!ReferenceEquals(info.Thumbnail, _lastArtBytes))
        {
            _lastArtBytes = info.Thumbnail;
            Bitmap? bmp = null;
            if (info.Thumbnail != null)
            {
                try { using var ms = new MemoryStream(info.Thumbnail); bmp = new Bitmap(ms); }
                catch { bmp = null; }
            }
            var old = Art;
            Art = bmp;
            old?.Dispose();
        }
    }

    public void TogglePlayPause() => _gateway.TogglePlayPause();
    public void SkipNext() => _gateway.SkipNext();
    public void SkipPrevious() => _gateway.SkipPrevious();

    public override void Shutdown()
    {
        _gateway.Changed -= OnChanged;
        Art?.Dispose();
        Art = null;
    }
}
