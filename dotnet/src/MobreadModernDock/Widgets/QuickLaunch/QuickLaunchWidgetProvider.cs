using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;
using MobreadModernDock.ViewModels;

namespace MobreadModernDock.Widgets.QuickLaunch;

public static class QuickLaunchSettings
{
    /// <summary>JSON array of {label, path, args}.</summary>
    public const string Items = "items";
    public const string Columns = "columns";
    public const string IconSize = "iconSize";
    public const string ShowLabels = "showLabels";
}

public sealed record QuickLaunchEntry(string Label, string Path, string? Args)
{
    public static List<QuickLaunchEntry> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<QuickLaunchEntry>>(json, JsonOpts) ?? new(); }
        catch { return new(); }
    }
    public static string Serialize(IEnumerable<QuickLaunchEntry> items) => JsonSerializer.Serialize(items, JsonOpts);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
}

/// <summary>A grid of launchers separate from the dock — a second, denser tier of shortcuts.</summary>
public sealed class QuickLaunchWidgetProvider : IWidgetProvider
{
    public string TypeKey => WidgetTypes.QuickLaunch;
    public string DisplayNameKey => "widget.type.quicklaunch";

    public Dictionary<string, string> DefaultSettings() => new()
    {
        [QuickLaunchSettings.Items] = "[]",
        [QuickLaunchSettings.Columns] = "4",
        [QuickLaunchSettings.IconSize] = "32",
        [QuickLaunchSettings.ShowLabels] = "false",
    };

    public Control CreateView(WidgetDefinition definition, AppServices services)
    {
        var vm = new QuickLaunchViewModel(definition, services);
        var grid = new ItemsControl
        {
            DataContext = vm, Margin = new Thickness(4),
            ItemsSource = vm.Items,
            ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new UniformGrid
            {
                [!UniformGrid.ColumnsProperty] = new Avalonia.Data.Binding(nameof(QuickLaunchViewModel.Columns)),
            }),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<QuickLaunchItemViewModel>((item, _) =>
            {
                var img = new Image
                {
                    Source = item.Icon, Stretch = Stretch.Uniform,
                    [!Layoutable.WidthProperty] = new Avalonia.Data.Binding(nameof(QuickLaunchViewModel.IconSize)) { Source = vm },
                    [!Layoutable.HeightProperty] = new Avalonia.Data.Binding(nameof(QuickLaunchViewModel.IconSize)) { Source = vm },
                };
                RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);
                var fallback = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(6), IsVisible = item.Icon == null,
                    [!Layoutable.WidthProperty] = new Avalonia.Data.Binding(nameof(QuickLaunchViewModel.IconSize)) { Source = vm },
                    [!Layoutable.HeightProperty] = new Avalonia.Data.Binding(nameof(QuickLaunchViewModel.IconSize)) { Source = vm },
                    Child = new TextBlock { Text = item.Glyph, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                var label = new TextBlock
                {
                    Text = item.Label, FontSize = 10, Foreground = Brushes.White, TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 72, Margin = new Thickness(0, 2, 0, 0),
                    [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(QuickLaunchViewModel.ShowLabels)) { Source = vm },
                };
                var btn = new Button
                {
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Center, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    Content = new StackPanel { Children = { new Panel { Children = { fallback, img } }, label } },
                    [ToolTip.TipProperty] = item.Label,
                };
                btn.Click += (_, _) => item.Launch();
                return btn;
            }, supportsRecycling: false),
        };
        // Empty grid still needs a grabbable footprint.
        var empty = new TextBlock
        {
            Text = services.LocalizationService.Text("widget.quicklaunch.empty"), Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            FontSize = 11, Margin = new Thickness(10, 6), [!Visual.IsVisibleProperty] = new Avalonia.Data.Binding(nameof(QuickLaunchViewModel.IsEmpty)),
        };
        return new Panel { DataContext = vm, Children = { empty, grid } };
    }

    public Control? CreateSettingsView(WidgetDefinition definition, AppServices services, Action onChanged)
    {
        var loc = services.LocalizationService;
        var widgets = services.WidgetService;
        var panel = new StackPanel { Spacing = 8 };
        var items = QuickLaunchEntry.Parse(definition.GetSetting(QuickLaunchSettings.Items, "[]"));

        var list = new StackPanel { Spacing = 2 };
        void Save()
        {
            widgets.UpdateSetting(definition.Id, QuickLaunchSettings.Items, QuickLaunchEntry.Serialize(items));
            RebuildList();
            onChanged();
        }
        void RebuildList()
        {
            list.Children.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                var e = items[i];
                var row = new DockPanel { LastChildFill = true };
                var remove = new Button { Content = "✕", Padding = new Thickness(6, 0), FontSize = 11, Margin = new Thickness(6, 0, 0, 0) };
                remove.Click += (_, _) => { items.RemoveAt(idx); Save(); };
                var up = new Button { Content = "▲", Padding = new Thickness(6, 0), FontSize = 9, IsEnabled = idx > 0 };
                up.Click += (_, _) => { (items[idx - 1], items[idx]) = (items[idx], items[idx - 1]); Save(); };
                var down = new Button { Content = "▼", Padding = new Thickness(6, 0), FontSize = 9, IsEnabled = idx < items.Count - 1, Margin = new Thickness(2, 0, 0, 0) };
                down.Click += (_, _) => { (items[idx + 1], items[idx]) = (items[idx], items[idx + 1]); Save(); };
                DockPanel.SetDock(remove, Dock.Right); DockPanel.SetDock(down, Dock.Right); DockPanel.SetDock(up, Dock.Right);
                row.Children.Add(remove); row.Children.Add(down); row.Children.Add(up);
                row.Children.Add(new TextBlock { Text = e.Label, Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, [ToolTip.TipProperty] = e.Path });
                list.Children.Add(row);
            }
        }
        RebuildList();

        var addProgram = new Button { Content = loc.Text("settings.widget.quicklaunch.addProgram") };
        addProgram.Click += async (_, _) =>
        {
            var window = TopLevel.GetTopLevel(addProgram) as Window;
            if (window == null) return;
            var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                AllowMultiple = true,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Programs & shortcuts") { Patterns = new[] { "*.exe", "*.lnk" } } },
            });
            bool added = false;
            foreach (var f in files)
            {
                var entry = BuildEntry(f.Path.LocalPath);
                if (entry == null) continue;
                services.IconGateway.CacheProgramIcon(entry.Path);
                items.Add(entry); added = true;
            }
            if (added) Save();
        };
        var addFolder = new Button { Content = loc.Text("settings.widget.quicklaunch.addFolder") };
        addFolder.Click += async (_, _) =>
        {
            var window = TopLevel.GetTopLevel(addFolder) as Window;
            if (window == null) return;
            var folders = await window.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions());
            if (folders.Count == 0) return;
            string p = folders[0].Path.LocalPath;
            string label = Path.GetFileName(Path.TrimEndingDirectorySeparator(p));
            if (string.IsNullOrWhiteSpace(label)) label = p;
            services.IconGateway.CacheFolderIcon(p);
            items.Add(new QuickLaunchEntry(label, p, null));
            Save();
        };
        panel.Children.Add(Text.TextWidgetProvider.Section(loc.Text("settings.widget.quicklaunch.items"), null,
            new StackPanel { Spacing = 6, Children = { list, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { addProgram, addFolder } } } }));

        var cols = new Slider { Minimum = 1, Maximum = 8, TickFrequency = 1, IsSnapToTickEnabled = true, Value = definition.GetSettingInt(QuickLaunchSettings.Columns, 4) };
        cols.ValueChanged += (_, _) => { widgets.UpdateSetting(definition.Id, QuickLaunchSettings.Columns, ((int)cols.Value).ToString()); onChanged(); };
        panel.Children.Add(Text.TextWidgetProvider.Section(loc.Text("settings.widget.quicklaunch.columns"), null, cols));

        var size = new Slider { Minimum = 16, Maximum = 64, TickFrequency = 4, IsSnapToTickEnabled = true, Value = definition.GetSettingInt(QuickLaunchSettings.IconSize, 32) };
        size.ValueChanged += (_, _) => { widgets.UpdateSetting(definition.Id, QuickLaunchSettings.IconSize, ((int)size.Value).ToString()); onChanged(); };
        panel.Children.Add(Text.TextWidgetProvider.Section(loc.Text("settings.widget.quicklaunch.iconSize"), null, size));

        var labels = new CheckBox { Content = loc.Text("settings.widget.quicklaunch.labels"), IsChecked = definition.GetSettingBool(QuickLaunchSettings.ShowLabels, false), Foreground = Brushes.LightGray };
        labels.IsCheckedChanged += (_, _) => { widgets.UpdateSetting(definition.Id, QuickLaunchSettings.ShowLabels, (labels.IsChecked == true).ToString()); onChanged(); };
        panel.Children.Add(labels);
        return panel;
    }

    /// <summary>Same shortcut handling as "Add Program" on the dock.</summary>
    private static QuickLaunchEntry? BuildEntry(string path)
    {
        if (Infrastructure.Windows.Native.ShellLinkResolver.IsShortcut(path))
        {
            var link = Infrastructure.Windows.Native.ShellLinkResolver.Resolve(path);
            if (link == null || string.IsNullOrWhiteSpace(link.TargetPath)) return null;
            if (!link.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(link.TargetPath)) return null;
            var sel = ProgramSelectionResolver.Resolve(link.TargetPath);
            return new QuickLaunchEntry(Path.GetFileNameWithoutExtension(path), sel.ExecutablePath, string.IsNullOrWhiteSpace(link.Arguments) ? null : link.Arguments);
        }
        var resolved = ProgramSelectionResolver.Resolve(path);
        return new QuickLaunchEntry(resolved.Label, resolved.ExecutablePath, null);
    }
}

public sealed class QuickLaunchItemViewModel
{
    private readonly QuickLaunchEntry _entry;
    private readonly AppServices _services;
    public string Label => _entry.Label;
    public Bitmap? Icon { get; }
    public string Glyph => string.IsNullOrEmpty(_entry.Label) ? "?" : _entry.Label[..1].ToUpperInvariant();

    public QuickLaunchItemViewModel(QuickLaunchEntry entry, AppServices services)
    {
        _entry = entry; _services = services;
        string? iconPath;
        if (Directory.Exists(entry.Path))
        {
            services.IconGateway.CacheFolderIcon(entry.Path);
            iconPath = services.IconGateway.ResolveFolderIcon(entry.Path);
        }
        else
        {
            // Resolve* only maps to the cache path; extract if it isn't there yet
            // (config edited by hand, or cache cleared).
            services.IconGateway.CacheProgramIcon(entry.Path);
            iconPath = services.IconGateway.ResolveProgramIcon(entry.Path);
            if (iconPath == null || !File.Exists(iconPath)) iconPath = services.IconGateway.ResolveFileIcon(entry.Path);
        }
        Icon = IconLoader.LoadFromFile(iconPath);
    }

    public void Launch()
    {
        DockItem item = Directory.Exists(_entry.Path)
            ? new DockFolderItemModel(_entry.Label, _entry.Path)
            : new DockProgramItemModel(_entry.Label, _entry.Path, _entry.Args);
        _services.ItemActionService.Execute(item, () => { });
    }
}

public sealed class QuickLaunchViewModel : WidgetViewModelBase
{
    private readonly WidgetDefinition _definition;
    private readonly AppServices _services;
    public ObservableCollection<QuickLaunchItemViewModel> Items { get; } = new();

    private int _columns = 4;
    public int Columns { get => _columns; set => SetProperty(ref _columns, value); }
    private double _iconSize = 32;
    public double IconSize { get => _iconSize; set => SetProperty(ref _iconSize, value); }
    private bool _showLabels;
    public bool ShowLabels { get => _showLabels; set => SetProperty(ref _showLabels, value); }
    private bool _isEmpty = true;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    public QuickLaunchViewModel(WidgetDefinition definition, AppServices services)
    {
        _definition = definition; _services = services;
        Refresh();
    }

    public override void Refresh()
    {
        Columns = Math.Clamp(_definition.GetSettingInt(QuickLaunchSettings.Columns, 4), 1, 12);
        IconSize = _definition.GetSettingInt(QuickLaunchSettings.IconSize, 32);
        ShowLabels = _definition.GetSettingBool(QuickLaunchSettings.ShowLabels, false);
        var entries = QuickLaunchEntry.Parse(_definition.GetSetting(QuickLaunchSettings.Items, "[]"));
        Items.Clear();
        foreach (var e in entries) Items.Add(new QuickLaunchItemViewModel(e, _services));
        IsEmpty = Items.Count == 0;
    }
}
