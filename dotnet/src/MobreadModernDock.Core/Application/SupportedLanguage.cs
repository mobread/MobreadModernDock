namespace MobreadModernDock.Core.Application;

using System.Globalization;

/// <summary>
/// Direct port of the Java SupportedLanguage enum.
/// Each value carries a .NET culture and a native display name.
/// The enum is serialized by name (e.g. "EN_US") to match the Java config.json.
/// </summary>
public enum SupportedLanguage
{
    [Culture("en", "US")] EN_US,
    [Culture("zh", "CN")] ZH_CN,
    [Culture("zh", "TW")] ZH_TW,
    [Culture("hi", "IN")] HI_IN,
    [Culture("es", "ES")] ES_ES,
    [Culture("fr", "FR")] FR_FR,
    [Culture("ar", "SA")] AR_SA,
    [Culture("bn", "BD")] BN_BD,
    [Culture("pt", "BR")] PT_BR,
    [Culture("ru", "RU")] RU_RU,
    [Culture("ur", "PK")] UR_PK,
    [Culture("id", "ID")] ID_ID,
    [Culture("de", "DE")] DE_DE,
    [Culture("ja", "JP")] JA_JP,
    [Culture("pcm", "NG")] PCM_NG,
    [Culture("mr", "IN")] MR_IN,
    [Culture("te", "IN")] TE_IN,
    [Culture("tr", "TR")] TR_TR,
    [Culture("ta", "IN")] TA_IN,
    [Culture("yue", "HK")] YUE_HK,
    [Culture("vi", "VN")] VI_VN
}

/// <summary>Attribute carrying the culture info for a supported language.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class CultureAttribute(string language, string country) : Attribute
{
    public string Language { get; } = language;
    public string Country { get; } = country;

    public CultureInfo ToCulture() => CultureInfo.GetCultureInfo($"{Language}-{Country}");
}

/// <summary>Extension methods for SupportedLanguage.</summary>
public static class SupportedLanguageExtensions
{
    private static readonly Dictionary<SupportedLanguage, string> NativeNames = new()
    {
        [SupportedLanguage.EN_US] = "English",
        [SupportedLanguage.ZH_CN] = "\u7B80\u4F53\u4E2D\u6587",
        [SupportedLanguage.ZH_TW] = "\u7E41\u9AD4\u4E2D\u6587",
        [SupportedLanguage.HI_IN] = "\u0939\u093F\u0928\u094D\u0926\u0940",
        [SupportedLanguage.ES_ES] = "Espa\u00F1ol",
        [SupportedLanguage.FR_FR] = "Fran\u00E7ais",
        [SupportedLanguage.AR_SA] = "\u0627\u0644\u0639\u0631\u0628\u064A\u0629",
        [SupportedLanguage.BN_BD] = "\u09AC\u09BE\u0982\u09B2\u09BE",
        [SupportedLanguage.PT_BR] = "Portugu\u00EAs (Brasil)",
        [SupportedLanguage.RU_RU] = "\u0420\u0443\u0441\u0441\u043A\u0438\u0439",
        [SupportedLanguage.UR_PK] = "\u0627\u0631\u062F\u0648",
        [SupportedLanguage.ID_ID] = "Bahasa Indonesia",
        [SupportedLanguage.DE_DE] = "Deutsch",
        [SupportedLanguage.JA_JP] = "\u65E5\u672C\u8A9E",
        [SupportedLanguage.PCM_NG] = "Naij\u00E1",
        [SupportedLanguage.MR_IN] = "\u092E\u0930\u093E\u0920\u0940",
        [SupportedLanguage.TE_IN] = "\u0C24\u0C46\u0C32\u0C41\u0C17\u0C41",
        [SupportedLanguage.TR_TR] = "T\u00FCrk\u00E7e",
        [SupportedLanguage.TA_IN] = "\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD",
        [SupportedLanguage.YUE_HK] = "\u7CB5\u8A9E",
        [SupportedLanguage.VI_VN] = "Ti\u1EBFng Vi\u1EC7t"
    };

    /// <summary>Returns the native display name (e.g. "Português (Brasil)").</summary>
    public static string NativeDisplayName(this SupportedLanguage language) =>
        NativeNames.TryGetValue(language, out var name) ? name : language.ToString();

    /// <summary>Returns the .NET culture for this language.</summary>
    public static CultureInfo Culture(this SupportedLanguage language)
    {
        var field = typeof(SupportedLanguage).GetField(language.ToString());
        var attr = field?.GetCustomAttributes(typeof(CultureAttribute), false)
                           .FirstOrDefault() as CultureAttribute;
        return attr?.ToCulture() ?? CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// Returns the resource bundle suffix for this language (e.g. "pt_BR"),
    /// matching the .properties filename convention (messages_pt_BR.properties).
    /// </summary>
    public static string BundleSuffix(this SupportedLanguage language)
    {
        var culture = language.Culture();
        // culture.Name is "pt-BR" → convert to "pt_BR" to match the filename.
        return culture.Name.Replace('-', '_');
    }
}
