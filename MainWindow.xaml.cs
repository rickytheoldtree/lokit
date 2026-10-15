using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Editing;
using LocalizationTranslationWPF.Projects;
using LocalizationTranslationWPF.Translate;
using LocalizationTranslationWPF.Utils;
using Wpf.Ui.Controls;
using static LocalizationTranslationWPF.Data.Config;

namespace LocalizationTranslationWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private CancellationTokenSource? cts;
        private readonly EditorSession editorSession = new();
        private readonly AddNewSession addNewSession = new();
        private GridLength expandedLogHeight = new(180);
        private bool isChangingEditorLanguage;
        private bool isEditorInitialized;
        private bool isAddNewInitialized;
        private bool isClosePromptActive;
        private bool isInitializingTheme;
        private bool allowWindowClose;
        private bool isWindowReady;
        private int currentPageIndex;
        private int currentEditorLanguageIndex = -1;

        private static readonly Brush InformationLogBrush = new SolidColorBrush(Color.FromRgb(113, 214, 156));
        private static readonly Brush ErrorLogBrush = new SolidColorBrush(Color.FromRgb(255, 107, 107));

        public MainWindow()
        {
            ThemeUtils.Apply(ConfigUtils.AppTheme.Value);
            InitializeComponent();
            isWindowReady = true;
            LogUtils.EntryAdded += OnLogEntryAdded;
            editorSession.StateChanged += UpdateEditingStateUi;
            addNewSession.StateChanged += UpdateEditingStateUi;
            Closed += MainWindowClosed;
            InitializeNavigation();
            RefreshLogBox();
            Init();
            RestoreRecentProject();
        }

        #region 初始化

        private void Init()
        {
            PathUtils.MakeSureFolderExist();

            var isoIndex = ConfigUtils.IsoPairIndex.Value;

            isInitializingTheme = true;
            ThemeModeComboBox.ItemsSource = ThemeChoices;
            ThemeModeComboBox.SelectedIndex = Array.FindIndex(ThemeChoices,
                choice => choice.Mode == ConfigUtils.AppTheme.Value);
            if (ThemeModeComboBox.SelectedIndex < 0) ThemeModeComboBox.SelectedIndex = 0;
            isInitializingTheme = false;

            WebDriveComboBox.ItemsSource = BrowserChoices;
            var browserIndex = Array.FindIndex(BrowserChoices, c => c.Type == ConfigUtils.WebDriver.Value);
            WebDriveComboBox.SelectedIndex = browserIndex >= 0 ? browserIndex : 0;
            ToolTipService.SetInitialShowDelay(WebDriveComboBox, 0);

            DictConverterComboBox.ItemsSource = DictConverterUtils.Converters;
            DictConverterComboBox.SelectedItem = new DictConverterType(ConfigUtils.DictConverter.Value);
            ToolTipService.SetInitialShowDelay(DictConverterComboBox, 0);

            IsoPairComboBox.ItemsSource = PairUtils.PairsCache;
            IsoPairComboBox.SelectedIndex = isoIndex >= PairUtils.PairsCache.Count ? 0 : isoIndex;
            ToolTipService.SetInitialShowDelay(IsoPairComboBox, 0);

            //IsoPairComboBox 的赋值可能把越界索引纠正回0，这里要用纠正后的值取语言表
            var pairs = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value);
            SourceLangComboBox.ItemsSource = pairs;
            SourceLangComboBox.SelectedIndex = ConfigUtils.SourceLangIndex.Value;
            ToolTipService.SetInitialShowDelay(SourceLangComboBox, 0);

            TargetLangComboBox.ItemsSource = pairs;
            TargetLangComboBox.SelectedIndex = ConfigUtils.TargetLangIndex.Value;
            ToolTipService.SetInitialShowDelay(TargetLangComboBox, 0);

            BindEnum<TranslateMethod>(TranslateMethodComboBox, (int)ConfigUtils.TranslateMethod.Value);
            TargetLangAmountComboBox.ItemsSource = new[]
            {
                new TranslationScopeChoice(TargetLangAmount.Single, "选定语言"),
                new TranslationScopeChoice(TargetLangAmount.All, "全部目标语言"),
            };
            TargetLangAmountComboBox.SelectedIndex = ConfigUtils.TargetLangAmount.Value == TargetLangAmount.Single ? 0 : 1;

            TextBoxApiEndpoint.Text = ConfigUtils.ApiEndpoint.Value;
            isInitializingApiKey = true;
            PasswordBoxApiKey.Password = ConfigUtils.ApiKey.Value;
            isInitializingApiKey = false;
            TextBoxModel.Text = ConfigUtils.AIModel.Value;
            TextBoxPrompt.Text = ConfigUtils.AIPrompt.Value;
            TextBoxDriverProxy.Text = ConfigUtils.DriverProxy.Value;
            UpdateStartButtonState();
        }

        private void MainWindowClosed(object? sender, EventArgs e)
        {
            ConfigUtils.FlushPendingSave();
            LogUtils.EntryAdded -= OnLogEntryAdded;
            editorSession.StateChanged -= UpdateEditingStateUi;
            addNewSession.StateChanged -= UpdateEditingStateUi;
            editorSession.Dispose();
            addNewSession.Dispose();
            if (observedProject != null) observedProject.Changed -= ProjectChanged;
        }

        /// <summary>
        /// 浏览器下拉项。枚举数值要兼容旧配置文件不能重排，所以这里显式指定展示顺序。
        /// </summary>
        private readonly struct BrowserChoice
        {
            public BrowserChoice(WebDriverType type, string name)
            {
                Type = type;
                Name = name;
            }

            public WebDriverType Type { get; }
            private string Name { get; }

            public override string ToString() => Name;
        }

        private static readonly BrowserChoice[] BrowserChoices =
        {
            new(WebDriverType.Auto, "自动"),
            new(WebDriverType.Edge, "Edge"),
            new(WebDriverType.Chrome, "Chrome"),
            new(WebDriverType.FireFox, "Firefox"),
        };

        private sealed record ThemeChoice(AppThemeMode Mode, string Name)
        {
            public override string ToString() => Name;
        }

        private static readonly ThemeChoice[] ThemeChoices =
        {
            new(AppThemeMode.System, "跟随系统"),
            new(AppThemeMode.Light, "浅色"),
            new(AppThemeMode.Dark, "深色"),
        };

        private sealed record TranslationScopeChoice(TargetLangAmount Amount, string Name)
        {
            public override string ToString() => Name;
        }

        private static void BindEnum<T>(Selector comboBox, int selectedIndex) where T : Enum
        {
            comboBox.ItemsSource = Enum.GetValues(typeof(T)).Cast<T>();
            comboBox.SelectedIndex = selectedIndex;
        }

        #endregion

        #region 翻译

        private async void BtnStartClick(object sender, RoutedEventArgs e)
        {
            //任务进行中，忽略重复点击
            if (cts != null) return;

            if (!TryValidateTranslationConfiguration(out var validationMessage))
            {
                ShowInfoBar("翻译配置不完整", validationMessage, InfoBarSeverity.Warning);
                return;
            }

            var tokenSource = new CancellationTokenSource();
            cts = tokenSource;
            PopWindow? popWindow = null;
            try
            {
                MainInfoBar.IsOpen = false;
                SetUILock(true);
                SetStatus("任务运行中", Brushes.DodgerBlue);

                popWindow = new PopWindow("任务正在运行，请稍候...", "取消任务", window =>
                {
                    tokenSource.Cancel();
                    SetStatus("正在取消任务", Brushes.Orange);
                })
                {
                    Owner = this
                };
                popWindow.Show();

                var succeeded = await StartTranslate(tokenSource.Token);

                if (tokenSource.IsCancellationRequested)
                {
                    SetStatus("任务已取消", Brushes.Orange);
                    ShowSnackbar("任务已取消", "翻译任务已停止。", ControlAppearance.Caution, SymbolRegular.Dismiss24);
                }
                else if (succeeded)
                {
                    SetStatus("任务已完成", Brushes.MediumSeaGreen);
                    ShowSnackbar("翻译完成", "任务已成功执行。", ControlAppearance.Success,
                        SymbolRegular.CheckmarkCircle24);
                }
                else
                {
                    SetStatus("任务完成，但存在错误", ErrorLogBrush);
                    ShowInfoBar("任务存在未完成项", "部分文件处理失败，请切换到“仅错误”查看详情。",
                        InfoBarSeverity.Error);
                }
            }
            catch (OperationCanceledException)
            {
                LogUtils.Log("任务已取消");
                SetStatus("任务已取消", Brushes.Orange);
                ShowSnackbar("任务已取消", "翻译任务已停止。", ControlAppearance.Caution, SymbolRegular.Dismiss24);
            }
            catch (Exception exception)
            {
                LogUtils.LogError(exception.Message);
                SetStatus("任务失败", ErrorLogBrush);
                ShowInfoBar("任务执行失败", exception.Message, InfoBarSeverity.Error);
            }
            finally
            {
                popWindow?.CloseFromOwner();
                tokenSource.Dispose();
                cts = null;
                SetUILock(false);
                Focus();
            }
        }

        private static Task<bool> StartTranslate(CancellationToken token)
        {
            return ConfigUtils.TranslateMethod.Value switch
            {
                TranslateMethod.GoogleTranslate => GoogleWebDriverTranslator.Run(token),
                TranslateMethod.DeepSeek => DeepSeekWebDriverTranslator.Run(false, token),
                TranslateMethod.DeepSeekWithDeepThinking => DeepSeekWebDriverTranslator.Run(true, token),
                TranslateMethod.AIApi => AIApiTranslator.Run(token),
                TranslateMethod.DeepL => DeepLWebDriverTranslator.Run(token),
                _ => Task.FromResult(false),
            };
        }

        private void SetUILock(bool isLocked)
        {
            RootNavigationView.IsEnabled = !isLocked;
            ProjectToolbar.IsEnabled = !isLocked;
            if (isLocked)
            {
                BtnStart.IsEnabled = false;
            }
            else
            {
                UpdateProjectUi();
            }
        }

        private void SetStatus(string status, Brush indicatorBrush)
        {
            StatusTextBlock.Text = status;
            StatusIndicator.Fill = indicatorBrush;
        }

        private bool TryValidateTranslationConfiguration(out string message)
        {
            if (ProjectContext.Current is not { } project)
            {
                message = "请先创建或打开本地化项目。";
                return false;
            }
            if (project.ReadLanguage(project.SourceLanguage.Name).Count == 0)
            {
                message = "源语言还没有内容，请先在新增页添加文案。";
                return false;
            }
            if (SourceLangComboBox.SelectedIndex < 0 || TargetLangComboBox.SelectedIndex < 0)
            {
                message = "请选择源语言和目标语言。";
                return false;
            }

            if (ConfigUtils.TargetLangAmount.Value == TargetLangAmount.Single &&
                ConfigUtils.TargetLang.Name == project.SourceLanguage.Name)
            {
                message = "目标语言不能与源语言相同。";
                return false;
            }

            if (TranslateMethodComboBox.SelectedIndex < 0)
            {
                message = "请选择翻译方式。";
                return false;
            }

            if (ConfigUtils.TranslateMethod.Value == TranslateMethod.AIApi)
            {
                if (!Uri.TryCreate(ConfigUtils.ApiEndpoint.Value, UriKind.Absolute, out var endpoint) ||
                    endpoint.Scheme is not ("http" or "https"))
                {
                    message = "API Endpoint 必须是有效的 HTTP 或 HTTPS 地址。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(ConfigUtils.ApiKey.Value))
                {
                    message = "请填写 API Key。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(ConfigUtils.AIModel.Value))
                {
                    message = "请填写模型名称。";
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }

        private void UpdateStartButtonState()
        {
            if (cts != null)
            {
                BtnStart.IsEnabled = false;
                return;
            }

            BtnStart.IsEnabled = TryValidateTranslationConfiguration(out var message);
            BtnStart.ToolTip = BtnStart.IsEnabled ? "开始执行翻译任务" : message;
        }

        #endregion

        #region 编辑 / 新增

        private async void ComboBoxEditorTargetLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, ComboBoxEditorTargetLanguage) || isChangingEditorLanguage) return;
            if (ComboBoxEditorTargetLanguage.SelectedIndex == -1) return;

            var nextIndex = ComboBoxEditorTargetLanguage.SelectedIndex;
            if (nextIndex == currentEditorLanguageIndex)
            {
                return;
            }

            if (!await ResolveUnsavedChangesAsync(1, "切换编辑语言", false))
            {
                RevertEditorLanguageSelection();
                return;
            }

            if (ComboBoxEditorTargetLanguage.SelectedItem is Pair pair && LoadEditorPair(pair))
            {
                currentEditorLanguageIndex = nextIndex;
                editorSession.ApplyFilter(EditorSearchTextBox.Text);
                return;
            }

            RevertEditorLanguageSelection();
        }

        private void BtnEditorSaveClick(object sender, RoutedEventArgs e)
        {
            SaveEditor();
        }

        private async void BtnEditorDeleteKeyClick(object sender, RoutedEventArgs e)
        {
            var keys = DataGridEditor.SelectedItems.OfType<DataRowView>()
                .Select(row => row[EditorSession.KeyColumn]?.ToString() ?? string.Empty)
                .Where(key => !string.IsNullOrEmpty(key))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var keyCount = keys.Length;
            if (keyCount == 0)
            {
                ShowInfoBar("没有选中 Key", "请先在表格中选择要删除的行。", InfoBarSeverity.Warning);
                return;
            }

            if (!await ResolveUnsavedChangesAsync(1, "删除选中的 Key", false))
            {
                return;
            }

            var languageCount = ProjectContext.Current?.Pairs.Count ?? 0;
            var dialog = new ContentDialog(RootContentDialogHost)
            {
                Title = "确认删除选中的 Key？",
                Content = $"将从项目的 {languageCount} 种语言中删除 {keyCount} 个 Key。发布前正式文件不变；删除会保存到项目，不能使用表格的撤销恢复。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                PrimaryButtonAppearance = ControlAppearance.Danger,
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync(CancellationToken.None) != ContentDialogResult.Primary)
            {
                return;
            }

            try
            {
                if (editorSession.DeleteKeys(keys))
                {
                    ShowSnackbar("删除完成", $"已删除 {keyCount} 个 Key。", ControlAppearance.Success,
                        SymbolRegular.CheckmarkCircle24);
                }
                else
                {
                    ShowInfoBar("删除未完全成功", "部分语言文件处理失败，请查看运行日志。", InfoBarSeverity.Error);
                }
            }
            catch (Exception exception)
            {
                LogUtils.LogError(exception.Message);
                ShowInfoBar("删除失败", exception.Message, InfoBarSeverity.Error);
            }
        }

        private async void BtnEditorRetranslateClick(object sender, RoutedEventArgs e)
        {
            if (ProjectContext.Current is not { } project) return;
            var keys = DataGridEditor.SelectedItems.OfType<DataRowView>()
                .Select(row => row[EditorSession.KeyColumn]?.ToString() ?? string.Empty)
                .Where(key => !string.IsNullOrEmpty(key))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (keys.Length == 0)
            {
                ShowInfoBar("没有选中 Key", "请先在表格中选择要重新翻译的行。", InfoBarSeverity.Warning);
                return;
            }
            if (!await ResolveUnsavedChangesAsync(1, "标记重新翻译", false)) return;

            var targets = project.Pairs.Where(pair => pair.Name != project.SourceLanguage.Name).ToList();
            var scope = new ComboBox
            {
                ItemsSource = targets.Select(pair => pair.Name).Prepend("全部目标语言").ToArray(),
                Style = (Style)FindResource("StandardComboBoxStyle"),
            };
            var currentLanguage = ComboBoxEditorTargetLanguage.SelectedItem is Pair currentPair
                ? currentPair.Name
                : null;
            var currentTargetIndex = targets.FindIndex(pair => pair.Name == currentLanguage);
            scope.SelectedIndex = currentTargetIndex < 0 ? 0 : currentTargetIndex + 1;
            var panel = new StackPanel { MinWidth = 420 };
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"已选择 {keys.Length} 个 Key。标记后，它们会和新增/缺失条目一起进入下一次翻译。旧译文会保留，直到对应语言的新结果完整校验并成功应用。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            });
            AddProjectField(panel, "目标语言", scope);
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "“取消标记”只取消重译计划，不修改当前译文。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = .68,
                Margin = new Thickness(0, 10, 0, 0),
            });
            var dialog = new ContentDialog(RootContentDialogHost)
            {
                Title = "重新翻译选中的 Key",
                Content = panel,
                PrimaryButtonText = "标记重译",
                SecondaryButtonText = "取消标记",
                CloseButtonText = "关闭",
                PrimaryButtonAppearance = ControlAppearance.Primary,
                DefaultButton = ContentDialogButton.Primary,
            };
            var result = await dialog.ShowAsync(CancellationToken.None);
            if (result == ContentDialogResult.None) return;
            try
            {
                var selectedTargets = scope.SelectedIndex <= 0
                    ? targets.Select(pair => pair.Name)
                    : new[] { targets[scope.SelectedIndex - 1].Name };
                project.SetRetranslation(keys, selectedTargets, result == ContentDialogResult.Primary);
                ShowSnackbar(result == ContentDialogResult.Primary ? "已标记待重译" : "已取消重译标记",
                    result == ContentDialogResult.Primary
                        ? $"{keys.Length} 个 Key 将在下次翻译时处理。"
                        : "当前译文没有变化。",
                    ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
            }
            catch (Exception exception)
            {
                LogUtils.LogError(exception.Message);
                ShowInfoBar("更新重译标记失败", exception.Message, InfoBarSeverity.Error);
            }
        }

        private void BtnAddNewSaveClick(object sender, RoutedEventArgs e)
        {
            SaveAddNew();
        }

        private void BtnPasteRowClick(object sender, RoutedEventArgs e)
        {
            if (!CommitGridEdits(DataGridAddNew)) return;
            var added = addNewSession.AppendTabSeparatedText(ThreadUtils.GetClipboard());
            if (added > 0)
            {
                AddNewSearchTextBox.Text = string.Empty;
                ShowSnackbar("粘贴完成", $"已添加 {added} 行。", ControlAppearance.Success,
                    SymbolRegular.ClipboardPaste24);
            }
            else
            {
                ShowInfoBar("没有可粘贴的内容", "请复制以 Tab 分隔的 Key 和文本。", InfoBarSeverity.Warning);
            }
        }

        private void BtnEditorUndoClick(object sender, RoutedEventArgs e)
        {
            CancelGridEdits(DataGridEditor);
            if (editorSession.DiscardChanges())
            {
                ShowSnackbar("已撤销更改", "编辑器已恢复到最近一次保存或加载的内容。",
                    ControlAppearance.Success, SymbolRegular.ArrowCounterclockwise24);
            }
        }

        private void BtnAddNewUndoClick(object sender, RoutedEventArgs e)
        {
            CancelGridEdits(DataGridAddNew);
            if (addNewSession.DiscardChanges())
            {
                ShowSnackbar("已撤销更改", "新增表格已恢复到最近一次保存或加载的内容。",
                    ControlAppearance.Success, SymbolRegular.ArrowCounterclockwise24);
            }
        }

        private void BtnAddNewRowClick(object sender, RoutedEventArgs e)
        {
            if (!CommitGridEdits(DataGridAddNew)) return;
            AddNewSearchTextBox.Text = string.Empty;
            var row = addNewSession.AddEmptyRow();
            DataGridAddNew.SelectedItem = row;
            DataGridAddNew.ScrollIntoView(row);
            DataGridAddNew.CurrentCell = new DataGridCellInfo(row, DataGridAddNew.Columns[0]);
            DataGridAddNew.BeginEdit();
        }

        private void BtnAddNewDeleteRowClick(object sender, RoutedEventArgs e)
        {
            if (!CommitGridEdits(DataGridAddNew)) return;
            var rows = DataGridAddNew.SelectedItems.OfType<DataRowView>().ToArray();
            var deleted = addNewSession.DeleteRows(rows);
            if (deleted > 0)
            {
                ShowSnackbar("已移除", $"已从待新增列表移除 {deleted} 行，保存后才会写入文件。",
                    ControlAppearance.Success, SymbolRegular.Delete24);
            }
        }

        private void MainWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (isProjectActionActive || cts != null) return;
            if (e.Key != Key.S || Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            switch (currentPageIndex)
            {
                case 1:
                    SaveEditor();
                    e.Handled = true;
                    break;
                case 2:
                    SaveAddNew();
                    e.Handled = true;
                    break;
            }
        }

        private bool SaveEditor(bool showNotification = true)
        {
            try
            {
                if (!CommitGridEdits(DataGridEditor)) return false;
                if (editorSession.Save())
                {
                    if (showNotification)
                    {
                        ShowSnackbar("已保存到项目", "工作副本已保存；发布前正式文件保持不变。", ControlAppearance.Success,
                            SymbolRegular.Save24);
                    }
                    return true;
                }

                ShowInfoBar("保存失败", "请查看运行日志中的详细原因。", InfoBarSeverity.Error);
                return false;
            }
            catch (EditorMergeConflictException conflict)
            {
                try
                {
                    var project = ProjectContext.Current!;
                    project.SaveMergeDraft(conflict.Plan);
                    ReloadEditorAfterMerge();
                    ShowInfoBar("编辑已保存为待合并内容", "其他人也修改了部分文案。点击顶部“继续合并”选择要保留的内容，你的编辑已保存在项目中。", InfoBarSeverity.Warning);
                }
                catch (Exception error) { ShowInfoBar("保存合并进度失败", error.Message + " 你的编辑仍保留在表格中。", InfoBarSeverity.Error); }
                return false;
            }
            catch (Exception exception)
            {
                LogUtils.LogError(exception.Message);
                ShowInfoBar("保存失败", exception.Message, InfoBarSeverity.Error);
                return false;
            }
        }

        private bool SaveAddNew(bool showNotification = true)
        {
            try
            {
                if (!CommitGridEdits(DataGridAddNew)) return false;
                if (addNewSession.ValidationErrorCount > 0)
                {
                    ShowInfoBar("请先修正 Key", "Key 不能为空或重复；将鼠标停在标红的行上可查看原因。",
                        InfoBarSeverity.Warning);
                    return false;
                }
                if (addNewSession.Save())
                {
                    if (showNotification)
                    {
                        ShowSnackbar("已加入项目", "新增文案已加入源语言，可直接开始翻译。", ControlAppearance.Success,
                            SymbolRegular.Save24);
                    }
                    return true;
                }

                ShowInfoBar("保存失败", "请查看运行日志中的详细原因。", InfoBarSeverity.Error);
                return false;
            }
            catch (Exception exception)
            {
                LogUtils.LogError(exception.Message);
                ShowInfoBar("保存失败", exception.Message, InfoBarSeverity.Error);
                return false;
            }
        }

        private void InitializeEditingPage(int tabIndex)
        {
            if (ProjectContext.Current == null) { UpdateEditingStateUi(); return; }
            switch (tabIndex)
            {
                case 1:
                    if (!isEditorInitialized)
                    {
                        isChangingEditorLanguage = true;
                        editorSession.RefreshPairs();
                        ComboBoxEditorTargetLanguage.ItemsSource = editorSession.Pairs;
                        ComboBoxEditorTargetLanguage.SelectedIndex = editorSession.Pairs.Count > 0 ? 0 : -1;
                        isEditorInitialized = true;
                        currentEditorLanguageIndex = ComboBoxEditorTargetLanguage.SelectedIndex;
                        if (ComboBoxEditorTargetLanguage.SelectedItem is Pair pair)
                        {
                            LoadEditorPair(pair);
                        }
                        isChangingEditorLanguage = false;
                    }
                    else if ((!editorSession.HasLoadedData || (!editorSession.IsDirty && editorSession.NeedsReload)) &&
                             ComboBoxEditorTargetLanguage.SelectedItem is Pair pair)
                    {
                        LoadEditorPair(pair);
                    }

                    editorSession.ApplyFilter(EditorSearchTextBox.Text);
                    break;

                case 2:
                    if (!isAddNewInitialized)
                    {
                        DataGridAddNew.ItemsSource = addNewSession.View;
                        isAddNewInitialized = true;
                    }

                    addNewSession.RefreshState();
                    addNewSession.ApplyFilter(AddNewSearchTextBox.Text);
                    break;
            }
        }

        private async Task<bool> ResolveUnsavedChangesAsync(int tabIndex, string action, bool discardImmediately)
        {
            var isDirty = tabIndex switch
            {
                1 => editorSession.IsDirty,
                2 => addNewSession.IsDirty,
                _ => false,
            };
            if (!isDirty)
            {
                return true;
            }

            var pageName = tabIndex == 1 ? "编辑页" : "新增页";
            var dialog = new ContentDialog(RootContentDialogHost)
            {
                Title = $"{pageName}有未保存的更改",
                Content = $"要在{action}前保存这些更改吗？",
                PrimaryButtonText = "保存",
                SecondaryButtonText = "不保存",
                CloseButtonText = "取消",
                PrimaryButtonAppearance = ControlAppearance.Primary,
                DefaultButton = ContentDialogButton.Primary,
            };

            var result = await dialog.ShowAsync(CancellationToken.None);
            if (result == ContentDialogResult.None)
            {
                return false;
            }

            if (result == ContentDialogResult.Primary)
            {
                return tabIndex == 1 ? SaveEditor(false) : SaveAddNew(false);
            }

            if (!discardImmediately)
            {
                return true;
            }

            CancelGridEdits(tabIndex == 1 ? DataGridEditor : DataGridAddNew);
            return tabIndex == 1 ? editorSession.DiscardChanges() : addNewSession.DiscardChanges();
        }

        private void RevertEditorLanguageSelection()
        {
            isChangingEditorLanguage = true;
            ComboBoxEditorTargetLanguage.SelectedIndex = currentEditorLanguageIndex;
            isChangingEditorLanguage = false;
        }

        private bool LoadEditorPair(Pair pair)
        {
            return editorSession.Load(pair);
        }

        private bool CommitGridEdits(System.Windows.Controls.DataGrid grid)
        {
            if (grid.CommitEdit(DataGridEditingUnit.Cell, true) &&
                grid.CommitEdit(DataGridEditingUnit.Row, true)) return true;
            ShowInfoBar("无法提交当前编辑", "请先修正当前单元格，或按 Esc 取消本次编辑。", InfoBarSeverity.Warning);
            return false;
        }

        private static void CancelGridEdits(System.Windows.Controls.DataGrid grid)
        {
            grid.CancelEdit(DataGridEditingUnit.Cell);
            grid.CancelEdit(DataGridEditingUnit.Row);
        }

        private void EditorSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            editorSession.ApplyFilter(EditorSearchTextBox.Text);
        }

        private void AddNewSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            addNewSession.ApplyFilter(AddNewSearchTextBox.Text);
        }

        private void EditingDataGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateEditingStateUi();
        }

        private void EditingDataGridEditEnding(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ReferenceEquals(sender, DataGridAddNew)) addNewSession.RefreshState();
                else UpdateEditingStateUi();
            }), System.Windows.Threading.DispatcherPriority.DataBind);
        }

        private void DataGridEditorAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyName == EditorSession.ValueColumn) e.Column.Header = editorSession.CurrentLanguageName;
            ConfigureDataGridColumn(e, e.PropertyName == EditorSession.KeyColumn ? 2 : 3);
        }

        private void DataGridAddNewAutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyName == AddNewSession.ValidationColumn)
            {
                e.Cancel = true;
                return;
            }
            e.Column.Header = e.PropertyName == AddNewSession.ValueColumn ? "文本" : e.PropertyName;
            ConfigureDataGridColumn(e, e.PropertyName == AddNewSession.KeyColumn ? 2 : 3);
        }

        private static void ConfigureDataGridColumn(DataGridAutoGeneratingColumnEventArgs e, double width)
        {
            e.Column.Width = new DataGridLength(width, DataGridLengthUnitType.Star);
            e.Column.MinWidth = e.PropertyName == EditorSession.KeyColumn ? 180 : 240;
            e.Column.CanUserSort = true;

            if (e.Column is DataGridTextColumn textColumn)
            {
                // DataGridColumn does not participate in visual-tree inheritance.
                // Apply theme-aware styles explicitly to both generated elements.
                textColumn.ElementStyle = (Style)Application.Current.FindResource("LocalizationDataGridTextStyle");
                textColumn.EditingElementStyle = (Style)Application.Current.FindResource("LocalizationDataGridEditingTextBoxStyle");
            }

            if (e.Column is DataGridBoundColumn { Binding: Binding binding })
            {
                binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
                // Business validation is displayed on rows and checked before saving. Blocking
                // cell/row commits here can trap the user before they can fix another duplicate.
                binding.ValidatesOnDataErrors = false;
            }
        }

        private void UpdateEditingStateUi()
        {
            if (!isWindowReady) return;
            if (isEditorInitialized && !ReferenceEquals(DataGridEditor.ItemsSource, editorSession.View))
            {
                DataGridEditor.ItemsSource = editorSession.View;
            }
            EditorDirtyTextBlock.Visibility = editorSession.IsDirty ? Visibility.Visible : Visibility.Collapsed;
            AddNewDirtyTextBlock.Visibility = addNewSession.IsDirty ? Visibility.Visible : Visibility.Collapsed;
            EditorRowCountTextBlock.Text = $"显示 {editorSession.VisibleRowCount} / {editorSession.TotalRowCount} 行";
            AddNewRowCountTextBlock.Text = $"显示 {addNewSession.VisibleRowCount} / {addNewSession.TotalRowCount} 行";
            EditorEmptyValueTextBlock.Text = editorSession.EmptyValueCount > 0
                ? $"{editorSession.EmptyValueCount} 条空文本"
                : string.Empty;
            EditorEmptyValueTextBlock.Visibility = editorSession.EmptyValueCount > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            var pendingRetranslations = ProjectContext.Current?.PendingRetranslationCount ?? 0;
            EditorRetranslateTextBlock.Text = $"{pendingRetranslations} 项待重译";
            EditorRetranslateTextBlock.Visibility = pendingRetranslations > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            AddNewValidationTextBlock.Text = addNewSession.ValidationErrorCount > 0
                ? $"{addNewSession.ValidationErrorCount} 行需修正"
                : string.Empty;
            AddNewValidationTextBlock.Visibility = addNewSession.ValidationErrorCount > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            BtnEditorSave.IsEnabled = editorSession.HasLoadedData && editorSession.IsDirty;
            BtnEditorUndo.IsEnabled = editorSession.IsDirty;
            BtnEditorRetranslate.IsEnabled = ProjectContext.Current != null && DataGridEditor.SelectedItems.Count > 0;
            BtnEditorDeleteKey.IsEnabled = DataGridEditor.SelectedItems.Count > 0;
            BtnAddNewSave.IsEnabled = addNewSession.IsDirty && addNewSession.ValidationErrorCount == 0;
            BtnAddNewUndo.IsEnabled = addNewSession.IsDirty;
            BtnAddNewDeleteRow.IsEnabled = DataGridAddNew.SelectedItems.Count > 0;
            var hasProject = ProjectContext.Current != null;
            BtnAddNewRow.IsEnabled = hasProject;
            BtnPasteRow.IsEnabled = hasProject;
            UpdateEditingEmptyStates();

            var hasUnsavedChanges = editorSession.IsDirty || addNewSession.IsDirty;
            Title = (ProjectContext.Current?.Name is { } projectName ? projectName + " · " : "") +
                (hasUnsavedChanges ? "本地化工具 · 未保存" : "本地化工具");
            TitleBarDirtyIndicator.Visibility = hasUnsavedChanges ? Visibility.Visible : Visibility.Hidden;
        }

        private void UpdateEditingEmptyStates()
        {
            var showEditorEmptyState = !editorSession.HasLoadedData || editorSession.VisibleRowCount == 0;
            EditorEmptyStateBorder.Visibility = showEditorEmptyState ? Visibility.Visible : Visibility.Collapsed;
            if (!editorSession.HasLoadedData)
            {
                EditorEmptyStateTitle.Text = "尚未打开项目";
                EditorEmptyStateDescription.Text = "新建或打开项目后，可直接编辑所有语言。";
            }
            else if (editorSession.TotalRowCount == 0)
            {
                EditorEmptyStateTitle.Text = "语言文件中还没有内容";
                EditorEmptyStateDescription.Text = "可以先在“新增”页准备本地化键值。";
            }
            else
            {
                EditorEmptyStateTitle.Text = "没有匹配的结果";
                EditorEmptyStateDescription.Text = "换一个关键词，或清空搜索条件。";
            }

            var showAddNewEmptyState = addNewSession.VisibleRowCount == 0;
            AddNewEmptyStateBorder.Visibility = showAddNewEmptyState ? Visibility.Visible : Visibility.Collapsed;
            if (addNewSession.TotalRowCount == 0)
            {
                AddNewEmptyStateTitle.Text = "还没有待新增内容";
                AddNewEmptyStateDescription.Text = "可以新增空行，或粘贴以 Tab 分隔的 Key 和文本。";
            }
            else
            {
                AddNewEmptyStateTitle.Text = "没有匹配的结果";
                AddNewEmptyStateDescription.Text = "换一个关键词，或清空搜索条件。";
            }
        }

        private async void MainWindowClosing(object? sender, CancelEventArgs e)
        {
            if (allowWindowClose || (!editorSession.IsDirty && !addNewSession.IsDirty))
            {
                ThemeUtils.Detach(this);
                return;
            }

            e.Cancel = true;
            if (isClosePromptActive)
            {
                return;
            }

            isClosePromptActive = true;
            try
            {
                if (!await ResolveUnsavedChangesAsync(1, "关闭程序", true) ||
                    !await ResolveUnsavedChangesAsync(2, "关闭程序", true))
                {
                    return;
                }

                allowWindowClose = true;
                Close();
            }
            finally
            {
                isClosePromptActive = false;
            }
        }

        private void OnLogEntryAdded(LogEntry entry)
        {
            if (ErrorsOnlyToggleButton.IsChecked == true && entry.Level != LogLevel.Error)
            {
                return;
            }

            AppendLogEntry(entry);
        }

        private void AppendLogEntry(LogEntry entry)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
            paragraph.Inlines.Add(new Run($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {entry.Text}")
            {
                Foreground = entry.Level == LogLevel.Error ? ErrorLogBrush : InformationLogBrush,
            });
            LogBox.Document.Blocks.Add(paragraph);

            if (AutoScrollToggleButton.IsChecked == true)
            {
                LogBox.ScrollToEnd();
            }
        }

        private void RefreshLogBox()
        {
            LogBox.Document.Blocks.Clear();
            var errorsOnly = ErrorsOnlyToggleButton.IsChecked == true;
            foreach (var entry in LogUtils.GetEntries())
            {
                if (!errorsOnly || entry.Level == LogLevel.Error)
                {
                    AppendLogEntry(entry);
                }
            }
        }

        private void LogFilterChanged(object sender, RoutedEventArgs e)
        {
            RefreshLogBox();
        }

        private void BtnClearLogClick(object sender, RoutedEventArgs e)
        {
            LogUtils.Clear();
            LogBox.Document.Blocks.Clear();
            ShowSnackbar("日志已清空", "当前运行日志已经清除。", ControlAppearance.Secondary,
                SymbolRegular.DeleteDismiss24);
        }

        private void BtnCopyLogClick(object sender, RoutedEventArgs e)
        {
            var text = new TextRange(LogBox.Document.ContentStart, LogBox.Document.ContentEnd).Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                ShowInfoBar("没有可复制的日志", "当前日志视图为空。", InfoBarSeverity.Informational);
                return;
            }

            try
            {
                Clipboard.SetText(text);
                ShowSnackbar("已复制日志", "当前日志视图已复制到剪贴板。", ControlAppearance.Success,
                    SymbolRegular.Copy24);
            }
            catch (Exception exception)
            {
                ShowInfoBar("复制失败", exception.Message, InfoBarSeverity.Error);
            }
        }

        private void BtnToggleLogClick(object sender, RoutedEventArgs e)
        {
            if (LogPanel.Visibility == Visibility.Visible)
            {
                expandedLogHeight = new GridLength(Math.Max(LogRow.ActualHeight, 100));
                LogPanel.Visibility = Visibility.Collapsed;
                LogSplitter.Visibility = Visibility.Collapsed;
                LogRow.Height = new GridLength(0);
                BtnToggleLog.Content = "展开日志";
                return;
            }

            LogRow.Height = expandedLogHeight;
            LogPanel.Visibility = Visibility.Visible;
            LogSplitter.Visibility = Visibility.Visible;
            BtnToggleLog.Content = "收起日志";
        }

        private void ShowSnackbar(string title, string message, ControlAppearance appearance, SymbolRegular symbol)
        {
            var snackbar = new Snackbar(RootSnackbarPresenter)
            {
                Title = title,
                Content = message,
                Appearance = appearance,
                Icon = new SymbolIcon { Symbol = symbol },
                IsCloseButtonEnabled = false,
                Timeout = TimeSpan.FromSeconds(4),
            };
            snackbar.Show();
        }

        private void ShowInfoBar(string title, string message, InfoBarSeverity severity)
        {
            MainInfoBar.Title = title;
            MainInfoBar.Message = message;
            MainInfoBar.Severity = severity;
            MainInfoBar.IsOpen = true;
        }

        #endregion

        private void BtnOpenIsoClick(object sender, RoutedEventArgs e) => PathUtils.OpenIsoFolder();
        private void BtnDictConverterClick(object sender, RoutedEventArgs e) => PathUtils.OpenDictConverterFolder();
    }
}
