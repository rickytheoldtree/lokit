using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Translate;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LocalizationTranslationWPF.Data;
using System.Threading;
using LocalizationTranslationWPF.Utils;
using static LocalizationTranslationWPF.Data.Config;

namespace LocalizationTranslationWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            Init();
        }
        private void Init()
        {
            PathUtils.MakeSureFolderExist();
            WebDriveComboBox.ItemsSource = Enum.GetValues(typeof(WebDriverType)).Cast<WebDriverType>();
            WebDriveComboBox.SelectedIndex = (int)ConfigUtils.WebDriver.Value;
            ToolTipService.SetInitialShowDelay(WebDriveComboBox, 0);

            DictConverterComboBox.ItemsSource = DictConverterUtils.Converters;
            DictConverterComboBox.SelectedItem = new DictConverterType(ConfigUtils.DictConverter.Value);
            ToolTipService.SetInitialShowDelay(DictConverterComboBox, 0);

            IsoPairComboBox.ItemsSource = PairUtils.PairsCache;
            IsoPairComboBox.SelectedIndex = ConfigUtils.IsoPairIndex.Value > PairUtils.PairsCache.Count - 1 ? 0 : ConfigUtils.IsoPairIndex.Value;
            ToolTipService.SetInitialShowDelay(IsoPairComboBox, 0);

            SourceLangComboBox.ItemsSource = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value);
            SourceLangComboBox.SelectedIndex = ConfigUtils.SourceLangIndex.Value;
            ToolTipService.SetInitialShowDelay(SourceLangComboBox, 0);

            TargetLangComboBox.ItemsSource = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value);
            TargetLangComboBox.SelectedIndex = ConfigUtils.TargetLangIndex.Value;
            ToolTipService.SetInitialShowDelay(TargetLangComboBox, 0);

            TranslateMethodComboBox.ItemsSource = Enum.GetValues(typeof(TranslateMethod)).Cast<TranslateMethod>();
            TranslateMethodComboBox.SelectedIndex = (int)ConfigUtils.TranslateMethod.Value;
            
            TargetLangAmountComboBox.ItemsSource = Enum.GetValues(typeof(TargetLangAmount)).Cast<TargetLangAmount>();
            TargetLangAmountComboBox.SelectedIndex = (int)ConfigUtils.TargetLangAmount.Value;

            TextBoxApiEndpoint.Text = ConfigUtils.ApiEndpoint.Value;
            TextBoxApiKey.Text = ConfigUtils.ApiKey.Value;
            TextBoxModel.Text = ConfigUtils.AIModel.Value;
            TextBoxPrompt.Text = ConfigUtils.AIPrompt.Value;
        }

        private CancellationTokenSource? cts;
        private PopWindow? popWindow;
        private async void BtnStartClick(object sender, RoutedEventArgs e)
        {
            try
            {
                cts = new CancellationTokenSource();
                SetUILock(true);

                popWindow = new PopWindow("任务正在运行，请稍候...","取消任务", window =>
                {
                    cts?.Cancel();
                    window.TextBlock.Text = "正在取消任务...";
                    window.Btn.IsEnabled = false;
                    window.Btn.Visibility = Visibility.Visible;
                })
                {
                    Owner = this
                };
                popWindow.Show();

                switch (ConfigUtils.TranslateMethod.Value)
                {
                    case TranslateMethod.GoogleTranslate:
                        await GoogleWebDriverTranslator.Run(cts.Token);
                        break;
                    case TranslateMethod.DeepSeek:
                        await DeepSeekWebDriverTranslator.Run(cts.Token, false);
                        break;
                    case TranslateMethod.DeepSeekWithDeepThinking:
                        await DeepSeekWebDriverTranslator.Run(cts.Token, true);
                        break;
                    case TranslateMethod.AIApi:
                        await AIApiTranslator.Run(cts.Token);
                        break;
                }
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
                cts = null;
                SetUILock(false);
                Focus();
            }
        }


        private void WebDriveComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, WebDriveComboBox)) return;
            if (WebDriveComboBox.SelectedIndex == -1) return;

            ConfigUtils.WebDriver.Value = (WebDriverType)WebDriveComboBox.SelectedIndex;
            WebDriveComboBox.ToolTip = ToolTipUtils.WebDriverToolTip;
        }

        private void DictConverterComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, DictConverterComboBox)) return;
            if (DictConverterComboBox.SelectedIndex == -1) return;

            ConfigUtils.DictConverter.Value = DictConverterUtils.GetConverterType(((DictConverterType)DictConverterComboBox.SelectedItem).type);
            DictConverterComboBox.ToolTip = ToolTipUtils.DictConverterToolTip;
        }

        private void IsoPairComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, IsoPairComboBox)) return;
            if (IsoPairComboBox.SelectedIndex == -1) return;

            ConfigUtils.IsoPairIndex.Value = IsoPairComboBox.SelectedIndex;
            SourceLangComboBox.ItemsSource = PairUtils.GetPairs(IsoPairComboBox.SelectedIndex);
            SourceLangComboBox.SelectedIndex = 0;
            TargetLangComboBox.ItemsSource = PairUtils.GetPairs(IsoPairComboBox.SelectedIndex);
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

        private void BtnOpenInputClick(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenInputFolder();
        }

        private void BtnOpenOutputClick(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenOutputFolder();
        }

        private void BtnOpenIsoClick(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenIsoFolder();
        }

        private void SetUILock(bool isLocked)
        {
            TabControl.IsEnabled = !isLocked;
            BtnStart.IsEnabled = true;
        }

        private void BtnDictConverterClick(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenDictConverterFolder();
        }

        private void BtnSimplified2TraditionalClick(object sender, RoutedEventArgs e)
        {
            Simplified2TraditionalTranslator.Translate();
        }

        private void BtnOpenDriversFolderClick(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenDriversFolder();
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

        private void BtnOpenCombineSourceFolderClick(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenCombineSourceFolder();
        }

        private void BtnOpenCombineOutputFolderClick(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenCombineOutputFolder();
        }

        private void ComboBoxEditorTargetLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, ComboBoxEditorTargetLanguage)) return;
            if (ComboBoxEditorTargetLanguage.SelectedIndex == -1) return;
            EditorUtils.UpdateGrid((Pair)ComboBoxEditorTargetLanguage.SelectedItem);
        }

        private void TabControlSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(!Equals(e.Source, TabControl)) return;
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

        private void BtnEditorSaveClick(object sender, RoutedEventArgs e)
        {
            EditorUtils.Save();
        }

        private void BtnEditorDeleteKeyClick(object sender, RoutedEventArgs e)
        {
            EditorUtils.DeleteKeys();
        }

        private void BtnOpenEditorFolderClick(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenEditorFolder();
        }

        private void BtnAddNewSaveClick(object sender, RoutedEventArgs e)
        {
            AddNewUtils.Save();
        }

        private void BtnOpenAddNewFolderClick(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenNewFolder();
        }

        private void BtnAddNewLoadClick(object sender, RoutedEventArgs e)
        {
            AddNewUtils.Load();
        }

        private void BtnPasteRowClick(object sender, RoutedEventArgs e)
        {
            AddNewUtils.PasteClipboardData(DataGridAddNew);
        }

        private void TranslateMethodComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, TranslateMethodComboBox)) return;
            if (TranslateMethodComboBox.SelectedIndex == -1) return;

            ConfigUtils.TranslateMethod.Value = (TranslateMethod)TranslateMethodComboBox.SelectedIndex;
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

        private void TargetLangAmountComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, TargetLangAmountComboBox)) return;
            if (TargetLangAmountComboBox.SelectedIndex == -1) return;

            ConfigUtils.TargetLangAmount.Value = (TargetLangAmount)TargetLangAmountComboBox.SelectedIndex;
        }
    }
}
