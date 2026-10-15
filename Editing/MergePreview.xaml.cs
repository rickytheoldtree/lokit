using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LocalizationTranslationWPF.Projects;

namespace LocalizationTranslationWPF.Editing
{
    public partial class MergePreview : UserControl, IDisposable
    {
        private readonly MergePlan plan;
        private readonly ICollectionView view;
        public sealed record ResolutionChoice(MergeResolution Value, string Label);
        public static IReadOnlyList<ResolutionChoice> ResolutionChoices { get; } = new[]
        {
            new ResolutionChoice(MergeResolution.Unresolved, "请选择…"),
            new ResolutionChoice(MergeResolution.KeepCurrent, "保留当前"),
            new ResolutionChoice(MergeResolution.UseIncoming, "采用传入"),
            new ResolutionChoice(MergeResolution.Custom, "手动编辑结果"),
        };
        public event Action? ChoicesChanged;

        public void SetListHeight(double height) => ChangesRow.Height = new GridLength(height);

        public void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        public MergePreview(MergePlan mergePlan)
        {
            plan = mergePlan;
            InitializeComponent();
            view = CollectionViewSource.GetDefaultView(plan.Entries);
            view.Filter = Matches;
            ChangesGrid.ItemsSource = view;
            foreach (var entry in plan.Entries) entry.PropertyChanged += ChoiceChanged;
            HintText.Text = string.Join(Environment.NewLine, plan.Notes.Append(
                "互不冲突的修改已自动选择。点击一行比较内容；删除会明确显示，确认合并前不会修改项目文案。"));
            ChangesGrid.SelectedIndex = plan.Entries.Count > 0 ? 0 : -1;
            UpdateProgress();
        }

        private bool Matches(object item)
        {
            var entry = (MergeEntry)item;
            if (ConflictsOnly.IsChecked == true && !entry.IsConflict) return false;
            var text = SearchBox.Text.Trim();
            return text.Length == 0 || new[] { entry.Language, entry.Key, entry.BeforeText, entry.CurrentText, entry.IncomingText }
                .Any(value => value.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        private void FilterChanged(object sender, RoutedEventArgs e) => RefreshFilter();
        private void SearchChanged(object sender, TextChangedEventArgs e) => RefreshFilter();
        private void RefreshFilter()
        {
            if (view == null) return;
            view.Refresh();
            if (ChangesGrid.SelectedItem == null && !view.IsEmpty) ChangesGrid.SelectedIndex = 0;
        }
        private void SelectedEntryChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DetailsPanel == null || ResultBox == null) return;
            DetailsPanel.DataContext = ChangesGrid.SelectedItem;
            ResultBox.DataContext = ChangesGrid.SelectedItem;
            ResultBox.IsEnabled = ChangesGrid.SelectedItem != null;
        }
        private void ResultChanged(object sender, TextChangedEventArgs e)
        {
            if (ResultBox.IsKeyboardFocusWithin && ResultBox.DataContext is MergeEntry entry)
                entry.Resolution = MergeResolution.Custom;
        }
        private void KeepVisibleClick(object sender, RoutedEventArgs e) => ResolveVisible(MergeResolution.KeepCurrent);
        private void AcceptVisibleClick(object sender, RoutedEventArgs e) => ResolveVisible(MergeResolution.UseIncoming);
        private void BatchChoicesClick(object sender, RoutedEventArgs e)
        {
            var button = (Wpf.Ui.Controls.Button)sender;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
        private void ResolveVisible(MergeResolution resolution)
        {
            foreach (var entry in view.Cast<MergeEntry>()) entry.Resolution = resolution;
        }
        private void ChoiceChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdateProgress();
            ChoicesChanged?.Invoke();
        }
        private void UpdateProgress() => SummaryText.Text = $"{plan.Entries.Count} 项变化 · 新增 {plan.Entries.Count(e => e.Current == null)} · 删除 {plan.Entries.Count(e => e.Incoming == null)} · " +
            (plan.UnresolvedCount == 0 ? "已准备好，可以合并" : $"还有 {plan.UnresolvedCount} 项冲突需要选择");
        public void Dispose()
        {
            foreach (var entry in plan.Entries) entry.PropertyChanged -= ChoiceChanged;
            GC.SuppressFinalize(this);
        }
    }
}
