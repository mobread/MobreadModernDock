namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using global::Windows.Media.Control;
using global::Windows.Storage.Streams;
using MobreadModernDock.Core.Domain;

/// <summary>
/// Media session via <see cref="GlobalSystemMediaTransportControlsSessionManager"/>
/// (the same source as the volume-flyout media card). Tracks the manager's
/// "current" session and re-subscribes when it changes.
/// </summary>
public sealed class GsmtcMediaSessionGateway : IMediaSessionGateway, IDisposable
{
    private readonly object _sync = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private MediaSessionInfo? _cached;
    private byte[]? _thumbCache;
    private string? _thumbCacheKey;
    private bool _initStarted;

    public event Action? Changed;

    public MediaSessionInfo? GetCurrent()
    {
        EnsureInitialized();
        lock (_sync) return _cached;
    }

    private void EnsureInitialized()
    {
        lock (_sync)
        {
            if (_initStarted) return;
            _initStarted = true;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                lock (_sync) _manager = manager;
                manager.CurrentSessionChanged += (_, _) => AttachCurrent();
                AttachCurrent();
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[Media] manager init failed: {e.Message}");
            }
        });
    }

    private void AttachCurrent()
    {
        GlobalSystemMediaTransportControlsSession? session;
        try { session = _manager?.GetCurrentSession(); }
        catch { session = null; }

        lock (_sync)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnSessionChanged;
                _session.PlaybackInfoChanged -= OnSessionChanged;
            }
            _session = session;
            if (session != null)
            {
                session.MediaPropertiesChanged += OnSessionChanged;
                session.PlaybackInfoChanged += OnSessionChanged;
            }
        }
        _ = RefreshAsync();
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_sync) session = _session;

        MediaSessionInfo? info = null;
        if (session != null)
        {
            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();
                var controls = playback.Controls;
                bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                // Thumbnails are re-read on every metadata event; cache by
                // title+artist so a play/pause toggle doesn't re-decode the art.
                string thumbKey = $"{props.Title}\u0001{props.Artist}\u0001{props.AlbumTitle}";
                byte[]? thumb;
                lock (_sync) thumb = thumbKey == _thumbCacheKey ? _thumbCache : null;
                if (thumb == null && props.Thumbnail != null)
                {
                    thumb = await ReadAllAsync(props.Thumbnail);
                    lock (_sync) { _thumbCache = thumb; _thumbCacheKey = thumbKey; }
                }

                info = new MediaSessionInfo(
                    props.Title ?? "", props.Artist ?? "", props.AlbumTitle ?? "",
                    playing,
                    controls.IsPlayEnabled || controls.IsPauseEnabled || controls.IsPlayPauseToggleEnabled,
                    controls.IsNextEnabled, controls.IsPreviousEnabled,
                    thumb, session.SourceAppUserModelId ?? "");
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[Media] refresh failed: {e.Message}");
            }
        }

        lock (_sync) _cached = info;
        Changed?.Invoke();
    }

    private static async Task<byte[]?> ReadAllAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 8 * 1024 * 1024) return null;
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            uint size = (uint)stream.Size;
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch { return null; }
    }

    public void TogglePlayPause() => Fire(s => s.TryTogglePlayPauseAsync());
    public void SkipNext() => Fire(s => s.TrySkipNextAsync());
    public void SkipPrevious() => Fire(s => s.TrySkipPreviousAsync());

    private void Fire(Func<GlobalSystemMediaTransportControlsSession, global::Windows.Foundation.IAsyncOperation<bool>> action)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_sync) session = _session;
        if (session == null) return;
        _ = Task.Run(async () =>
        {
            try { await action(session); }
            catch (Exception e) { Debug.WriteLine($"[Media] control failed: {e.Message}"); }
        });
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnSessionChanged;
                _session.PlaybackInfoChanged -= OnSessionChanged;
                _session = null;
            }
        }
    }
}
