using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LocalizationTranslationWPF.Navigation;
using LocalizationTranslationWPF.Utils;
using Wpf.Ui.Controls;

namespace LocalizationTranslationWPF
{
    public partial class MainWindow
    {
        private UserControl[] PageViews => PageStore.GetPages(this);
        private bool isNavigationPromptActive;
        private bool allowNavigation;
        private bool? isCompactNavigation;

        private void InitializeNavigation()
        {
            RootNavigationView.SetServiceProvider(new WorkspacePageProvider(PageViews));
            Loaded += MainWindowLoaded;
        }

        private void MainWindowLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindowLoaded;
            ThemeUtils.Attach(this, ConfigUtils.AppTheme.Value);
            UpdateNavigationPane();
            RootNavigationView.Navigate(typeof(TranslationPage));
        }

        private void MainWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (isWindowReady && e.WidthChanged) UpdateNavigationPane();
        }

        private void UpdateNavigationPane()
        {
            var compact = ActualWidth < 1040;
            // Only switch on crossing the breakpoint; respect manual folding at the same window size.
            if (isCompactNavigation == compact) return;
            isCompactNavigation = compact;
            RootNavigationView.IsPaneOpen = !compact;
        }

        private async void NavigationNavigating(NavigationView sender, NavigatingCancelEventArgs e)
        {
            if (!isWindowReady) return;
            if (cts != null || isProjectActionActive)
            {
                e.Cancel = true;
                return;
            }
            if (allowNavigation) return;
            if (isNavigationPromptActive)
            {
                e.Cancel = true;
                return;
            }
            var isDirty = currentPageIndex switch
            {
                1 => editorSession.IsDirty,
                2 => addNewSession.IsDirty,
                _ => false,
            };
            if (!isDirty) return;

            // Cancel synchronously, then navigate again only after the async save/discard decision.
            // The page and selected menu item therefore remain unchanged when the user cancels.
            e.Cancel = true;
            isNavigationPromptActive = true;
            try
            {
                if (!await ResolveUnsavedChangesAsync(currentPageIndex, "离开当前页面", true)) return;
                allowNavigation = true;
                RootNavigationView.Navigate(e.Page.GetType());
            }
            finally
            {
                allowNavigation = false;
                isNavigationPromptActive = false;
            }
        }                                                                                                                                                                                           

        private void NavigationSelectionChanged(NavigationView sender, RoutedEventArgs e)
        {
            if (!isWindowReady || sender.SelectedItem?.TargetPageType is not { } pageType) return;
            currentPageIndex = Array.FindIndex(PageViews, page => page.GetType() == pageType);
            InitializeEditingPage(currentPageIndex);
        }

        private sealed class WorkspacePageProvider : IServiceProvider
        {
            private readonly Dictionary<Type, UserControl> pages;
            public WorkspacePageProvider(IEnumerable<UserControl> views) => pages = views.ToDictionary(page => page.GetType());
            public object? GetService(Type serviceType) => pages.GetValueOrDefault(serviceType);
        }
    }

    public static class PageStore
    {
        public static readonly DependencyProperty PagesProperty = DependencyProperty.RegisterAttached(
            "Pages", typeof(UserControl[]), typeof(PageStore), new PropertyMetadata(Array.Empty<UserControl>()));

        public static UserControl[] GetPages(DependencyObject element) =>
            (UserControl[])element.GetValue(PagesProperty);

        public static void SetPages(DependencyObject element, UserControl[] value) =>
            element.SetValue(PagesProperty, value);
    }
}
