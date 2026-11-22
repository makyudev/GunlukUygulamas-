using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

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
    }
}


