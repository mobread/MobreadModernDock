namespace MobreadModernDock.Core.Application;

/// <summary>
/// Parses hotkey chords written as "Ctrl+Alt+D" / "Win+Shift" into the
/// modifier flags and virtual-key code that <c>RegisterHotKey</c> expects.
/// Kept in Core so the parsing is unit-testable without Win32.
/// </summary>
public static class HotkeyChord
{
    // MOD_* values from winuser.h.
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    /// <summary>Modifier part of a chord. <see cref="Key"/> is 0 when the chord is modifiers only.</summary>
    public sealed record Parsed(uint Modifiers, uint Key)
    {
        public bool HasKey => Key != 0;
    }

    /// <summary>
    /// Parses a chord. Returns null when the text is empty, names an unknown
    /// token, or contains more than one non-modifier key. A chord with no
    /// modifiers at all is rejected too: a bare "D" would swallow every D
    /// typed anywhere on the system.
    /// </summary>
    public static Parsed? TryParse(string? text, bool allowModifiersOnly = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint mods = 0, key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string t = raw.ToUpperInvariant();
            switch (t)
            {
                case "CTRL": case "CONTROL": mods |= ModControl; continue;
                case "ALT": mods |= ModAlt; continue;
                case "SHIFT": mods |= ModShift; continue;
                case "WIN": case "WINDOWS": case "SUPER": case "META": mods |= ModWin; continue;
            }
            uint vk = KeyCode(t);
            if (vk == 0 || key != 0) return null;
            key = vk;
        }
        if (mods == 0) return null;
        if (key == 0 && !allowModifiersOnly) return null;
        return new Parsed(mods, key);
    }

    /// <summary>Canonical text for a parsed chord ("Ctrl+Alt+D").</summary>
    public static string Format(Parsed chord)
    {
        var parts = new List<string>();
        if ((chord.Modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((chord.Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((chord.Modifiers & ModShift) != 0) parts.Add("Shift");
        if ((chord.Modifiers & ModWin) != 0) parts.Add("Win");
        if (chord.HasKey) parts.Add(KeyName(chord.Key));
        return string.Join("+", parts);
    }

    /// <summary>Virtual-key code for the digit 1..9 (VK_1 = 0x31).</summary>
    public static uint DigitKey(int digit) => (uint)(0x30 + Math.Clamp(digit, 0, 9));

    private static uint KeyCode(string t)
    {
        if (t.Length == 1)
        {
            char c = t[0];
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        if (t.Length is 2 or 3 && t[0] == 'F' && int.TryParse(t.AsSpan(1), out int f) && f is >= 1 and <= 24)
            return (uint)(0x70 + f - 1);
        return t switch
        {
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "ENTER" or "RETURN" => 0x0D,
            "BACKSPACE" => 0x08,
            "INSERT" => 0x2D,
            "DELETE" or "DEL" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "GRAVE" or "`" => 0xC0,
            "MINUS" or "-" => 0xBD,
            "PLUS" or "=" or "EQUALS" => 0xBB,
            "COMMA" or "," => 0xBC,
            "PERIOD" or "." => 0xBE,
            "SLASH" or "/" => 0xBF,
            "BACKSLASH" or "\\" => 0xDC,
            "SEMICOLON" or ";" => 0xBA,
            "QUOTE" or "'" => 0xDE,
            "LBRACKET" or "[" => 0xDB,
            "RBRACKET" or "]" => 0xDD,
            _ => 0,
        };
    }

    private static string KeyName(uint vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);
        return vk switch
        {
            0x20 => "Space", 0x09 => "Tab", 0x1B => "Esc", 0x0D => "Enter", 0x08 => "Backspace",
            0x2D => "Insert", 0x2E => "Delete", 0x24 => "Home", 0x23 => "End",
            0x21 => "PageUp", 0x22 => "PageDown",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            0xC0 => "`", 0xBD => "-", 0xBB => "=", 0xBC => ",", 0xBE => ".", 0xBF => "/",
            0xDC => "\\", 0xBA => ";", 0xDE => "'", 0xDB => "[", 0xDD => "]",
            _ => $"0x{vk:X2}",
        };
    }
}
