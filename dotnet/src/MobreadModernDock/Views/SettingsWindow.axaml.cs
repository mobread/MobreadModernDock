using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;
using MobreadModernDock.ViewModels;

namespace MobreadModernDock.Views;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;

    private const double DragThresholdPixels = 4;

    private SettingsViewModel? _vm;
    private AppServices? _appServices;
    private Action? _dockRefreshAction;

    private Point _pressPoint;
    private int _dragSourceIndex = -1;
    private bool _dragInProgress;

    public SettingsWindow()
    {
        InitializeComponent();
        // ListBoxItem marks PointerPressed as handled while it updates the
        // selection, which would skip a normal (bubble) handler on the ListBox.
        // Subscribe with handledEventsToo so the drag gesture still sees presses.
        ItemsList.AddHandler(
            InputElement.PointerPressedEvent,
            OnItemsPointerPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    public static void Open(AppServices appServices, Window owner,
        Action dockRefreshAction, Action<DockPositioningMode> positioningModeChangeAction)
    {
        // Only one settings window at a time: focus the existing one.
        if (_instance != null)
        {
            _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            return;
        }

        var vm = new SettingsViewModel(appServices, dockRefreshAction, positioningModeChangeAction);
        var window = new SettingsWindow
        {
            DataContext = vm, _vm = vm,
            _appServices = appServices, _dockRefreshAction = dockRefreshAction
        };
        _instance = window;
        window.Closed += (_, _) => _instance = null;
        vm.Initialize();
        window.InitializeWidgetsTab();
        window.Show(owner);
    }

    private SettingsViewModel Vm => _vm ??= (DataContext as SettingsViewModel)!;

    private void OnClosed(object? sender, EventArgs e) => Vm?.Shutdown();

    private async void OnAddProgram(object? sender, RoutedEventArgs e) => await Vm?.AddProgramAsync(this)!;
    private async void OnAddFolder(object? sender, RoutedEventArgs e) => await Vm?.AddFolderAsync(this)!;
    private void OnImportTaskbar(object? sender, RoutedEventArgs e) => Vm?.ImportFromTaskbar();
    private void OnApplyPreset(object? sender, RoutedEventArgs e) => Vm?.ApplySelectedPreset();
    private void OnDeletePreset(object? sender, RoutedEventArgs e) => Vm?.DeleteSelectedPreset();
    private void OnSavePreset(object? sender, RoutedEventArgs e) => Vm?.SaveCurrentAsPreset();
    private async void OnCheckUpdates(object? sender, RoutedEventArgs e) { if (Vm != null) await Vm.CheckForUpdatesAsync(); }
    private void OnDownloadUpdate(object? sender, RoutedEventArgs e) => Vm?.OpenUpdateDownload();

    private async void OnAddModule(object? sender, RoutedEventArgs e)
    {
        if (_appServices != null && _dockRefreshAction != null)
        {
            await AddWindowsModulesWindow.Open(_appServices, _dockRefreshAction, this);
            // Refresh the dock-items list after the modal closes: adding a
            // module must show up immediately, not only after reopening.
            Vm?.RefreshItemLabels();
        }
    }

    private void OnRemove(object? sender, RoutedEventArgs e) => Vm?.RemoveSelected();
    private void OnMoveUp(object? sender, RoutedEventArgs e) => Vm?.MoveItemUp();
    private void OnMoveDown(object? sender, RoutedEventArgs e) => Vm?.MoveItemDown();

    // --- Drag-to-reorder the dock items list ---

    private void OnItemsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // The pressed row must be resolved from the event source, not
        // SelectedIndex: ListBoxItem handles the press for selection (marking
        // the event handled), so our ListBox-level handler runs afterwards via
        // handledEventsToo and the selection binding may not be committed yet.
        if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>() is { } container)
        {
            _dragSourceIndex = ItemsList.IndexFromContainer(container);
            if (_dragSourceIndex < 0) return;
            _pressPoint = e.GetPosition(ItemsList);
            _dragInProgress = false;
        }
    }

    private async void OnItemsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragSourceIndex < 0 || _dragInProgress) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var current = e.GetPosition(ItemsList);
        double dx = current.X - _pressPoint.X;
        double dy = current.Y - _pressPoint.Y;
        if (Math.Abs(dx) < DragThresholdPixels && Math.Abs(dy) < DragThresholdPixels)
            return;

        // Only reordering within this list is allowed (no cross-window drops).
        _dragInProgress = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(_dragSourceIndex.ToString()));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        _dragInProgress = false;
        _dragSourceIndex = -1;
        HideDropIndicator();
    }

    private void OnItemsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // No drag was ever started: clear the pending press state.
        _dragSourceIndex = -1;
        _dragInProgress = false;
    }

    private void OnItemsPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragSourceIndex = -1;
        _dragInProgress = false;
        HideDropIndicator();
    }

    private void OnItemsDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.Text))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Move;
        ShowDropIndicatorAt(e.GetPosition(ItemsList));
        e.Handled = true;
    }

    private void OnItemsDragLeave(object? sender, DragEventArgs e) => HideDropIndicator();

    private void OnItemsDrop(object? sender, DragEventArgs e)
    {
        HideDropIndicator();
        string? sourceText = e.DataTransfer.TryGetText();
        if (sourceText is null || !int.TryParse(sourceText, out int fromIndex)) return;

        var (gapIndex, _) = ResolveDropGap(e.GetPosition(ItemsList));
        Vm?.MoveItem(fromIndex, gapIndex);
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>
    /// Resolves the pointer position inside the (possibly scrolled) list to a
    /// "gap index" (0..Count) plus the screen Y of that gap. Works with
    /// virtualized containers: it walks the realized rows, uses each container's
    /// true index (so scrolled items keep their real position), and splits each
    /// row at its vertical midpoint to decide before/after.
    /// </summary>
    private (int GapIndex, double GapY) ResolveDropGap(Point position)
    {
        int count = Vm?.ItemEntries.Count ?? 0;
        if (count == 0) return (0, 0);

        // Collect realized rows: index + viewport-relative top/height.
        var rows = new List<(int Index, double Top, double Height)>();
        foreach (var container in ItemsList.GetRealizedContainers())
        {
            if (container is not Control c) continue;
            int index = ItemsList.IndexFromContainer(c);
            if (index < 0) continue;
            if (c.TranslatePoint(new Point(0, 0), ItemsList) is not Point topLeft) continue;
            double height = c.Bounds.Height;
            if (height <= 0) continue;
            rows.Add((index, topLeft.Y, height));
        }
        if (rows.Count == 0) return (0, 0);
        rows.Sort((a, b) => a.Index.CompareTo(b.Index));

        // Above the first realized row: gap at its index (its top line).
        var first = rows[0];
        if (position.Y < first.Top)
            return (first.Index, first.Top);

        // Inside a realized row: split at the midpoint.
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            double bottom = row.Top + row.Height;
            if (position.Y <= bottom)
            {
                double mid = row.Top + row.Height / 2;
                return position.Y <= mid
                    ? (row.Index, row.Top)
                    : (row.Index + 1, bottom);
            }
        }

        // Below the last realized row: gap right after it.
        var last = rows[^1];
        return (Math.Min(last.Index + 1, count), last.Top + last.Height);
    }

    private void ShowDropIndicatorAt(Point position)
    {
        if (DropIndicator is null) return;
        var (_, gapY) = ResolveDropGap(position);
        DropIndicator.Margin = new Thickness(0, gapY, 0, 0);
        DropIndicator.IsVisible = true;
    }

    private void HideDropIndicator()
    {
        if (DropIndicator is null) return;
        DropIndicator.IsVisible = false;
    }

    private void OnAcknowledgements(object? sender, RoutedEventArgs e)
    {
        if (_appServices != null)
            AcknowledgementsWindow.Open(this, _appServices.LocalizationService);
    }

    // --- Widgets tab ---

    private sealed record WidgetListEntry(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record WidgetTypeEntry(string TypeKey, string Label)
    {
        public override string ToString() => Label;
    }

    private bool _widgetsInitialized;
    private bool _suppressWidgetEvents;

    /// <summary>Populates the type combo and the widget list; safe to call repeatedly.</summary>
    private void InitializeWidgetsTab()
    {
        if (_appServices == null) return;
        if (!_widgetsInitialized)
        {
            _widgetsInitialized = true;
            foreach (var provider in App.WidgetRegistry.All)
                WidgetTypeCombo.Items.Add(new WidgetTypeEntry(provider.TypeKey, Vm.WidgetTypeName(provider.TypeKey)));
            if (WidgetTypeCombo.Items.Count > 0) WidgetTypeCombo.SelectedIndex = 0;
        }
        RefreshWidgetList();
    }

    private void RefreshWidgetList(string? selectId = null)
    {
        if (_appServices == null) return;
        string? previous = selectId ?? (WidgetsList.SelectedItem as WidgetListEntry)?.Id;
        _suppressWidgetEvents = true;
        WidgetsList.Items.Clear();
        int index = 0, selectIndex = -1;
        foreach (var def in _appServices.WidgetService.GetWidgets())
        {
            string label = Vm.WidgetTypeName(def.Type);
            if (def.Type == Core.Application.WidgetTypes.Text)
            {
                string preview = Core.Application.WidgetService.ResolveText(
                    def.GetSetting(Core.Application.TextWidgetSettings.Template, "{host}"));
                if (!string.IsNullOrEmpty(preview)) label += $" — {preview}";
            }
            if (!def.Enabled) label += "  (off)";
            WidgetsList.Items.Add(new WidgetListEntry(def.Id, label));
            if (def.Id == previous) selectIndex = index;
            index++;
        }
        _suppressWidgetEvents = false;
        WidgetsList.SelectedIndex = selectIndex;
        ShowWidgetSettings((WidgetsList.SelectedItem as WidgetListEntry)?.Id);
    }

    private void OnWidgetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressWidgetEvents) return;
        ShowWidgetSettings((WidgetsList.SelectedItem as WidgetListEntry)?.Id);
    }

    private void ShowWidgetSettings(string? id)
    {
        var def = id != null ? _appServices?.WidgetService.Find(id) : null;
        bool has = def != null;
        RemoveWidgetButton.IsEnabled = has;
        WidgetEnabledCheck.IsEnabled = has;
        WidgetNoSelection.IsVisible = !has;

        _suppressWidgetEvents = true;
        WidgetEnabledCheck.IsChecked = def?.Enabled ?? false;
        _suppressWidgetEvents = false;

        if (def == null || _appServices == null)
        {
            WidgetSettingsHost.Content = null;
            return;
        }
        var provider = App.WidgetRegistry.Get(def.Type);
        var typePanel = provider?.CreateSettingsView(def, _appServices,
            onChanged: () => RefreshWidgetListLabelOnly(def.Id));

        // Opacity is common to every widget type: follow the global slider
        // (default) or pick a per-widget value.
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(BuildWidgetOpacitySection(def));
        panel.Children.Add(BuildWidgetLayerSection(def));
        panel.Children.Add(BuildWidgetAutoHideSection(def));
        if (typePanel != null) panel.Children.Add(typePanel);
        WidgetSettingsHost.Content = panel;
    }

    /// <summary>Per-widget always-on-top: follow the global toggle, or force above / desktop layer.</summary>
    private Control BuildWidgetLayerSection(Core.Models.WidgetDefinition def)
    {
        var vm = Vm;
        var widgets = _appServices!.WidgetService;
        string mode = def.GetSetting(Core.Application.CommonWidgetSettings.LayerMode, Core.Application.CommonWidgetSettings.LayerModeGlobal);
        string group = "layer-" + def.Id;
        var follow = new RadioButton { Content = vm.WidgetLayerFollowGlobal, IsChecked = mode == Core.Application.CommonWidgetSettings.LayerModeGlobal, Foreground = Avalonia.Media.Brushes.White, GroupName = group };
        var top = new RadioButton { Content = vm.WidgetLayerTop, IsChecked = mode == Core.Application.CommonWidgetSettings.LayerModeTop, Foreground = Avalonia.Media.Brushes.White, GroupName = group };
        var desktop = new RadioButton { Content = vm.WidgetLayerDesktop, IsChecked = mode == Core.Application.CommonWidgetSettings.LayerModeDesktop, Foreground = Avalonia.Media.Brushes.White, GroupName = group };
        void Set(RadioButton rb, string value) => rb.IsCheckedChanged += (_, _) =>
        {
            if (rb.IsChecked == true) widgets.UpdateSetting(def.Id, Core.Application.CommonWidgetSettings.LayerMode, value);
        };
        Set(follow, Core.Application.CommonWidgetSettings.LayerModeGlobal);
        Set(top, Core.Application.CommonWidgetSettings.LayerModeTop);
        Set(desktop, Core.Application.CommonWidgetSettings.LayerModeDesktop);

        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock { Text = vm.WidgetLayerTitle, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = new Avalonia.Media.SolidColorBrush(Color.Parse("#CCCCCC")) });
        section.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 16, Children = { follow, top, desktop } });
        return section;
    }

    /// <summary>Per-widget auto-hide. Only takes effect while the widget sits on a screen edge.</summary>
    private Control BuildWidgetAutoHideSection(Core.Models.WidgetDefinition def)
    {
        var vm = Vm;
        var widgets = _appServices!.WidgetService;
        var cb = new CheckBox
        {
            Content = vm.WidgetAutoHideText,
            IsChecked = def.GetSettingBool(Core.Application.CommonWidgetSettings.AutoHide, false),
            Foreground = Avalonia.Media.Brushes.LightGray,
        };
        cb.IsCheckedChanged += (_, _) => widgets.UpdateSetting(def.Id, Core.Application.CommonWidgetSettings.AutoHide, (cb.IsChecked == true).ToString());
        var helper = new TextBlock { Text = vm.WidgetAutoHideHelper, FontSize = 11, Foreground = new Avalonia.Media.SolidColorBrush(Color.Parse("#888888")), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        return new StackPanel { Spacing = 4, Children = { cb, helper } };
    }

    private Control BuildWidgetOpacitySection(Core.Models.WidgetDefinition def)
    {
        var vm = Vm;
        var widgets = _appServices!.WidgetService;
        bool custom = def.GetSetting(Core.Application.CommonWidgetSettings.OpacityMode,
            Core.Application.CommonWidgetSettings.OpacityModeGlobal) == Core.Application.CommonWidgetSettings.OpacityModeCustom;

        var follow = new RadioButton { Content = vm.WidgetOpacityFollowGlobal, IsChecked = !custom, Foreground = Avalonia.Media.Brushes.White, GroupName = "opacity-" + def.Id };
        var own = new RadioButton { Content = vm.WidgetOpacityCustom, IsChecked = custom, Foreground = Avalonia.Media.Brushes.White, GroupName = "opacity-" + def.Id };
        var slider = new Slider
        {
            Minimum = 20, Maximum = 100,
            Value = def.GetSettingInt(Core.Application.CommonWidgetSettings.Opacity, 100),
            IsEnabled = custom, Margin = new Thickness(0, 4, 0, 0),
        };

        follow.IsCheckedChanged += (_, _) =>
        {
            if (follow.IsChecked != true) return;
            slider.IsEnabled = false;
            widgets.UpdateSetting(def.Id, Core.Application.CommonWidgetSettings.OpacityMode, Core.Application.CommonWidgetSettings.OpacityModeGlobal);
        };
        own.IsCheckedChanged += (_, _) =>
        {
            if (own.IsChecked != true) return;
            slider.IsEnabled = true;
            widgets.UpdateSetting(def.Id, Core.Application.CommonWidgetSettings.Opacity, ((int)Math.Round(slider.Value)).ToString());
            widgets.UpdateSetting(def.Id, Core.Application.CommonWidgetSettings.OpacityMode, Core.Application.CommonWidgetSettings.OpacityModeCustom);
        };
        slider.ValueChanged += (_, _) =>
        {
            if (own.IsChecked == true)
                widgets.UpdateSetting(def.Id, Core.Application.CommonWidgetSettings.Opacity, ((int)Math.Round(slider.Value)).ToString());
        };

        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock { Text = vm.WidgetOpacityTitle, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = new Avalonia.Media.SolidColorBrush(Color.Parse("#CCCCCC")) });
        section.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 16, Children = { follow, own } });
        section.Children.Add(slider);
        return section;
    }

    /// <summary>Updates list labels without rebuilding the settings panel (keeps focus in text boxes).</summary>
    private void RefreshWidgetListLabelOnly(string id)
    {
        if (_appServices == null) return;
        var def = _appServices.WidgetService.Find(id);
        if (def == null) return;
        for (int i = 0; i < WidgetsList.Items.Count; i++)
        {
            if (WidgetsList.Items[i] is WidgetListEntry entry && entry.Id == id)
            {
                string label = Vm.WidgetTypeName(def.Type);
                if (def.Type == Core.Application.WidgetTypes.Text)
                {
                    string preview = Core.Application.WidgetService.ResolveText(
                        def.GetSetting(Core.Application.TextWidgetSettings.Template, "{host}"));
                    if (!string.IsNullOrEmpty(preview)) label += $" — {preview}";
                }
                if (!def.Enabled) label += "  (off)";
                _suppressWidgetEvents = true;
                WidgetsList.Items[i] = new WidgetListEntry(id, label);
                WidgetsList.SelectedIndex = i;
                _suppressWidgetEvents = false;
                break;
            }
        }
    }

    private void OnAddWidget(object? sender, RoutedEventArgs e)
    {
        if (_appServices == null || WidgetTypeCombo.SelectedItem is not WidgetTypeEntry type) return;
        var provider = App.WidgetRegistry.Get(type.TypeKey);
        if (provider == null) return;
        var def = _appServices.WidgetService.Add(type.TypeKey, provider.DefaultSettings());
        RefreshWidgetList(def.Id);
    }

    private void OnRemoveWidget(object? sender, RoutedEventArgs e)
    {
        if (_appServices == null || WidgetsList.SelectedItem is not WidgetListEntry entry) return;
        _appServices.WidgetService.Remove(entry.Id);
        RefreshWidgetList();
    }

    private void OnWidgetEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressWidgetEvents || _appServices == null) return;
        if (WidgetsList.SelectedItem is not WidgetListEntry entry) return;
        _appServices.WidgetService.SetEnabled(entry.Id, WidgetEnabledCheck.IsChecked == true);
        RefreshWidgetListLabelOnly(entry.Id);
    }

    private void OnPresetColorPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: ISolidColorBrush brush })
            Vm.DockColor = brush.Color;
    }

    private void OnTintPresetColorPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: ISolidColorBrush brush })
            Vm.TintColor = brush.Color;
    }

    private void OnCustomTintColor(object? sender, RoutedEventArgs e)
    {
        var vm = Vm;
        var dialog = new Window
        {
            Title = vm.TintIconsTitle,
            Width = 340,
            Height = 420,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Color.Parse("#1E1E1E")),
            Content = new ColorView
            {
                Color = vm.TintColor,
                Margin = new Thickness(16),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            }
        };
        dialog.Closed += (_, _) =>
        {
            if (dialog.Content is ColorView picker)
                vm.TintColor = picker.Color;
        };
        dialog.ShowDialog(this);
    }

    private void OnCustomColor(object? sender, RoutedEventArgs e)
    {
        var vm = Vm;
        var dialog = new Window
        {
            Title = vm.BgColorTitle,
            Width = 340,
            Height = 420,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Color.Parse("#1E1E1E")),
            Content = new ColorView
            {
                Color = vm.DockColor,
                Margin = new Thickness(16),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            }
        };
        dialog.Closed += (_, _) =>
        {
            if (dialog.Content is ColorView picker)
                vm.DockColor = picker.Color;
        };
        dialog.ShowDialog(this);
    }
}
