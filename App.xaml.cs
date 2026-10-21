using System.Windows;

using Lokit.Utils;

namespace Lokit
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // StartupUri creates MainWindow from base.OnStartup. Apply the saved
            // theme first so controls never resolve their initial colors from
            // the Light fallback in App.xaml during a dark-theme cold start.
            ThemeUtils.Apply(ConfigUtils.AppTheme.Value);
            base.OnStartup(e);
        }
    }
}
