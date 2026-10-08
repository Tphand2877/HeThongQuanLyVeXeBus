using System.Globalization;
using System.Windows;

namespace HeThongQuanLyVeXeBus;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var culture = CultureInfo.GetCultureInfo("vi-VN");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        base.OnStartup(e);
    }
}
