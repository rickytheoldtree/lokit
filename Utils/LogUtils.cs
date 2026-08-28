using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LocalizationTranslationWPF.Utils
{
    public static class LogUtils
    {
        public static void Log(string text) => Append(text, Brushes.Green);

        public static void LogError(string text) => Append(text, Brushes.Red);

        /// <summary>
        /// 追加一行带颜色的日志，可从任意线程调用
        /// </summary>
        private static void Append(string text, Brush color)
        {
            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            app.Dispatcher.Invoke(() =>
            {
                if (app.MainWindow?.FindName("LogBox") is not RichTextBox logBox)
                {
                    return;
                }

                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run($"[{GetTime()}] {text}") { Foreground = color });
                logBox.Document.Blocks.Add(paragraph);
                //滚动到最底部
                logBox.ScrollToEnd();
            });
        }

        private static string GetTime()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
