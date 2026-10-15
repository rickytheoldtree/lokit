using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
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
        private bool isSwitchingProject;
        private bool isProjectActionActive;
        private ProjectWorkspace? observedProject;

        private async void BtnNewProjectClick(object sender, RoutedEventArgs e)
        {
            if (cts != null || isProjectActionActive) return;
            isProjectActionActive = true;
            try
            {
                if (!await ResolveProjectEditsAsync("创建项目")) return;
                var folderDialog = new OpenFolderDialog { Title = "选择正式语言文件目录（也可以是空目录）" };
                if (folderDialog.ShowDialog(this) != true) return;
                var rules = new ComboBox
                {
                    ItemsSource = PairUtils.PairsCache,
                    DisplayMemberPath = "Key",
                    Style = (Style)FindResource("StandardComboBoxStyle"),
                    SelectedIndex = Math.Clamp(ConfigUtils.IsoPairIndex.Value, 0, Math.Max(0, PairUtils.PairsCache.Count - 1)),
                };
                var source = new ComboBox { Style = (Style)FindResource("StandardComboBoxStyle") };
                var format = new ComboBox
                {
                    ItemsSource = DictConverterUtils.Converters,
                    DisplayMemberPath = "Name",
                    Style = (Style)FindResource("StandardComboBoxStyle"),
                    SelectedItem = new DictConverterType(ConfigUtils.DictConverter.Value),
                };
                if (format.SelectedIndex < 0) format.SelectedIndex = 0;
                var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .68, Margin = new Thickness(0, 12, 0, 0) };
                void RefreshRules()
                {
                    var pairs = PairUtils.GetPairs(rules.SelectedIndex);
                    source.ItemsSource = pairs;
                    source.SelectedIndex = 0;
                    var matched = pairs.Count(p => File.Exists(Path.Combine(folderDialog.FolderName, p.Name + ".json")));
                    hint.Text = $"识别 {matched}/{pairs.Count} 个语言文件。缺少的语言从空内容开始；其他文件不读取也不发布。\n创建项目只读取正式文件，不修改它们。";
                }
                rules.SelectionChanged += (_, _) => RefreshRules();
                RefreshRules();
                var panel = new StackPanel { MinWidth = 420 };
                panel.Children.Add(new TextBlock { Text = folderDialog.FolderName, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
                AddProjectField(panel, "语言文件命名规则", rules);
                AddProjectField(panel, "文件格式", format);
                AddProjectField(panel, "源语言（项目内固定）", source);
                panel.Children.Add(hint);
                var dialog = new ContentDialog(RootContentDialogHost)
                {
                    Title = "新建本地化项目",
                    Content = panel,
                    PrimaryButtonText = "选择项目保存位置",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Primary,
                };
                if (await dialog.ShowAsync(CancellationToken.None) != ContentDialogResult.Primary) return;
                if (source.SelectedItem is not Pair sourcePair || format.SelectedItem is not DictConverterType converter)
                    throw new InvalidOperationException("请选择源语言和文件格式。");
                var save = new SaveFileDialog
                {
                    Title = "保存项目工作副本（不需要放在游戏目录）",
                    Filter = "本地化项目 (*.locproj)|*.locproj",
                    DefaultExt = ".locproj",
                    FileName = new DirectoryInfo(folderDialog.FolderName).Name + ".locproj",
                };
                if (save.ShowDialog(this) != true) return;
                SetProject(ProjectWorkspace.Create(save.FileName, folderDialog.FolderName,
                    PairUtils.GetPairs(rules.SelectedIndex), sourcePair.Name, converter.type!));
                ShowSnackbar("项目已创建", "新增和编辑保存到工作副本，发布时才写回正式目录。", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
            }
            catch (Exception error) { ShowInfoBar("创建项目失败", error.Message, InfoBarSeverity.Error); }
            finally { isProjectActionActive = false; }
        }

        private static void AddProjectField(Panel panel, string label, UIElement element)
        {
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) });
            panel.Children.Add(element);
        }

        private async void BtnOpenProjectClick(object sender, RoutedEventArgs e)
        {
            if (cts != null || isProjectActionActive) return;
            isProjectActionActive = true;
            try
            {
                if (!await ResolveProjectEditsAsync("打开项目")) return;
                var dialog = new OpenFileDialog { Title = "打开本地化项目", Filter = "本地化项目 (*.locproj)|*.locproj" };
                if (dialog.ShowDialog(this) == true) SetProject(ProjectWorkspace.Open(dialog.FileName));
            }
            catch (Exception error) { ShowInfoBar("打开项目失败", error.Message, InfoBarSeverity.Error); }
            finally { isProjectActionActive = false; }
        }

        private async void BtnCloseProjectClick(object sender, RoutedEventArgs e)
        {
            if (cts != null || isProjectActionActive) return;
            isProjectActionActive = true;
            try { if (await ResolveProjectEditsAsync("关闭项目")) SetProject(null); }
            finally { isProjectActionActive = false; }
        }

        private async Task<bool> ResolveProjectEditsAsync(string action) =>
            await ResolveUnsavedChangesAsync(1, action, true) && await ResolveUnsavedChangesAsync(2, action, true);

        private void SetProject(ProjectWorkspace? project)
        {
            if (observedProject != null) observedProject.Changed -= ProjectChanged;
            isSwitchingProject = true;
            try
            {
                ProjectContext.Current = project;
                observedProject = project;
                editorSession.Reset();
                addNewSession.Reset();
                EditorSearchTextBox.Text = string.Empty;
                AddNewSearchTextBox.Text = string.Empty;
                isEditorInitialized = false;
                currentEditorLanguageIndex = -1;
                ComboBoxEditorTargetLanguage.ItemsSource = null;
                DataGridEditor.ItemsSource = null;
                var pairs = project?.Pairs.ToList() ?? new List<Pair>();
                SourceLangComboBox.ItemsSource = pairs;
                TargetLangComboBox.ItemsSource = pairs;
                SourceLangComboBox.SelectedIndex = project == null ? -1 : pairs.FindIndex(p => p.Name == project.SourceLanguage.Name);
                TargetLangComboBox.SelectedIndex = project == null ? -1 : pairs.FindIndex(p => p.Name != project.SourceLanguage.Name);
                if (project != null)
                {
                    ConfigUtils.DictConverter.Value = project.ConverterType;
                    DictConverterComboBox.SelectedItem = new DictConverterType(project.ConverterType);
                    project.Changed += ProjectChanged;
                }
                ConfigUtils.LastProject.Value = project?.FilePath ?? string.Empty;
            }
            finally { isSwitchingProject = false; }
            InitializeEditingPage(currentPageIndex);
            UpdateProjectUi();
        }

        private void RestoreRecentProject()
        {
            var recent = ConfigUtils.LastProject.Value;
            try { SetProject(!string.IsNullOrWhiteSpace(recent) && File.Exists(recent) ? ProjectWorkspace.Open(recent) : null); }
            catch (Exception error)
            {
                SetProject(null);
                ShowInfoBar("上次项目无法打开", error.Message, InfoBarSeverity.Warning);
            }
        }

        private void ProjectChanged()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(UpdateProjectUi)); return; }
            UpdateProjectUi();
        }

        private void UpdateProjectUi()
        {
            if (!isWindowReady) return;
            var project = ProjectContext.Current;
            ProjectNameTextBlock.Text = project?.Name ?? "未打开项目";
            ProjectNameTextBlock.ToolTip = project?.FilePath;
            ProjectSummaryTextBlock.Text = project == null ? "打开协作副本即可继续编辑"
                : $"{project.SourceLanguage.Name} · {project.Pairs.Count} 种语言 · {project.PendingChangeCount} 项" + (project.IsCollaborationCopy ? "修订" : "待导出") +
                  (project.PendingRetranslationCount > 0 ? $" · {project.PendingRetranslationCount} 项待重译" : string.Empty);
            ProjectSummaryTextBlock.ToolTip = project?.TargetDirectory;
            var available = project != null && cts == null && !isProjectActionActive;
            BtnPublishProject.IsEnabled = available && project!.HasPublishTarget;
            BtnPublishProject.Visibility = project?.HasPublishTarget == true ? Visibility.Visible : Visibility.Collapsed;
            BtnReceiveChanges.IsEnabled = available;
            BtnReceiveChanges.Visibility = project == null ? Visibility.Collapsed : Visibility.Visible;
            BtnShareProject.IsEnabled = available;
            BtnShareProject.Visibility = project == null ? Visibility.Collapsed : Visibility.Visible;
            BtnShareProject.Content = project?.IsCollaborationCopy == true ? "保存并发回" : "发给协作者";
            BtnContinueMerge.IsEnabled = available;
            BtnContinueMerge.Visibility = project?.HasPendingMerge == true ? Visibility.Visible : Visibility.Collapsed;
            BtnCreateProject.Visibility = project == null ? Visibility.Visible : Visibility.Collapsed;
            BtnOpenProject.Visibility = project == null ? Visibility.Visible : Visibility.Collapsed;
            BtnProjectActions.IsEnabled = cts == null && !isProjectActionActive;
            ProjectDropHint.Text = project == null ? "拖入协作副本直接打开"
                : project.IsCollaborationCopy ? "直接编辑或新增文案，完成后“保存并发回”。也可以拖入文件接收修改。"
                : "拖入协作副本、文案文件或语言目录，预览并合并到当前项目。";
            ProjectWelcomeCard.Visibility = project == null ? Visibility.Visible : Visibility.Collapsed;
            TranslationConfigurationCard.Visibility = project == null ? Visibility.Collapsed : Visibility.Visible;
            TranslationUtilitiesCard.Visibility = project == null ? Visibility.Collapsed : Visibility.Visible;
            SourceLangComboBox.IsEnabled = false;
            DictConverterComboBox.IsEnabled = project == null;
            IsoPairComboBox.IsEnabled = project == null;
            ProjectLocationTextBlock.Text = project?.IsCollaborationCopy == true
                ? "修改会保存到这份协作副本。完成后点击“保存并发回”，由项目负责人合并；无需配置游戏目录。"
                : "修改保存在项目中。通过“发给协作者”交给同事继续编辑，收到修订后用“接收修改”合并。检查完成后可导出游戏资源。";
            AddNewPageDescription.Text = project == null ? "请先创建或打开项目。"
                : $"新增到 {project.SourceLanguage.Name}，保存后可直接翻译；正式文件在发布前保持不变。";
            UpdateEditingStateUi();
            UpdateStartButtonState();
        }

        private void BtnProjectActionsClick(object sender, RoutedEventArgs e)
        {
            BtnProjectActions.ContextMenu.PlacementTarget = BtnProjectActions;
            BtnProjectActions.ContextMenu.IsOpen = true;
        }
        private void BtnOpenProjectFolderClick(object sender, RoutedEventArgs e)
        {
            if (ProjectContext.Current is { } project) PathUtils.OpenFolder(Path.GetDirectoryName(project.FilePath)!);
        }
        private void BtnOpenTargetFolderClick(object sender, RoutedEventArgs e)
        {
            if (ProjectContext.Current is { HasPublishTarget: true } project) PathUtils.OpenFolder(project.TargetDirectory);
        }

        private async void BtnPublishProjectClick(object sender, RoutedEventArgs e)
        {
            if (cts != null || isProjectActionActive || ProjectContext.Current is not { } project) return;
            isProjectActionActive = true;
            try
            {
                if (!await ResolveProjectEditsAsync("预览发布")) return;
                var plan = project.PreviewPublish();
                if (plan.Conflicts.Count > 0)
                {
                    ShowInfoBar("发布已阻止", string.Join("\n", plan.Conflicts), InfoBarSeverity.Warning);
                    return;
                }
                if (!plan.CanPublish)
                {
                    if (project.PendingRetranslationCount > 0)
                    {
                        ShowInfoBar("仍有待重译条目",
                            $"还有 {project.PendingRetranslationCount} 项目标译文等待重新翻译；当前没有内容变更可发布。",
                            InfoBarSeverity.Warning);
                        return;
                    }
                    ShowSnackbar("没有待发布更改", "项目工作副本与创建项目/上次发布时的内容一致。", ControlAppearance.Secondary, SymbolRegular.CheckmarkCircle24);
                    return;
                }
                var panel = new StackPanel { MinWidth = 620 };
                panel.Children.Add(new TextBlock
                {
                    Text = $"目标：{project.TargetDirectory}\n{plan.Files.Count} 个文件 · 新增 {plan.Changes.Count(c => c.Kind == "新增")} · 修改 {plan.Changes.Count(c => c.Kind == "修改")} · 删除 {plan.Changes.Count(c => c.Kind == "删除")}\n发布前会自动备份原文件。",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12),
                });
                if (project.PendingRetranslationCount > 0)
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"注意：仍有 {project.PendingRetranslationCount} 项目标译文待重译。发布不会清除这些标记。",
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 12),
                    });
                var grid = new Wpf.Ui.Controls.DataGrid
                {
                    ItemsSource = plan.Changes,
                    IsReadOnly = true,
                    AutoGenerateColumns = false,
                    Height = 270,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    Style = (Style)FindResource("LocalizationDataGridStyle"),
                };
                foreach (var column in new[] { ("语言", "Language", 1d), ("类型", "Kind", .6d), ("Key", "Key", 1.2d), ("原内容", "Before", 1.5d), ("新内容", "After", 1.5d) })
                    grid.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2), Width = new DataGridLength(column.Item3, DataGridLengthUnitType.Star) });
                panel.Children.Add(grid);
                var dialog = new ContentDialog(RootContentDialogHost)
                {
                    Title = "发布预览",
                    Content = panel,
                    PrimaryButtonText = "备份并发布",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    DialogMaxWidth = 840,
                };
                if (await dialog.ShowAsync(CancellationToken.None) != ContentDialogResult.Primary) return;
                var backup = project.Publish(plan);
                ShowSnackbar("发布完成", $"已写回 {plan.Files.Count} 个文件。可从“项目操作”恢复本次发布。", ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                LogUtils.Log($"发布备份：{backup}");
            }
            catch (Exception error) { ShowInfoBar("发布失败", error.Message, InfoBarSeverity.Error); }
            finally { isProjectActionActive = false; }
        }

        private async void BtnRestorePublishClick(object sender, RoutedEventArgs e)
        {
            if (cts != null || isProjectActionActive || ProjectContext.Current is not { } project) return;
            isProjectActionActive = true;
            try
            {
                if (!project.HasBackup) { ShowInfoBar("没有发布备份", "完成首次发布后才可以恢复。", InfoBarSeverity.Informational); return; }
                var dialog = new ContentDialog(RootContentDialogHost)
                {
                    Title = "恢复上次发布？",
                    Content = $"正式目录：{project.TargetDirectory}\n正式文件将恢复到上次发布前；当前工作副本保留，变为未发布状态。如果正式文件已被外部修改，将阻止恢复。",
                    PrimaryButtonText = "恢复正式文件",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await dialog.ShowAsync(CancellationToken.None) != ContentDialogResult.Primary) return;
                project.RestoreLastPublish();
                ShowSnackbar("已恢复正式文件", "工作副本保持不变，可继续编辑或重新发布。", ControlAppearance.Success, SymbolRegular.ArrowCounterclockwise24);
            }
            catch (Exception error) { ShowInfoBar("恢复失败", error.Message, InfoBarSeverity.Error); }
            finally { isProjectActionActive = false; }
        }
    }
}
