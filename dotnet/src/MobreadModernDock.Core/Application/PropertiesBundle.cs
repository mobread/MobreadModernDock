namespace MobreadModernDock.Core.Application;

using System.Reflection;
using System.Text;

/// <summary>
/// Reads Java-style .properties resource files embedded in the assembly.
/// Handles the ISO-8859-1 encoding with \uXXXX escape sequences that Java
/// ResourceBundle uses, so the original 21 locale files can be used verbatim.
/// </summary>
internal static class PropertiesBundle
{
    private static readonly Assembly Assembly = typeof(PropertiesBundle).Assembly;

    /// <summary>
    /// Loads the messages bundle for the given language suffix
    /// (e.g. "en_US", "pt_BR"). Falls back to the key itself if not found.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Load(string languageSuffix)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        // First load the base (English) so missing keys in other locales fall back.
        LoadInto("en_US", result);
        if (languageSuffix != "en_US")
            LoadInto(languageSuffix, result);

        return result;
    }

    private static void LoadInto(string languageSuffix, Dictionary<string, string> target)
    {
        string resourceName = $"MobreadModernDock.Core.i18n.messages_{languageSuffix}.properties";
        Stream? stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return;

        // The original .properties files were saved as UTF-8 (not the Java-standard
        // ISO-8859-1), so we read them as UTF-8. \uXXXX escapes are still resolved
        // for the files that use them (e.g. non-Latin scripts).
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            int sep = line.IndexOf('=');
            if (sep <= 0)
                continue;

            string key = line[..sep].Trim();
            string value = line[(sep + 1)..].Trim();
            target[key] = UnescapeUnicode(value);
        }
    }

    /// <summary>Resolves \uXXXX escape sequences to their actual Unicode characters.</summary>
    private static string UnescapeUnicode(string value)
    {
        if (!value.Contains("\\u", StringComparison.Ordinal))
            return value;

        var sb = new StringBuilder(value.Length);
        int i = 0;
        while (i < value.Length)
        {
            if (value[i] == '\\' && i + 5 < value.Length && value[i + 1] == 'u')
            {
                string hex = value.Substring(i + 2, 4);
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))
                {
                    sb.Append((char)code);
                    i += 6;
                    continue;
                }
            }
            sb.Append(value[i]);
            i++;
        }
        return sb.ToString();
    }
}
