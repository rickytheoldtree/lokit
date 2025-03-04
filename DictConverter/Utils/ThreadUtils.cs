using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LocalizationTranslationWPF.Utils
{
    public static class ThreadUtils
    {
        /// <summary>
        /// 复制文本到剪切板
        /// </summary>
        public static string GetClipboard()
        {
            //用STA方式启动线程 并获取剪切板数据
            string result = "";
            Thread thread = new Thread(() =>
            {
                try
                {
                    result = System.Windows.Clipboard.GetText();
                }
                catch (Exception)
                {
                    result = "";
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }
    }
}
