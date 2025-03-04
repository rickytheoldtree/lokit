using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Translate;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LocalizationTranslationWPF.Data;
using System.ComponentModel;
using System.Threading.Tasks;
using LocalizationTranslationWPF.Utils;
using LocalizationTranslationWPF.Editor;

namespace LocalizationTranslationWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Init();
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            GoogleTranslator.Close();
        }
        private void Init()
        {
            PathUtils.MakeSureFolderExist();
            webDriveComboBox.ItemsSource = Enum.GetValues(typeof(Config.WebDriverType)).Cast<Config.WebDriverType>();
            webDriveComboBox.SelectedIndex = (int)ConfigUtils.WebDriver.Value;
            ToolTipService.SetInitialShowDelay(webDriveComboBox, 0);

            dictConverterComboBox.ItemsSource = DictConverterUtils.Converters;
            dictConverterComboBox.SelectedItem = new DictConverterType(ConfigUtils.DictConverter.Value);
            ToolTipService.SetInitialShowDelay(dictConverterComboBox, 0);

            isoPairComboBox.ItemsSource = PairUtils.PairsCache;
            isoPairComboBox.SelectedIndex = ConfigUtils.IsoPairIndex.Value > PairUtils.PairsCache.Count - 1 ? 0 : ConfigUtils.IsoPairIndex.Value;
            ToolTipService.SetInitialShowDelay(isoPairComboBox, 0);

            sourceLangComboBox.ItemsSource = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value);
            sourceLangComboBox.SelectedIndex = ConfigUtils.SourceLangIndex.Value;
            ToolTipService.SetInitialShowDelay(sourceLangComboBox, 0);

            targetLangComboBox.ItemsSource = PairUtils.GetPairsWithAll(ConfigUtils.IsoPairIndex.Value);
            targetLangComboBox.SelectedIndex = ConfigUtils.TargetLangIndex.Value;
            ToolTipService.SetInitialShowDelay(targetLangComboBox, 0);
        }
        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            LockUI();
            await MainTask();
            UnlockUI();
        }
        private async Task MainTask()
        {
            await Task.Run( async () =>
            {
                var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
                if(dictConverter == null)
                {
                    LogUtils.LogError("字典转换器类型错误");
                    return;
                }
                var sourceLang = ConfigUtils.SourceLang;
                var targetLang = ConfigUtils.TargetLang;
                if (targetLang.Name == "All")
                {
                    foreach (var pair in PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value))
                    {
                        targetLang = pair;
                        if (!await TaskUtils.TranslateTask(sourceLang, targetLang, dictConverter))
                        {
                            LogUtils.LogError($"翻译{targetLang.Name}失败");
                            continue;
                        }
                    }
                }
                else
                {
                    await TaskUtils.TranslateTask(sourceLang, targetLang, dictConverter);
                }
                GoogleTranslator.Close();
            });
        } 
        private void webDriveComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != webDriveComboBox) return;
            if (webDriveComboBox.SelectedIndex == -1) return;

            ConfigUtils.WebDriver.Value = (Config.WebDriverType)webDriveComboBox.SelectedIndex;
            webDriveComboBox.ToolTip = ToolTipUtils.WebDriverToolTip;
        }

        private void dictConverterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != dictConverterComboBox) return;
            if (dictConverterComboBox.SelectedIndex == -1) return;

            ConfigUtils.DictConverter.Value = DictConverterUtils.GetConverterType(((DictConverterType)dictConverterComboBox.SelectedItem).type);
            dictConverterComboBox.ToolTip = ToolTipUtils.DictConverterToolTip;
        }

        private void isoPairComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != isoPairComboBox) return;
            if (isoPairComboBox.SelectedIndex == -1) return;

            ConfigUtils.IsoPairIndex.Value = isoPairComboBox.SelectedIndex;
            sourceLangComboBox.ItemsSource = PairUtils.GetPairs(isoPairComboBox.SelectedIndex);
            sourceLangComboBox.SelectedIndex = 0;
            targetLangComboBox.ItemsSource = PairUtils.GetPairsWithAll(isoPairComboBox.SelectedIndex);
            targetLangComboBox.SelectedIndex = 0;
            isoPairComboBox.ToolTip = ToolTipUtils.IsoPairToolTip;
        }

        private void sourceLangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != sourceLangComboBox) return;
            if (sourceLangComboBox.SelectedIndex == -1) return;
            
            ConfigUtils.SourceLangIndex.Value = sourceLangComboBox.SelectedIndex;
            sourceLangComboBox.ToolTip = ToolTipUtils.SourceLangToolTip;
        }

        private void targetLangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != targetLangComboBox) return;
            if (targetLangComboBox.SelectedIndex == -1) return;

            ConfigUtils.TargetLangIndex.Value = targetLangComboBox.SelectedIndex;
            targetLangComboBox.ToolTip = ToolTipUtils.TargetLangToolTip;
        }

        private void btnOpenInput_Click(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenInputFolder();
        }

        private void btnOpenOutput_Click(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenOutputFolder();
        }

        private void btnOpenISO_Click(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenIsoFolder();
        }
        private void LockUI()
        {
            btnStart.IsEnabled = false;
            webDriveComboBox.IsEnabled = false;
            dictConverterComboBox.IsEnabled = false;
            isoPairComboBox.IsEnabled = false;
            sourceLangComboBox.IsEnabled = false;
            targetLangComboBox.IsEnabled = false;
            tabControl.IsEnabled = false;
        }
        private void UnlockUI()
        {
            btnStart.IsEnabled = true;
            webDriveComboBox.IsEnabled = true;
            dictConverterComboBox.IsEnabled = true;
            isoPairComboBox.IsEnabled = true;
            sourceLangComboBox.IsEnabled = true;
            targetLangComboBox.IsEnabled = true;
            tabControl.IsEnabled = true;
        }

        private void btnDictConverter_Click(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenDictConverterFolder();
        }

        private void btnSimplified2Traditional_Click(object sender, RoutedEventArgs e)
        {
            Simplified2TraditionalTranslator.Translate();
        }

        private void btnOpenDriversFolder_Click(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenDriversFolder();
        }

        private void btnCombine_Click(object sender, RoutedEventArgs e)
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
                    continue;
                }
            }
        }

        private void btnOpenCombineSourceFolder_Click(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenCombineSourceFolder();
        }

        private void btnOpenCombineOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenCombineOutputFolder();
        }

        private void comboBoxEditorTargetLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != comboBoxEditorTargetLanguage) return;
            if (comboBoxEditorTargetLanguage.SelectedIndex == -1) return;
            EditorUtils.UpdateGrid((Pair)comboBoxEditorTargetLanguage.SelectedItem);
        }

        private void tabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(e.Source != tabControl) return;
            if (tabControl.SelectedIndex != 1) return;
            var pairs = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value);
            EditorUtils.Init(dataGrid, pairs);
            comboBoxEditorTargetLanguage.ItemsSource = pairs;
            comboBoxEditorTargetLanguage.SelectedIndex = 0;
        }

        private void btnEditorSave_Click(object sender, RoutedEventArgs e)
        {
            EditorUtils.Save();
        }

        private void btnEditorDeleteKey_Click(object sender, RoutedEventArgs e)
        {
            EditorUtils.DeleteKeys();
        }

        private void btnOpenEditorFolder_Click(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenEditorFolder();
        }
    }
}
