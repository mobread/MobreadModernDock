using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CedroModernDock.Core.Application;
using CedroModernDock.ViewModels;

namespace CedroModernDock.Views;

/// <summary>One file or sub-folder shown in a stack.</summary>
public sealed class StackEntry : ViewModelBase
{
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }

    private Bitmap? _icon;
    public Bitmap? Icon { get => _icon; set => SetProperty(ref _icon, value); }

    public StackEntry(string fullPath, bool isDirectory)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(Name)) Name = fullPath;
    }
}

/// <summary>
/// macOS-style folder stack: clicking a folder item on the dock shows this
/// popup with the folder's contents as an icon grid. Click an entry to open
/// it with the shell; folders drill down in place; the header opens the
/// folder in Explorer. Hidden when the pointer leaves or another dock item
/// is clicked.
/// </summary>
public partial class FolderStackPopup : Window
{
    private const int MaxEntries = 60;
    private readonly ObservableCollection<StackEntry> _entries = new();
    private readonly DispatcherTimer _hideDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private AppServices? _services;
    private string _folderPath = "";
    private string _rootPath = "";
    private int _loadVersion;

    public FolderStackPopup()
    {
        InitializeComponent();
        Items.ItemsSource = _entries;
        // SizeToContent grows the window after entries/icons arrive; keep it
        // anchored to the dock icon as it does.
        SizeChanged += (_, _) => { if (IsVisible) PositionNearLast(); };
        PointerExited += (_, _) => { _hideDebounce.Stop(); _hideDebounce.Start(); };
        PointerEntered += (_, _) => _hideDebounce.Stop();
        _hideDebounce.Tick += (_, _) => { _hideDebounce.Stop(); Hide(); };
        Deactivated += (_, _) => { };
    }

    public bool IsShowingFolder(string path) => IsVisible &&
        string.Equals(_rootPath, path, StringComparison.OrdinalIgnoreCase);

    public void ShowFor(AppServices services, string folderPath, string label, Control anchor, bool verticalDock)
    {
        _services = services;
        _rootPath = folderPath;
        ApplyChrome();
        LoadFolder(folderPath, label);
        if (!IsVisible) Show();
        PositionNear(anchor, verticalDock);
        // SizeToContent finalizes after the first layout; re-clamp then so a
        // wide grid never hangs off the screen edge.
    }

    public void HidePopup()
    {
        _hideDebounce.Stop();
        Hide();
    }

    private void ApplyChrome()
    {
        if (_services == null) return;
        var a = _services.AppearanceService;
        Chrome.CornerRadius = new CornerRadius(Math.Max(6, a.GetDockBorderRounding()));
        var parts = a.GetDockColorRGB().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        byte r = parts.Length > 0 && byte.TryParse(parts[0], out var rv) ? rv : (byte)0;
        byte g = parts.Length > 1 && byte.TryParse(parts[1], out var gv) ? gv : (byte)0;
        byte b = parts.Length > 2 && byte.TryParse(parts[2], out var bv) ? bv : (byte)0;
        // Stacks need to be readable over any wallpaper: floor the alpha.
        byte alpha = (byte)Math.Max(200, a.GetDockTransparencyPercentage() / 100.0 * 255);
        Chrome.Background = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
    }

    private void LoadFolder(string folderPath, string label)
    {
        _folderPath = folderPath;
        HeaderText.Text = label;
        CountText.Text = "";
        EmptyText.IsVisible = false;
        _entries.Clear();
        int version = ++_loadVersion;

        Task.Run(() =>
        {
            List<StackEntry> list;
            string? error = null;
            try
            {
                var dirs = Directory.EnumerateDirectories(folderPath)
                    .Where(d => (File.GetAttributes(d) & FileAttributes.Hidden) == 0)
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new StackEntry(d, true));
                var files = Directory.EnumerateFiles(folderPath)
                    .Where(f => (File.GetAttributes(f) & FileAttributes.Hidden) == 0)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Select(f => new StackEntry(f, false));
                list = dirs.Concat(files).Take(MaxEntries).ToList();
            }
            catch (Exception e)
            {
                list = new List<StackEntry>();
                error = e.Message;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (version != _loadVersion) return;
                foreach (var e in list) _entries.Add(e);
                int total = list.Count;
                CountText.Text = total == 0 ? "" : total >= MaxEntries ? $"{MaxEntries}+" : total.ToString();
                if (total == 0)
                {
                    EmptyText.Text = error ?? _services?.LocalizationService.Text("stack.empty") ?? "Empty";
                    EmptyText.IsVisible = true;
                }
                PositionNearLast();
            });

            // Icons load after the list is visible so the popup appears instantly.
            var gateway = _services?.IconGateway;
            if (gateway == null) return;
            foreach (var entry in list)
            {
                if (version != _loadVersion) return;
                string? iconPath = null;
                try { iconPath = gateway.ResolveFileIcon(entry.FullPath); } catch { }
                var bmp = IconLoader.LoadFromFile(iconPath);
                if (bmp != null)
                    Dispatcher.UIThread.Post(() => { if (version == _loadVersion) entry.Icon = bmp; });
            }
        });
    }

    private Control? _lastAnchor;
    private bool _lastVertical;

    private void PositionNearLast()
    {
        if (_lastAnchor != null) PositionNear(_lastAnchor, _lastVertical);
    }

    private void PositionNear(Control anchor, bool verticalDock)
    {
        _lastAnchor = anchor;
        _lastVertical = verticalDock;
        double scale = RenderScaling;
        // Before the first layout, measure against the screen (not
        // infinity) so the WrapPanel wraps and reports a real width.
        double dw = Bounds.Width, dh = Bounds.Height;
        if (dw <= 0 || dh <= 0)
        {
            var probe = ScreenGeometry.WorkAreaAt(ScreenGeometry.ControlScreenRect(anchor).Center);
            Measure(new Size(Math.Min(600, probe.Width / scale), probe.Height / scale));
            dw = DesiredSize.Width;
            dh = DesiredSize.Height;
        }
        int w = (int)Math.Ceiling(dw * scale);
        int h = (int)Math.Ceiling(dh * scale);

        // True screen rects (the dock is desktop-parented, so Avalonia's
        // PointToScreen would be off by the virtual-desktop origin).
        var a = ScreenGeometry.ControlScreenRect(anchor);
        var dock = TopLevel.GetTopLevel(anchor) is Window dw2 ? ScreenGeometry.WindowScreenRect(dw2) : a;
        var center = new PixelPoint(a.X + a.Width / 2, a.Y + a.Height / 2);
        var work = ScreenGeometry.WorkAreaAt(center);

        const int gap = 6;
        int x, y;
        if (verticalDock)
        {
            bool placeRight = center.X < work.X + work.Width / 2;
            x = placeRight ? dock.Right + gap : dock.X - w - gap;
            y = center.Y - h / 2;
        }
        else
        {
            bool placeAbove = center.Y > work.Y + work.Height / 2;
            y = placeAbove ? dock.Y - h - gap : dock.Bottom + gap;
            x = center.X - w / 2;
        }
        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - w));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - h));
        Position = new PixelPoint(x, y);
    }

    private void OnEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: StackEntry entry }) return;
        if (entry.IsDirectory)
        {
            LoadFolder(entry.FullPath, entry.Name);
            return;
        }
        ShellOpen(entry.FullPath);
        HidePopup();
    }

    private void OnEntryContext(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Button { DataContext: StackEntry entry }) return;
        e.Handled = true;
        // Right-click: reveal in Explorer (select the file).
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.FullPath}\"") { UseShellExecute = false });
        }
        catch { }
        HidePopup();
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        ShellOpen(_folderPath);
        HidePopup();
    }

    private static void ShellOpen(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"[FolderStack] open failed: {ex.Message}"); }
    }
}
