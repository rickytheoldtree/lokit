using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Translate;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LocalizationTranslationWPF.Data;
using System.ComponentModel;
using LocalizationTranslationWPF.Utils;
using static LocalizationTranslationWPF.Data.Config;

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
            webDriveComboBox.ItemsSource = Enum.GetValues(typeof(WebDriverType)).Cast<WebDriverType>();
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

            translateMethodComboBox.ItemsSource = Enum.GetValues(typeof(TranslateMethod)).Cast<TranslateMethod>();
            translateMethodComboBox.SelectedIndex = (int)ConfigUtils.TranslateMethod.Value;

            textBoxApiEndpoint.Text = ConfigUtils.ApiEndpoint.Value;
            textBoxApiKey.Text = ConfigUtils.ApiKey.Value;
            textBoxModel.Text = ConfigUtils.AIModel.Value;
        }
        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            switch (ConfigUtils.TranslateMethod.Value)
            {
                case TranslateMethod.GoogleTranslate:
                    await GoogleTranslateTask.Run(this);
                    break;
                case TranslateMethod.AI:
                    await AITranslateTask.Run(this);
                    break;
            }
        }

        private void webDriveComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != webDriveComboBox) return;
            if (webDriveComboBox.SelectedIndex == -1) return;

            ConfigUtils.WebDriver.Value = (WebDriverType)webDriveComboBox.SelectedIndex;
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
        public void SetUILock(bool isLocked)
        {
            btnStart.IsEnabled = !isLocked;
            webDriveComboBox.IsEnabled = !isLocked;
            dictConverterComboBox.IsEnabled = !isLocked;
            isoPairComboBox.IsEnabled = !isLocked;
            sourceLangComboBox.IsEnabled = !isLocked;
            targetLangComboBox.IsEnabled = !isLocked;
            tabControl.IsEnabled = !isLocked;
            translateMethodComboBox.IsEnabled = !isLocked;
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
            switch (tabControl.SelectedIndex)
            {
                default:
                    break;
                case 1:
                    EditorUtils.Init(dataGridEditor, comboBoxEditorTargetLanguage);
                    break;
                case 2:
                    AddNewUtils.Init(dataGridAddNew);
                    break;
            }

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

        private void btnAddNewSave_Click(object sender, RoutedEventArgs e)
        {
            AddNewUtils.Save();
        }

        private void btnOpenAddNewFolder_Click(object sender, RoutedEventArgs e)
        {
            PathUtils.OpenNewFolder();
        }

        private void btnAddNewLoad_Click(object sender, RoutedEventArgs e)
        {
            AddNewUtils.Load();
        }

        private void btnPasteRow_Click(object sender, RoutedEventArgs e)
        {
            AddNewUtils.PasteClipboardData(dataGridAddNew);
        }

        private void translateMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != translateMethodComboBox) return;
            if (translateMethodComboBox.SelectedIndex == -1) return;

            ConfigUtils.TranslateMethod.Value = (TranslateMethod)translateMethodComboBox.SelectedIndex;
        }

        private void textBoxApiEndpoint_TextChanged(object sender, TextChangedEventArgs e)
        {
            ConfigUtils.ApiEndpoint.Value = textBoxApiEndpoint.Text;
        }

        private void textBoxApiKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            ConfigUtils.ApiKey.Value = textBoxApiKey.Text;
        }

        private void textBoxModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            ConfigUtils.AIModel.Value = textBoxModel.Text;
        }
    }
}
