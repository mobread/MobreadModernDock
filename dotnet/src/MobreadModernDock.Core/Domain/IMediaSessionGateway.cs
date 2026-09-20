namespace MobreadModernDock.Core.Domain;

/// <summary>Snapshot of the system's current media session (what the volume flyout shows).</summary>
public sealed record MediaSessionInfo(
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    bool CanPlayPause,
    bool CanSkipNext,
    bool CanSkipPrevious,
    /// <summary>PNG/JPEG bytes of the album art, or null when the source didn't supply one.</summary>
    byte[]? Thumbnail,
    /// <summary>Source app identifier (AUMID or exe name), for display/debugging.</summary>
    string SourceApp);

/// <summary>Port for the OS media transport controls.</summary>
public interface IMediaSessionGateway
{
    /// <summary>Current session, or null when nothing is playing/paused.</summary>
    MediaSessionInfo? GetCurrent();

    /// <summary>Raised (on an arbitrary thread) when the session, its playback state or its metadata changes.</summary>
    event Action? Changed;

    void TogglePlayPause();
    void SkipNext();
    void SkipPrevious();
}
