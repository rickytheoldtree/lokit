using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalizationTranslationWPF.Editing;
using LocalizationTranslationWPF.Projects;
using LocalizationTranslationWPF.Utils;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBlock = System.Windows.Controls.TextBlock;

namespace LocalizationTranslationWPF
{
    public partial class MainWindow
    {
        private async void BtnReceiveChangesClick(object sender, RoutedEventArgs e)
        {
            await RunExchangeAsync(async project =>
            {
                var picker = new OpenFileDialog
                {
                    Title = "接收修改到当前项目",
                    Filter = "协作副本或文案文件 (*.locproj;*.json)|*.locproj;*.json|协作副本 (*.locproj)|*.locproj|文案文件 (*.json)|*.json",
                    Multiselect = true,
                };
                if (picker.ShowDialog(this) == true) await ReceiveFilesAsync(project, picker.FileNames);
            });
        }

        private async void BtnImportFolderClick(object sender, RoutedEventArgs e)
        {
            await RunExchangeAsync(async project =>
            {
                var picker = new OpenFolderDialog { Title = "接收语言目录中的修改" };
                if (picker.ShowDialog(this) == true) await ReceiveFilesAsync(project, new[] { picker.FolderName });
            });
        }

        private async void BtnShareProjectClick(object sender, RoutedEventArgs e)
        {
            await RunExchangeAsync(project =>
            {
                var picker = new SaveFileDialog
                {
                    Title = project.IsCollaborationCopy ? "保存修订副本，发回项目负责人" : "保存协作副本，发给同事继续编辑",
                    Filter = "本地化协作副本 (*.locproj)|*.locproj",
                    DefaultExt = ".locproj",
                    FileName = project.Name + (project.IsCollaborationCopy ? "-修订.locproj" : "-协作.locproj"),
                };
                if (picker.ShowDialog(this) == true)
                {
                    project.ExportCollaboration(picker.FileName);
                    ShowSnackbar("协作副本已保存", "把这个文件发给同事，用本工具打开即可编辑。修改后发回，拖入原项目合并。",
                        ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                }
                return Task.CompletedTask;
            });
        }

        private async void BtnContinueMergeClick(object sender, RoutedEventArgs e) =>
            await RunExchangeAsync(project => ShowMergePlanAsync(project, project.PreviewPendingMerge()));

        private async void BtnSyncResourcesClick(object sender, RoutedEventArgs e) =>
            await RunExchangeAsync(project => ShowMergePlanAsync(project, project.PreviewTargetSync()));

        private async void BtnDiscardMergeClick(object sender, RoutedEventArgs e)
        {
            await RunExchangeAsync(async project =>
            {
                if (!project.HasPendingMerge) { ShowInfoBar("没有待处理的合并", "可以直接接收新的修改。", InfoBarSeverity.Informational); return; }
                var dialog = new ContentDialog(RootContentDialogHost)
                {
                    Title = "放弃保存的合并进度？",
                    Content = "当前项目文案保持原样。本次接收的内容和冲突选择将移除，之后可以重新拖入文件。",
                    PrimaryButtonText = "放弃本次合并", CloseButtonText = "继续保留",
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await dialog.ShowAsync(CancellationToken.None) == ContentDialogResult.Primary) project.DiscardMergeDraft();
            });
        }

        private async void BtnUndoMergeClick(object sender, RoutedEventArgs e)
        {
            await RunExchangeAsync(async project =>
            {
                if (!project.HasMergeBackup) { ShowInfoBar("没有可撤销的合并", "接收修改后会自动保留合并前的副本。", InfoBarSeverity.Informational); return; }
                var dialog = new ContentDialog(RootContentDialogHost)
                {
                    Title = "撤销上次合并？",
                    Content = "恢复合并前的项目文案。若合并后已继续修改或发布，将保留当前内容并阻止直接撤销。",
                    PrimaryButtonText = "撤销合并", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
                };
                if (await dialog.ShowAsync(CancellationToken.None) != ContentDialogResult.Primary) return;
                project.UndoLastMerge();
                ReloadEditorAfterMerge();
                ShowSnackbar("已撤销合并", "项目已恢复到合并前。", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
            });
        }

        private async Task RunExchangeAsync(Func<ProjectWorkspace, Task> action)
        {
            if (cts != null || isProjectActionActive || ProjectContext.Current is not { } project) return;
            isProjectActionActive = true;
            UpdateProjectUi();
            try
            {
                if (!CommitGridEdits(DataGridEditor) || !CommitGridEdits(DataGridAddNew)) return;
                if (editorSession.IsDirty && !SaveEditor(false)) return;
                if (addNewSession.IsDirty && !SaveAddNew(false)) return;
                await action(project);
            }
            catch (Exception error) { ShowInfoBar("操作未完成", error.Message, InfoBarSeverity.Warning); }
            finally { isProjectActionActive = false; InitializeEditingPage(currentPageIndex); UpdateProjectUi(); }
        }

        private void ProjectFilesDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Handled = true;
            e.Effects = cts == null && !isProjectActionActive ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void ProjectFilesDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            e.Handled = true;
            if (cts != null || isProjectActionActive) return;
            await ReceiveDroppedFilesAsync(paths);
        }

        private async Task ReceiveDroppedFilesAsync(string[] paths)
        {
            if (ProjectContext.Current == null)
            {
                if (paths.Length == 1 && string.Equals(Path.GetExtension(paths[0]), ".locproj", StringComparison.OrdinalIgnoreCase))
                {
                    try { SetProject(ProjectWorkspace.Open(paths[0])); }
                    catch (Exception error) { ShowInfoBar("无法打开协作副本", error.Message, InfoBarSeverity.Error); }
                }
                else ShowInfoBar("请先创建或打开项目", "协作副本可以直接拖入打开；文案文件和语言目录需要先打开要接收修改的项目。", InfoBarSeverity.Informational);
                return;
            }
            await RunExchangeAsync(project => ReceiveFilesAsync(project, paths));
        }

        // Both the picker and drag-and-drop use the same preview and apply path.
        private async Task ReceiveFilesAsync(ProjectWorkspace project, string[] paths)
        {
            if (project.HasPendingMerge)
            {
                await ShowMergePlanAsync(project, project.PreviewPendingMerge());
                ShowInfoBar("请再次接收新文件", "刚才打开了已保存的合并进度。完成或放弃后，再拖入新文件。", InfoBarSeverity.Informational);
                return;
            }
            var projectFiles = paths.Where(path => string.Equals(Path.GetExtension(path), ".locproj", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (projectFiles.Length > 0)
            {
                if (paths.Length != 1) throw new InvalidOperationException("请一次接收一个协作副本；多个语言 JSON 可以一起导入。");
                await ShowMergePlanAsync(project, project.PreviewCollaboration(projectFiles[0]));
                return;
            }
            var files = new List<string>();
            var skipped = 0;
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*.json"))
                    {
                        if (MatchImportLanguage(project, file) != null) files.Add(file);
                        else skipped++;
                    }
                }
                else if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)) files.Add(path);
                else throw new InvalidOperationException("请拖入协作副本、JSON 文案文件或语言目录。");
            }
            var mapped = new List<KeyValuePair<string, string>>();
            foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var language = MatchImportLanguage(project, file) ?? await ChooseImportLanguageAsync(project, file);
                if (language == null) return;
                mapped.Add(new(language, file));
            }
            if (mapped.Count == 0) throw new InvalidOperationException("没有识别到项目使用的语言文件。也可以直接拖入单个 JSON，再选择它对应的语言。");
            var plan = project.PreviewLanguageImport(mapped);
            if (skipped > 0) LogUtils.Log($"导入目录时跳过 {skipped} 个不属于当前语言规则的 JSON 文件。");
            await ShowMergePlanAsync(project, plan);
        }

        private static string? MatchImportLanguage(ProjectWorkspace project, string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var pairs = project.Pairs;
            return pairs.Where(pair => string.Equals(pair.Name, name, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Name).FirstOrDefault() ??
                pairs.Where(pair => string.Equals(pair.isoCode, name, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Name).FirstOrDefault();
        }

        private async Task<string?> ChooseImportLanguageAsync(ProjectWorkspace project, string path)
        {
            var languages = new ComboBox { ItemsSource = project.Pairs, SelectedIndex = -1, Style = (Style)FindResource("StandardComboBoxStyle") };
            var panel = new StackPanel { MinWidth = 360 };
            panel.Children.Add(new TextBlock { Text = $"“{Path.GetFileName(path)}”对应哪一种语言？", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            panel.Children.Add(languages);
            var dialog = new ContentDialog(RootContentDialogHost)
            {
                Title = "选择文案语言", Content = panel, PrimaryButtonText = "预览修改", CloseButtonText = "取消", IsPrimaryButtonEnabled = false,
            };
            languages.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = languages.SelectedIndex >= 0;
            return await dialog.ShowAsync(CancellationToken.None) == ContentDialogResult.Primary && languages.SelectedItem is Data.Pair pair ? pair.Name : null;
        }

        private async Task ShowMergePlanAsync(ProjectWorkspace project, MergePlan plan)
        {
            if (plan.Entries.Count == 0)
            {
                if (plan.IsTargetSync || project.HasPendingMerge) project.ApplyMerge(plan);
                ReloadEditorAfterMerge();
                ShowSnackbar("内容已是最新", "没有需要合并的文案修改，当前项目内容已保留。", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                return;
            }
            string? errorMessage = null;
            while (true)
            {
                using var preview = new MergePreview(plan);
                preview.SetListHeight(Math.Clamp(ActualHeight - 520, 120, 240));
                if (errorMessage != null) preview.ShowError(errorMessage);
                var scroll = new ScrollViewer
                {
                    Content = preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = Math.Max(220, ActualHeight - 230), Width = Math.Clamp(ActualWidth - 160, 640, 920),
                };
                var dialog = new ContentDialog(RootContentDialogHost)
                {
                    Title = "接收修改 · " + plan.Title, Content = scroll, DialogMaxWidth = 1000,
                    PrimaryButtonText = "合并到当前项目", SecondaryButtonText = "保存进度，稍后处理", CloseButtonText = "取消",
                    IsPrimaryButtonEnabled = plan.UnresolvedCount == 0, DefaultButton = ContentDialogButton.Close,
                };
                preview.ChoicesChanged += () => dialog.IsPrimaryButtonEnabled = plan.UnresolvedCount == 0;
                var result = await dialog.ShowAsync(CancellationToken.None);
                if (result == ContentDialogResult.None) return;
                try
                {
                    if (result == ContentDialogResult.Secondary)
                    {
                        project.SaveMergeDraft(plan);
                        ShowSnackbar("合并进度已保存", "可以继续编辑项目，之后点击“继续合并”处理剩余冲突。", ControlAppearance.Success, SymbolRegular.Save24);
                    }
                    else
                    {
                        project.ApplyMerge(plan);
                        ReloadEditorAfterMerge();
                        ShowSnackbar("修改已合并", $"已处理 {plan.Entries.Count} 项变化。可在项目操作中撤销本次合并。", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                    }
                    return;
                }
                catch (Exception error)
                {
                    errorMessage = error.Message;
                    plan = project.RefreshMergePlan(plan);
                }
            }
        }

        private void ReloadEditorAfterMerge()
        {
            if (editorSession.HasLoadedData && ComboBoxEditorTargetLanguage.SelectedItem is Data.Pair pair) LoadEditorPair(pair);
            addNewSession.RefreshState();
            UpdateProjectUi();
        }
    }
}
