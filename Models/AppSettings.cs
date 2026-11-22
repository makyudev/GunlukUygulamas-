namespace GunlukUygulamasi.Models;

public class AppSettings
{
    public string DiaryName { get; set; } = "Kişisel Günlüğüm";
    public double EditorFontSize { get; set; } = 15.0;
    public bool SortNewestFirst { get; set; } = true;
    public bool ConfirmDelete { get; set; } = true;
}
