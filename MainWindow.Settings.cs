using LocalizationTranslationWPF.DictConverter;
using System;
using System.Windows;
using System.Windows.Controls;
using LocalizationTranslationWPF.Utils;
using LocalizationTranslationWPF.Projects;
using static LocalizationTranslationWPF.Data.Config;

namespace LocalizationTranslationWPF
{
    public partial class MainWindow
    {
        private bool isInitializingApiKey;

        private void ThemeModeComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitializingTheme || !Equals(e.Source, ThemeModeComboBox) ||
                ThemeModeComboBox.SelectedItem is not ThemeChoice choice) return;
            ConfigUtils.AppTheme.Value = choice.Mode;
            if (IsLoaded) ThemeUtils.Attach(this, choice.Mode);
            else ThemeUtils.Apply(choice.Mode);
        }

        private void WebDriveComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, WebDriveComboBox)) return;
            if (WebDriveComboBox.SelectedItem is not BrowserChoice choice) return;

            ConfigUtils.WebDriver.Value = choice.Type;
            WebDriveComboBox.ToolTip = ToolTipUtils.WebDriverToolTip;
        }

        private void DictConverterComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isSwitchingProject || ProjectContext.Current != null) return;
            if (!Equals(e.Source, DictConverterComboBox)) return;
            if (DictConverterComboBox.SelectedIndex == -1) return;

            ConfigUtils.DictConverter.Value =
                DictConverterUtils.GetConverterType(((DictConverterType)DictConverterComboBox.SelectedItem).type);
            DictConverterComboBox.ToolTip = ToolTipUtils.DictConverterToolTip;
        }

        private void IsoPairComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isSwitchingProject || ProjectContext.Current != null) return;
            if (!Equals(e.Source, IsoPairComboBox)) return;
            if (IsoPairComboBox.SelectedIndex == -1) return;

            ConfigUtils.IsoPairIndex.Value = IsoPairComboBox.SelectedIndex;
            IsoPairComboBox.ToolTip = ToolTipUtils.IsoPairToolTip;
        }

        private void SourceLangComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, SourceLangComboBox)) return;
            if (SourceLangComboBox.SelectedIndex == -1) return;

            ConfigUtils.SourceLangIndex.Value = SourceLangComboBox.SelectedIndex;
            SourceLangComboBox.ToolTip = ToolTipUtils.SourceLangToolTip;
            UpdateStartButtonState();
        }

        private void TargetLangComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, TargetLangComboBox)) return;
            if (TargetLangComboBox.SelectedIndex == -1) return;

            ConfigUtils.TargetLangIndex.Value = TargetLangComboBox.SelectedIndex;
            TargetLangComboBox.ToolTip = ToolTipUtils.TargetLangToolTip;
            UpdateStartButtonState();
        }

        private void TranslateMethodComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, TranslateMethodComboBox)) return;
            if (TranslateMethodComboBox.SelectedIndex == -1) return;

            ConfigUtils.TranslateMethod.Value = (TranslateMethod)TranslateMethodComboBox.SelectedIndex;
            UpdateStartButtonState();
        }

        private void TargetLangAmountComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Equals(e.Source, TargetLangAmountComboBox)) return;
            if (TargetLangAmountComboBox.SelectedIndex == -1) return;

            if (TargetLangAmountComboBox.SelectedItem is TranslationScopeChoice choice)
                ConfigUtils.TargetLangAmount.Value = choice.Amount;
            UpdateStartButtonState();
        }

        private void TextBoxApiEndpointTextChanged(object sender, TextChangedEventArgs e)
        {
            ConfigUtils.ApiEndpoint.Value = TextBoxApiEndpoint.Text;
            UpdateStartButtonState();
        }

        private void PasswordBoxApiKeyPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (isInitializingApiKey) return;

            ConfigUtils.ApiKey.Value = PasswordBoxApiKey.Password;
            UpdateStartButtonState();
        }

        private void TextBoxModelTextChanged(object sender, TextChangedEventArgs e)
        {
            ConfigUtils.AIModel.Value = TextBoxModel.Text;
            UpdateStartButtonState();
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
    }
}
