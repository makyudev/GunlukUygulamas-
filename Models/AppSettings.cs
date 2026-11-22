namespace GunlukUygulamasi.Models;

public class AppSettings
{
    public string DiaryName { get; set; } = "Kişisel Günlüğüm";
    public string PasswordHint { get; set; } = string.Empty;
    public double EditorFontSize { get; set; } = 15.0;
    public string EditorFontFamily { get; set; } = "Segoe UI";
    public bool SortNewestFirst { get; set; } = true;
    public bool ConfirmDelete { get; set; } = true;
}
