namespace GunlukUygulamasi.Models;

public class AppSettings
{
    public string DiaryName { get; set; } = "Kişisel Günlüğüm";
    public string PasswordHint { get; set; } = string.Empty;
    public double EditorFontSize { get; set; } = 15.0;
    public string EditorFontFamily { get; set; } = "Segoe UI";
    public bool SortNewestFirst { get; set; } = true;
    public bool ConfirmDelete { get; set; } = true;
    public string? ManualCity { get; set; } = string.Empty;
    public bool AutoWeatherBadge { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool DailyReminderEnabled { get; set; }
    public string ReminderTime { get; set; } = "21:00";
    public string ThemeName { get; set; } = "Gece Mavisi";
    public bool ShowMiniAquarium { get; set; } = true;
    public string AquariumFishName { get; set; } = "Maviş";
    public int AquariumFeedCount { get; set; }
}
