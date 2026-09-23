namespace MobreadModernDock.Tests;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;
using MobreadModernDock.Core.Models;

/// <summary>
/// Chord parsing feeds RegisterHotKey directly, so a wrong flag or a
/// swallowed bare key would either register nothing or eat a plain letter
/// system-wide. The settings-service wrappers canonicalise what is stored.
/// </summary>
public class HotkeyChordTest
{
    [Theory]
    [InlineData("Ctrl+Alt+D", HotkeyChord.ModControl | HotkeyChord.ModAlt, 'D')]
    [InlineData("ctrl + shift + f5", HotkeyChord.ModControl | HotkeyChord.ModShift, 0x74u)]
    [InlineData("Win+Space", HotkeyChord.ModWin, 0x20u)]
    [InlineData("Control+Alt+7", HotkeyChord.ModControl | HotkeyChord.ModAlt, '7')]
    [InlineData("Alt+`", HotkeyChord.ModAlt, 0xC0u)]
    public void ParsesModifiersAndKey(string text, uint mods, uint key)
    {
        var parsed = HotkeyChord.TryParse(text);
        Assert.NotNull(parsed);
        Assert.Equal(mods, parsed!.Modifiers);
        Assert.Equal(key, parsed.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("D")]            // no modifier: would swallow every D typed anywhere
    [InlineData("Ctrl+Alt")]     // modifiers only, not allowed by default
    [InlineData("Ctrl+A+B")]     // two keys
    [InlineData("Ctrl+Bogus")]
    [InlineData("Ctrl+F25")]
    public void RejectsInvalidChords(string text)
    {
        Assert.Null(HotkeyChord.TryParse(text));
    }

    [Fact]
    public void ModifiersOnlyIsOptIn()
    {
        var parsed = HotkeyChord.TryParse("Ctrl+Alt", allowModifiersOnly: true);
        Assert.NotNull(parsed);
        Assert.False(parsed!.HasKey);
        Assert.Equal(HotkeyChord.ModControl | HotkeyChord.ModAlt, parsed.Modifiers);
    }

    [Theory]
    [InlineData("shift+ctrl+alt+win+d", "Ctrl+Alt+Shift+Win+D")]
    [InlineData("CONTROL+f12", "Ctrl+F12")]
    [InlineData("alt+pgup", "Alt+PageUp")]
    public void FormatIsCanonical(string text, string expected)
    {
        Assert.Equal(expected, HotkeyChord.Format(HotkeyChord.TryParse(text)!));
    }

    [Fact]
    public void DigitKeysAreVk0To9()
    {
        Assert.Equal((uint)'1', HotkeyChord.DigitKey(1));
        Assert.Equal((uint)'9', HotkeyChord.DigitKey(9));
    }

    // --- settings service ---

    private sealed class Repo : IDockRepository
    {
        private readonly DockModel _model;
        public int Saves;
        public Repo(DockModel model) => _model = model;
        public DockModel Load() => _model;
        public void Save(DockModel model) => Saves++;
    }

    private static (DockAppearanceService Svc, DockModel Model, Repo Repo) Build()
    {
        var model = new DockModel();
        var repo = new Repo(model);
        return (new DockAppearanceService(new DockService(repo)), model, repo);
    }

    [Fact]
    public void DefaultsAvoidExplorerOwnedWinDigits()
    {
        var (svc, _, _) = Build();
        Assert.True(svc.GetHotkeysEnabled());
        Assert.Equal("Ctrl+Alt+D", svc.GetHotkeyToggleDock());
        // Win+1..9 is Explorer's (RegisterHotKey fails with 1409), so the
        // launch chords must default to something else.
        Assert.Equal("Ctrl+Alt", svc.GetHotkeyLaunchModifiers());
    }

    [Fact]
    public void StoresCanonicalChordAndEmptiesInvalid()
    {
        var (svc, model, repo) = Build();
        svc.SetHotkeyToggleDock("alt + ctrl + x");
        Assert.Equal("Ctrl+Alt+X", model.HotkeyToggleDock);
        svc.SetHotkeyToggleDock("garbage");
        Assert.Equal("", model.HotkeyToggleDock);
        Assert.Equal(2, repo.Saves);
    }

    [Fact]
    public void LaunchModifiersRejectAKey()
    {
        var (svc, model, _) = Build();
        svc.SetHotkeyLaunchModifiers("win+shift");
        Assert.Equal("Shift+Win", model.HotkeyLaunchModifiers);
        svc.SetHotkeyLaunchModifiers("Ctrl+Alt+D");
        Assert.Equal("", model.HotkeyLaunchModifiers);
    }
}
