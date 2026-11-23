using System.Windows;
using System.Windows.Media;

namespace GunlukUygulamasi.Services;

public sealed class ThemeService
{
    public const string NightBlue = "Gece Mavisi";
    public const string Obsidian = "Obsidyen";
    public const string ForestNight = "Orman Gecesi";
    public const string WarmNight = "Sıcak Gece";
    public const string DynamicWeather = "Hava Durumuna Göre Otomatik";

    private static readonly string[] ThemeNames =
    [
        NightBlue, Obsidian, ForestNight, WarmNight, DynamicWeather
    ];

    private readonly Application _application;

    public ThemeService(Application application)
    {
        _application = application;
    }

    public event Action<TrayThemeColors>? ThemeApplied;

    public static bool IsKnownTheme(string? themeName) => ThemeNames.Contains(themeName, StringComparer.Ordinal);

    public void ApplyTheme(string? themeName, WeatherSnapshot? weather, DateTime localTime)
    {
        var palette = GetPalette(themeName, weather, localTime);
        foreach (var (resourceKey, color) in palette.Colors)
        {
            _application.Resources[resourceKey] = new SolidColorBrush(color);
        }

        SetColorResource("AccentColor", palette.Colors["AccentBrush"]);
        SetColorResource("BorderFocusColor", palette.Colors["BorderFocusBrush"]);
        ThemeApplied?.Invoke(GetTrayThemeColors());
    }

    public TrayThemeColors GetTrayThemeColors() => new(
        GetResourceColor("BgSurfaceBrush", ParseColor("#191F2D")),
        GetResourceColor("BorderSubtleBrush", ParseColor("#2A3247")),
        GetResourceColor("BgHoverBrush", ParseColor("#242C3F")),
        GetResourceColor("TextPrimaryBrush", ParseColor("#F3F4F6")),
        GetResourceColor("AccentBrush", ParseColor("#6366F1")));

    private Color GetResourceColor(string resourceKey, Color fallback) =>
        _application.TryFindResource(resourceKey) is SolidColorBrush brush
            ? brush.Color
            : fallback;

    public sealed record TrayThemeColors(
        Color Background,
        Color Border,
        Color HoverBackground,
        Color Foreground,
        Color Accent);

    private void SetColorResource(string resourceKey, Color color)
    {
        _application.Resources[resourceKey] = color;
    }

    private static ThemePalette GetPalette(string? themeName, WeatherSnapshot? weather, DateTime localTime)
    {
        if (string.Equals(themeName, Obsidian, StringComparison.Ordinal))
        {
            return CreatePalette("#101114", "#17191E", "#1D2026", "#262A31", "#2F343D",
                "#3A404A", "#A3B4C7", "#93A4B8", "#B4C4D6", "#75869A", "#2493A4B8",
                "#F2F4F7", "#ADB4C0", "#78808D");
        }

        if (string.Equals(themeName, ForestNight, StringComparison.Ordinal))
        {
            return CreatePalette("#0B1210", "#101A16", "#15231D", "#1B2B23", "#21382D",
                "#294438", "#10B981", "#10B981", "#34D399", "#059669", "#2410B981",
                "#ECFDF5", "#A7B8AD", "#6F8276");
        }

        if (string.Equals(themeName, WarmNight, StringComparison.Ordinal))
        {
            return CreatePalette("#15110E", "#1D1712", "#251D17", "#30251C", "#3D2F22",
                "#4B392A", "#F59E0B", "#D99A36", "#FBBF24", "#B7791F", "#24D99A36",
                "#F7F1E8", "#BFB3A2", "#817565");
        }

        if (string.Equals(themeName, DynamicWeather, StringComparison.Ordinal))
        {
            if (localTime.Hour >= 20 || localTime.Hour < 6)
            {
                return CreatePalette("#0C0D1A", "#121426", "#191B31", "#222440", "#292B4A",
                    "#35375A", "#818CF8", "#818CF8", "#A5B4FC", "#6366F1", "#24818CF8",
                    "#F3F4FF", "#B8B8D0", "#777891");
            }

            return GetWeatherPalette(weather?.WeatherCode);
        }

        return CreatePalette("#0D1017", "#131722", "#191F2D", "#21283A", "#242C3F",
            "#2A3247", "#6366F1", "#6366F1", "#7578F7", "#4F52DC", "#246366F1",
            "#F3F4F6", "#9CA3AF", "#6B7280");
    }

    private static ThemePalette GetWeatherPalette(int? weatherCode)
    {
        if (weatherCode is 0 or 1)
        {
            return CreatePalette("#15130E", "#1C1912", "#252116", "#302A1A", "#3A321F",
                "#4B4027", "#F59E0B", "#F59E0B", "#FBBF24", "#D97706", "#24F59E0B",
                "#FFF9EC", "#D0C2A0", "#918365");
        }

        if (weatherCode is 51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or
            80 or 81 or 82 or 95 or 96 or 99)
        {
            return CreatePalette("#0D141C", "#121B25", "#182431", "#202F3D", "#273947",
                "#344B5C", "#38BDF8", "#38BDF8", "#7DD3FC", "#0284C7", "#2438BDF8",
                "#F0F9FF", "#A7BDCC", "#708797");
        }

        if (weatherCode is 71 or 73 or 75 or 77 or 85 or 86)
        {
            return CreatePalette("#10171D", "#151F27", "#1C2932", "#243641", "#2D424E",
                "#3B5360", "#BAE6FD", "#BAE6FD", "#E0F2FE", "#7DD3FC", "#24BAE6FD",
                "#F5FCFF", "#B8CBD5", "#7A919D");
        }

        return CreatePalette("#10131B", "#151923", "#1D2230", "#262D3B", "#303849",
            "#3E4759", "#94A3B8", "#94A3B8", "#C4B5FD", "#64748B", "#2494A3B8",
            "#F5F6FA", "#B0B7C4", "#7F8795");
    }

    private static ThemePalette CreatePalette(
        string primary, string sidebar, string surface, string elevated, string hover,
        string border, string focus, string accent, string accentHover, string accentPressed,
        string accentSoft, string textPrimary, string textSecondary, string textMuted)
    {
        return new ThemePalette(new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["BgPrimaryBrush"] = ParseColor(primary),
            ["BgSidebarBrush"] = ParseColor(sidebar),
            ["BgSurfaceBrush"] = ParseColor(surface),
            ["BgElevatedBrush"] = ParseColor(elevated),
            ["BgHoverBrush"] = ParseColor(hover),
            ["BorderSubtleBrush"] = ParseColor(border),
            ["BorderFocusBrush"] = ParseColor(focus),
            ["AccentBrush"] = ParseColor(accent),
            ["AccentHoverBrush"] = ParseColor(accentHover),
            ["AccentPressedBrush"] = ParseColor(accentPressed),
            ["AccentSoftBrush"] = ParseColor(accentSoft),
            ["TextPrimaryBrush"] = ParseColor(textPrimary),
            ["TextSecondaryBrush"] = ParseColor(textSecondary),
            ["TextMutedBrush"] = ParseColor(textMuted)
        });
    }

    private static Color ParseColor(string color) =>
        (Color)ColorConverter.ConvertFromString(color)!;

    private sealed record ThemePalette(IReadOnlyDictionary<string, Color> Colors);
}
