using System.Windows.Controls;

namespace Lokit.Navigation
{
    // Distinct, retained page instances let NavigationView own routing without recreating edit sessions.
    // Their contents and event bindings are declared in MainWindow.xaml and held by PageStore.
    public sealed class TranslationPage : UserControl { }
    public sealed class EditorPage : UserControl { }
    public sealed class AddNewPage : UserControl { }
    public sealed class SettingsPage : UserControl { }
}
