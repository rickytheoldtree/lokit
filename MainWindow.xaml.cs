using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Translate;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LocalizationTranslationWPF.Data;
using System.ComponentModel;
using System.Threading.Tasks;
using LocalizationTranslationWPF.Helper;

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
            PathHelper.MakeSureFolderExist();
            webDriveComboBox.ItemsSource = Enum.GetValues(typeof(Config.WebDriverType)).Cast<Config.WebDriverType>();
            webDriveComboBox.SelectedIndex = (int)ConfigHelper.WebDriver.Value;
            ToolTipService.SetInitialShowDelay(webDriveComboBox, 0);

            dictConverterComboBox.ItemsSource = DictConverterHelper.Converters;
            dictConverterComboBox.SelectedItem = new DictConverterType(ConfigHelper.DictConverter.Value);
            ToolTipService.SetInitialShowDelay(dictConverterComboBox, 0);

            isoPairComboBox.ItemsSource = PairHelper.PairsCache;
            isoPairComboBox.SelectedIndex = ConfigHelper.IsoPairIndex.Value > PairHelper.PairsCache.Count - 1 ? 0 : ConfigHelper.IsoPairIndex.Value;
            ToolTipService.SetInitialShowDelay(isoPairComboBox, 0);

            sourceLangComboBox.ItemsSource = PairHelper.GetPairs(ConfigHelper.IsoPairIndex.Value);
            sourceLangComboBox.SelectedIndex = ConfigHelper.SourceLangIndex.Value;
            ToolTipService.SetInitialShowDelay(sourceLangComboBox, 0);

            targetLangComboBox.ItemsSource = PairHelper.GetPairsWithAll(ConfigHelper.IsoPairIndex.Value);
            targetLangComboBox.SelectedIndex = ConfigHelper.TargetLangIndex.Value;
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
                var dictConverter = DictConverterHelper.GetConverterInstance(ConfigHelper.DictConverter.Value);
                if(dictConverter == null)
                {
                    LogHelper.LogError("字典转换器类型错误");
                    return;
                }
                var sourceLang = ConfigHelper.SourceLang;
                var targetLang = ConfigHelper.TargetLang;
                if (targetLang.Name == "All")
                {
                    foreach (var pair in PairHelper.GetPairs(ConfigHelper.IsoPairIndex.Value))
                    {
                        targetLang = pair;
                        if (!await TaskHelper.TranslateTask(sourceLang, targetLang, dictConverter))
                        {
                            LogHelper.LogError($"翻译{targetLang.Name}失败");
                            continue;
                        }
                    }
                }
                else
                {
                    await TaskHelper.TranslateTask(sourceLang, targetLang, dictConverter);
                }
                GoogleTranslator.Close();
            });
        } 
        private void webDriveComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ConfigHelper.WebDriver.Value = (Config.WebDriverType)webDriveComboBox.SelectedIndex;
            webDriveComboBox.ToolTip = ToolTipHelper.WebDriverToolTip;
        }

        private void dictConverterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ConfigHelper.DictConverter.Value = DictConverterHelper.GetConverterType(((DictConverterType)dictConverterComboBox.SelectedItem).type);
            dictConverterComboBox.ToolTip = ToolTipHelper.DictConverterToolTip;
        }

        private void isoPairComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ConfigHelper.IsoPairIndex.Value = isoPairComboBox.SelectedIndex;
            sourceLangComboBox.ItemsSource = PairHelper.GetPairs(isoPairComboBox.SelectedIndex);
            sourceLangComboBox.SelectedIndex = 0;
            targetLangComboBox.ItemsSource = PairHelper.GetPairsWithAll(isoPairComboBox.SelectedIndex);
            targetLangComboBox.SelectedIndex = 0;
            isoPairComboBox.ToolTip = ToolTipHelper.IsoPairToolTip;
        }

        private void sourceLangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(sourceLangComboBox.SelectedIndex == -1)
            {
                return;
            }
            ConfigHelper.SourceLangIndex.Value = sourceLangComboBox.SelectedIndex;
            sourceLangComboBox.ToolTip = ToolTipHelper.SourceLangToolTip;
        }

        private void targetLangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (targetLangComboBox.SelectedIndex == -1)
            {
                return;
            }
            ConfigHelper.TargetLangIndex.Value = targetLangComboBox.SelectedIndex;
            targetLangComboBox.ToolTip = ToolTipHelper.TargetLangToolTip;
        }

        private void btnOpenInput_Click(object sender, RoutedEventArgs e)
        {
            PathHelper.OpenInputFolder();
        }

        private void btnOpenOutput_Click(object sender, RoutedEventArgs e)
        {
            PathHelper.OpenOutputFolder();
        }

        private void btnOpenISO_Click(object sender, RoutedEventArgs e)
        {
            PathHelper.OpenIsoFolder();
        }
        private void LockUI()
        {
            btnStart.IsEnabled = false;
            webDriveComboBox.IsEnabled = false;
            dictConverterComboBox.IsEnabled = false;
            isoPairComboBox.IsEnabled = false;
            sourceLangComboBox.IsEnabled = false;
            targetLangComboBox.IsEnabled = false;
        }
        private void UnlockUI()
        {
            btnStart.IsEnabled = true;
            webDriveComboBox.IsEnabled = true;
            dictConverterComboBox.IsEnabled = true;
            isoPairComboBox.IsEnabled = true;
            sourceLangComboBox.IsEnabled = true;
            targetLangComboBox.IsEnabled = true;
        }

        private void btnDictConverter_Click(object sender, RoutedEventArgs e)
        {
            PathHelper.OpenDictConverterFolder();
        }

        private void btnSimplified2Traditional_Click(object sender, RoutedEventArgs e)
        {
            Simplified2TraditionalTranslator.Translate();
        }

        private void btnOpenDriversFolder_Click(object sender, RoutedEventArgs e)
        {
            PathHelper.OpenDriversFolder();
        }

        private void btnCombine_Click(object sender, RoutedEventArgs e)
        {
            var list = PairHelper.GetPairs(ConfigHelper.IsoPairIndex.Value);
            if (list.Count == 0)
            {
                LogHelper.LogError("未找到任何语言对");
                return;
            }
            foreach (var pair in list)
            {
                if (!TaskHelper.CombineTask(pair))
                {
                    LogHelper.LogError($"合并{pair.Name}失败");
                    continue;
                }
            }
        }

        private void btnOpenCombineSourceFolder_Click(object sender, RoutedEventArgs e)
        {
            PathHelper.OpenCombineSourceFolder();
        }

        private void btnOpenCombineOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            PathHelper.OpenCombineOutputFolder();
        }
    }
}
