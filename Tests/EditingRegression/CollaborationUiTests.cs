using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using LocalizationTranslationWPF;
using LocalizationTranslationWPF.Editing;
using LocalizationTranslationWPF.Projects;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

internal static partial class Program
{
    private static void WpfCollaboration(MainWindow window, ProjectWorkspace owner)
    {
        PumpDelay(300);
        True(window.AllowDrop);
        True(!Control<System.Windows.Controls.TextBlock>(window, "ProjectLocationTextBlock").Text.Contains(owner.TargetDirectory, StringComparison.Ordinal));
        var path = SharePath(owner);
        owner.ExportCollaboration(path);
        var product = ProjectWorkspace.Open(path);
        Change(product, "English", "base", "Product source {0}");
        product.AddSourceEntries(new() { ["review-new"] = "New product copy" });
        Change(owner, "English", "base", "Owner source {0}");
        var task = InvokeDrop(window, path);
        Pump();
        if (task.IsCompleted) throw new InvalidOperationException("Drop returned without a dialog: " + Control<InfoBar>(window, "MainInfoBar").Message);
        var dialog = MergeDialog(window);
        var preview = Descendants(dialog).OfType<MergePreview>().Single();
        var primary = DialogButton(dialog, "合并到当前项目");
        True(!primary.IsEnabled);
        var grid = (System.Windows.Controls.DataGrid)preview.FindName("ChangesGrid");
        var conflict = grid.Items.Cast<MergeEntry>().Single(entry => entry.IsConflict);
        grid.SelectedItem = conflict;
        PumpDelay(300);
        Capture(window, "merge-conflict-minimum.png");
        True(dialog.ActualHeight <= window.ActualHeight, "The merge dialog must fit the minimum window height.");
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(conflict);
        var choices = Descendants(row).OfType<ComboBox>().Single();
        choices.SelectedValue = MergeResolution.UseIncoming;
        Pump();
        True(primary.IsEnabled);
        var result = (TextBox)preview.FindName("ResultBox");
        result.Focus();
        result.Text = "Reviewed source {0}";
        Pump();
        Equal(MergeResolution.Custom, conflict.Resolution);
        InvokeButton(DialogButton(dialog, "保存进度，稍后处理"));
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
        True(owner.HasPendingMerge);
        Equal(Visibility.Visible, Control<FrameworkElement>(window, "BtnContinueMerge").Visibility);
        owner.AddSourceEntries(new() { ["meanwhile-ui"] = "Other work" });
        Click(window, "BtnContinueMerge");
        Pump();
        dialog = MergeDialog(window);
        preview = Descendants(dialog).OfType<MergePreview>().Single();
        grid = (System.Windows.Controls.DataGrid)preview.FindName("ChangesGrid");
        Equal(MergeResolution.Custom, grid.Items.Cast<MergeEntry>().Single(entry => entry.IsConflict).Resolution);
        InvokeButton(DialogButton(dialog, "合并到当前项目"));
        PumpUntil(() => !owner.HasPendingMerge);
        Equal("Reviewed source {0}", owner.ReadLanguage("English")["base"]);
        True(owner.ReadLanguage("English").ContainsKey("review-new"));
        True(owner.ReadLanguage("English").ContainsKey("meanwhile-ui"));
        Capture(window, "merged-project-minimum.png");
        // The exact same file is harmless after a merge, even if its conflict was edited manually.
        task = InvokeDrop(window, path);
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
        True(!Descendants(window).OfType<ContentDialog>().Any(d => d.IsVisible));
        // Invoke the menu handler with its actual routed-event signature.
        typeof(MainWindow).GetMethod("BtnUndoMergeClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, new object[] { window, new RoutedEventArgs() });
        Pump();
        dialog = Descendants(window).OfType<ContentDialog>().Single(d => d.IsVisible && d.Title?.ToString() == "撤销上次合并？");
        InvokeButton(DialogButton(dialog, "撤销合并"));
        PumpUntil(() => !owner.ReadLanguage("English").ContainsKey("review-new"));
        Equal("Owner source {0}", owner.ReadLanguage("English")["base"]);
        True(owner.ReadLanguage("English").ContainsKey("meanwhile-ui"));
        Invoke(window, "SetProject", null);
        task = InvokeDrop(window, path);
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
        True(ProjectContext.Current?.IsCollaborationCopy == true);
        Equal(Visibility.Collapsed, Control<FrameworkElement>(window, "BtnPublishProject").Visibility);
        Equal("保存并发回", Control<Button>(window, "BtnShareProject").Content?.ToString());
        Capture(window, "portable-collaboration.png");
        Invoke(window, "SetProject", owner);
    }

    private static Task InvokeDrop(MainWindow window, string path) =>
        (Task)typeof(MainWindow).GetMethod("ReceiveDroppedFilesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, new object[] { new[] { path } })!;

    private static ContentDialog MergeDialog(MainWindow window) => Descendants(window).OfType<ContentDialog>()
        .Single(d => d.IsVisible && d.Title?.ToString()?.StartsWith("接收修改 · ", StringComparison.Ordinal) == true);

    private static Button DialogButton(ContentDialog dialog, string label) => Descendants(dialog).OfType<Button>()
        .Single(button => button.Content?.ToString() == label);
}