using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Translate;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LocalizationTranslationWPF.Data;
using System.Threading;
using System.Threading.Tasks;
using LocalizationTranslationWPF.Utils;
using static LocalizationTranslationWPF.Data.Config;

namespace LocalizationTranslationWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private CancellationTokenSource? cts;

        public MainWindow()
        {
            InitializeComponent();
            Init();
        }

        #region 初始化

        private void Init()
        {
            PathUtils.MakeSureFolderExist();

            var isoIndex = ConfigUtils.IsoPairIndex.Value;

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
            BindEnum<TargetLangAmount>(TargetLangAmountComboBox, (int)ConfigUtils.TargetLangAmount.Value);

            TextBoxApiEndpoint.Text = ConfigUtils.ApiEndpoint.Value;
            TextBoxApiKey.Text = ConfigUtils.ApiKey.Value;
            TextBoxModel.Text = ConfigUtils.AIModel.Value;
            TextBoxPrompt.Text = ConfigUtils.AIPrompt.Value;
            TextBoxDriverProxy.Text = ConfigUtils.DriverProxy.Value;
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

            var tokenSource = new CancellationTokenSource();
            cts = tokenSource;
            PopWindow? popWindow = null;
            try
            {
                SetUILock(true);

                popWindow = new PopWindow("任务正在运行，请稍候...", "取消任务", window =>
                {
                    tokenSource.Cancel();
                    window.TextBlock.Text = "正在取消任务...";
                    window.Btn.IsEnabled = false;
                })
                {
                    Owner = this
                };
                popWindow.Show();

                await StartTranslate(tokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                LogUtils.LogError("任务已取消");
            }
            catch (Exception exception)
            {
                LogUtils.LogError(exception.Message);
            }
            finally
            {
                popWindow?.Close();
                tokenSource.Dispose();
                cts = null;
                SetUILock(false);
                Focus();
            }
        }

        private static Task StartTranslate(CancellationToken token)
        {
            return ConfigUtils.TranslateMethod.Value switch
            {
                TranslateMethod.GoogleTranslate => GoogleWebDriverTranslator.Run(token),
                TranslateMethod.DeepSeek => DeepSeekWebDriverTranslator.Run(token, false),
                TranslateMethod.DeepSeekWithDeepThinking => DeepSeekWebDriverTranslator.Run(token, true),
                TranslateMethod.AIApi => AIApiTranslator.Run(token),
                _ => Task.CompletedTask,
            };
        }

        private void SetUILock(bool isLocked)
        {
            TabControl.IsEnabled = !isLocked;
            BtnStart.IsEnabled = !isLocked;
        }

        private void BtnSimplified2TraditionalClick(object sender, RoutedEventArgs e)
        {
            Simplified2TraditionalTranslator.Translate();
        }

        private void BtnCombineClick(object sender, RoutedEventArgs e)
        {
            var list = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value);
            if (list.Count == 0)
            {
                LogUtils.LogError("未找到任何语言对");
                return;
            }

            foreach (var pair in list)
            {
                if (!TaskUtils.CombineTask(pair))
                {
                    LogUtils.LogError($"合并{pair.Name}失败");
                }
            }
        }

        #endregion

        #region 设置项变更

        private void WebDriveComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, WebDriveComboBox)) return;
            if (WebDriveComboBox.SelectedItem is not BrowserChoice choice) return;

            ConfigUtils.WebDriver.Value = choice.Type;
            WebDriveComboBox.ToolTip = ToolTipUtils.WebDriverToolTip;
        }

        private void DictConverterComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, DictConverterComboBox)) return;
            if (DictConverterComboBox.SelectedIndex == -1) return;

            ConfigUtils.DictConverter.Value =
                DictConverterUtils.GetConverterType(((DictConverterType)DictConverterComboBox.SelectedItem).type);
            DictConverterComboBox.ToolTip = ToolTipUtils.DictConverterToolTip;
        }

        private void IsoPairComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, IsoPairComboBox)) return;
            if (IsoPairComboBox.SelectedIndex == -1) return;

            ConfigUtils.IsoPairIndex.Value = IsoPairComboBox.SelectedIndex;
            var pairs = PairUtils.GetPairs(IsoPairComboBox.SelectedIndex);

            SourceLangComboBox.ItemsSource = pairs;
            SourceLangComboBox.SelectedIndex = 0;
            TargetLangComboBox.ItemsSource = pairs;
            TargetLangComboBox.SelectedIndex = 0;
            IsoPairComboBox.ToolTip = ToolTipUtils.IsoPairToolTip;
        }

        private void SourceLangComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, SourceLangComboBox)) return;
            if (SourceLangComboBox.SelectedIndex == -1) return;

            ConfigUtils.SourceLangIndex.Value = SourceLangComboBox.SelectedIndex;
            SourceLangComboBox.ToolTip = ToolTipUtils.SourceLangToolTip;
        }

        private void TargetLangComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, TargetLangComboBox)) return;
            if (TargetLangComboBox.SelectedIndex == -1) return;

            ConfigUtils.TargetLangIndex.Value = TargetLangComboBox.SelectedIndex;
            TargetLangComboBox.ToolTip = ToolTipUtils.TargetLangToolTip;
        }

        private void TranslateMethodComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, TranslateMethodComboBox)) return;
            if (TranslateMethodComboBox.SelectedIndex == -1) return;

            ConfigUtils.TranslateMethod.Value = (TranslateMethod)TranslateMethodComboBox.SelectedIndex;
        }

        private void TargetLangAmountComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, TargetLangAmountComboBox)) return;
            if (TargetLangAmountComboBox.SelectedIndex == -1) return;

            ConfigUtils.TargetLangAmount.Value = (TargetLangAmount)TargetLangAmountComboBox.SelectedIndex;
        }

        private void TextBoxApiEndpointTextChanged(object sender, TextChangedEventArgs e)
        {
            ConfigUtils.ApiEndpoint.Value = TextBoxApiEndpoint.Text;
        }

        private void TextBoxApiKeyTextChanged(object sender, TextChangedEventArgs e)
        {
            ConfigUtils.ApiKey.Value = TextBoxApiKey.Text;
        }

        private void TextBoxModelTextChanged(object sender, TextChangedEventArgs e)
        {
            ConfigUtils.AIModel.Value = TextBoxModel.Text;
        }

        private void TextBoxPromptTextChanged(object sender, TextChangedEventArgs e)
        {
            ConfigUtils.AIPrompt.Value = TextBoxPrompt.Text;
        }

        private void TextBoxDriverProxyTextChanged(object sender, TextChangedEventArgs e)
        {
            ConfigUtils.DriverProxy.Value = TextBoxDriverProxy.Text;
        }

        private void ResetPromptClick(object sender, RoutedEventArgs e)
        {
            TextBoxPrompt.Text = ConfigUtils.DefaultAIPrompt;
        }

        #endregion

        #region 编辑 / 新增

        private void TabControlSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, TabControl)) return;

            switch (TabControl.SelectedIndex)
            {
                case 1:
                    EditorUtils.Init(DataGridEditor, ComboBoxEditorTargetLanguage);
                    break;
                case 2:
                    AddNewUtils.Init(DataGridAddNew);
                    break;
            }
        }

        private void ComboBoxEditorTargetLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, ComboBoxEditorTargetLanguage)) return;
            if (ComboBoxEditorTargetLanguage.SelectedIndex == -1) return;

            EditorUtils.UpdateGrid((Pair)ComboBoxEditorTargetLanguage.SelectedItem);
        }

        private void BtnEditorSaveClick(object sender, RoutedEventArgs e)
        {
            EditorUtils.Save();
        }

        private void BtnEditorDeleteKeyClick(object sender, RoutedEventArgs e)
        {
            EditorUtils.DeleteKeys();
        }

        private void BtnAddNewSaveClick(object sender, RoutedEventArgs e)
        {
            AddNewUtils.Save();
        }

        private void BtnAddNewLoadClick(object sender, RoutedEventArgs e)
        {
            AddNewUtils.Load();
        }

        private void BtnPasteRowClick(object sender, RoutedEventArgs e)
        {
            AddNewUtils.PasteClipboardData();
        }

        #endregion

        #region 打开文件夹

        private void BtnOpenInputClick(object sender, RoutedEventArgs e) => PathUtils.OpenInputFolder();

        private void BtnOpenOutputClick(object sender, RoutedEventArgs e) => PathUtils.OpenOutputFolder();

        private void BtnOpenIsoClick(object sender, RoutedEventArgs e) => PathUtils.OpenIsoFolder();

        private void BtnDictConverterClick(object sender, RoutedEventArgs e) => PathUtils.OpenDictConverterFolder();

        private void BtnOpenCombineSourceFolderClick(object sender, RoutedEventArgs e) =>
            PathUtils.OpenCombineSourceFolder();

        private void BtnOpenCombineOutputFolderClick(object sender, RoutedEventArgs e) =>
            PathUtils.OpenCombineOutputFolder();

        private void BtnOpenEditorFolderClick(object sender, RoutedEventArgs e) => PathUtils.OpenEditorFolder();

        private void BtnOpenAddNewFolderClick(object sender, RoutedEventArgs e) => PathUtils.OpenNewFolder();

        #endregion
    }
}
