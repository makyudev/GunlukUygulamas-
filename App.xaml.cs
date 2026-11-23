using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using Application = System.Windows.Application;
using GunlukUygulamasi.Services;

namespace GunlukUygulamasi;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var trCulture = new CultureInfo("tr-TR");
        Thread.CurrentThread.CurrentCulture = trCulture;
        Thread.CurrentThread.CurrentUICulture = trCulture;
        CultureInfo.DefaultThreadCurrentCulture = trCulture;
        CultureInfo.DefaultThreadCurrentUICulture = trCulture;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(trCulture.IetfLanguageTag)));

        base.OnStartup(e);

        try
        {
            var settings = new DiaryStorageService().LoadSettings();
            var themeName = ThemeService.IsKnownTheme(settings.ThemeName)
                ? settings.ThemeName
                : ThemeService.NightBlue;
            new ThemeService(this).ApplyTheme(themeName, weather: null, DateTime.Now);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("Başlangıç teması yüklenemedi: {0}", exception);
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}


