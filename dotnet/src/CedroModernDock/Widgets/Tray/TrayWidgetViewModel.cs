using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CedroModernDock.Core.Application;
using CedroModernDock.Core.Domain;
using CedroModernDock.Core.Models;

namespace CedroModernDock.Widgets.Tray;

/// <summary>
/// View model for the tray widget. Polls the tray gateway on a background
/// task and reconciles <see cref="Icons"/> by key so unchanged icons keep
/// their view (and their decoded bitmap) between refreshes.
/// </summary>
public sealed class TrayWidgetViewModel : WidgetViewModelBase
{
    private readonly WidgetDefinition _definition;
    private readonly AppServices _services;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<string, TrayIconViewModel> _byKey = new();

    public ObservableCollection<TrayIconViewModel> Icons { get; } = new();

    private int _iconSize = 20;
    public int IconSize { get => _iconSize; set => SetProperty(ref _iconSize, value); }

    private int _spacing = 6;
    public int Spacing { get => _spacing; set => SetProperty(ref _spacing, value); }

    private Orientation _orientation = Orientation.Horizontal;
    public Orientation Orientation { get => _orientation; set => SetProperty(ref _orientation, value); }

    private bool _showSystemIcons;
    private bool _isEmpty = true;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    public TrayWidgetViewModel(WidgetDefinition definition, AppServices services)
    {
        _definition = definition;
        _services = services;
        Refresh();
        StartPolling();
    }

    public override void Refresh()
    {
        IconSize = Math.Clamp(_definition.GetSettingInt(TrayWidgetSettings.IconSize, 20), 12, 64);
        Spacing = Math.Clamp(_definition.GetSettingInt(TrayWidgetSettings.Spacing, 6), 0, 30);
        Orientation = _definition.GetSettingBool(TrayWidgetSettings.Vertical, false)
            ? Orientation.Vertical : Orientation.Horizontal;
        bool showSystem = _definition.GetSettingBool(TrayWidgetSettings.ShowSystemIcons, false);
        bool filterChanged = showSystem != _showSystemIcons;
        _showSystemIcons = showSystem;
        foreach (var icon in Icons) icon.Size = IconSize;
        if (filterChanged) Task.Run(PollOnce);
    }

    public override void Shutdown()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private void StartPolling()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { PollOnce(); }
                catch { /* keep polling */ }
                try { await Task.Delay(2000, token); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    private void PollOnce()
    {
        var icons = _services.TrayIconGateway.GetIcons();
        var desired = icons.Where(i => _showSystemIcons || !i.IsSystemIcon).ToList();
        Dispatcher.UIThread.Post(() => Reconcile(desired));
    }

    private void Reconcile(List<TrayIconInfo> desired)
    {
        var desiredKeys = new HashSet<string>(desired.Select(d => d.Key));

        // Remove vanished icons.
        for (int i = Icons.Count - 1; i >= 0; i--)
        {
            if (!desiredKeys.Contains(Icons[i].Key))
            {
                _byKey.Remove(Icons[i].Key);
                Icons.RemoveAt(i);
            }
        }

        // Add / update / reorder to match the tray's order.
        for (int i = 0; i < desired.Count; i++)
        {
            var info = desired[i];
            if (!_byKey.TryGetValue(info.Key, out var vm))
            {
                vm = new TrayIconViewModel(info.Key, this) { Size = IconSize };
                _byKey[info.Key] = vm;
                Icons.Insert(Math.Min(i, Icons.Count), vm);
            }
            else
            {
                int current = Icons.IndexOf(vm);
                if (current != i && i < Icons.Count) Icons.Move(current, i);
            }
            vm.Update(info);
        }

        IsEmpty = Icons.Count == 0;
    }

    internal void Activate(string key) => Task.Run(() => _services.TrayIconGateway.Activate(key));
    internal void ShowContextMenu(string key) => Task.Run(() => _services.TrayIconGateway.ShowContextMenu(key));
}

public sealed class TrayIconViewModel : ViewModels.ViewModelBase
{
    private readonly TrayWidgetViewModel _owner;
    private byte[]? _pngBytes;

    public string Key { get; }

    private string _name = "";
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private Bitmap? _icon;
    public Bitmap? Icon { get => _icon; set => SetProperty(ref _icon, value); }

    private int _size = 20;
    public int Size { get => _size; set => SetProperty(ref _size, value); }

    private bool _hasIcon;
    public bool HasIcon { get => _hasIcon; set => SetProperty(ref _hasIcon, value); }

    /// <summary>First letter of the name, shown when no icon bitmap is available.</summary>
    private string _fallbackGlyph = "?";
    public string FallbackGlyph { get => _fallbackGlyph; set => SetProperty(ref _fallbackGlyph, value); }

    public TrayIconViewModel(string key, TrayWidgetViewModel owner)
    {
        Key = key;
        _owner = owner;
    }

    public void Update(TrayIconInfo info)
    {
        Name = info.Name;
        FallbackGlyph = string.IsNullOrEmpty(info.Name) ? "?" : info.Name[..1].ToUpperInvariant();

        // Decode only when the PNG bytes actually changed.
        if (info.IconPng != null && !SameBytes(info.IconPng, _pngBytes))
        {
            _pngBytes = info.IconPng;
            try
            {
                using var ms = new MemoryStream(info.IconPng);
                Icon = new Bitmap(ms);
                HasIcon = true;
            }
            catch
            {
                Icon = null;
                HasIcon = false;
            }
        }
        else if (info.IconPng == null && _pngBytes == null)
        {
            HasIcon = Icon != null;
        }
    }

    public void Activate() => _owner.Activate(Key);
    public void ShowContextMenu() => _owner.ShowContextMenu(Key);

    private static bool SameBytes(byte[] a, byte[]? b) =>
        b != null && a.Length == b.Length && a.AsSpan().SequenceEqual(b);
}
