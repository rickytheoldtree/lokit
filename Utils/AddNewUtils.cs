using LocalizationTranslationWPF.DictConverter;
using System;
using System.Collections.Generic;
using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace LocalizationTranslationWPF.Utils
{
    public static class AddNewUtils
    {
        static AddNewUtils()
        {
            dataTable.Columns.Add(new DataColumn()
            {
                ColumnName = "Key",
                DataType = typeof(string),
            });
            dataTable.Columns.Add(new DataColumn()
            {
                ColumnName = "Value",
                DataType = typeof(string),
            });
        }
        private static DataTable dataTable = new();
        public static void Init(DataGrid dataGridAddNew)
        {
            dataGridAddNew.ItemsSource = dataTable.DefaultView;
        }

        public static void Save()
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < dataTable.Rows.Count; i++)
            {
                var row = dataTable.Rows[i];
                var key = row["Key"].ToString();
                var value = row["Value"].ToString();
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }
                if (dict.ContainsKey(key))
                {
                    LogUtils.LogError($"第{i + 1}行的key: {key} 重复了");
                    return;
                }
                dict[key] = value ?? "";
            }
            var pair = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value)[0];

            var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
            if (dictConverter == null)
            {
                LogUtils.LogError("无法找到转换器");
                return;
            }
            IOUtils.OutputJson($"{PathUtils.NewPath}\\{pair.Name}.json", dict, dictConverter);
        }

        internal static void Load()
        {
            var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
            if (dictConverter == null)
            {
                LogUtils.LogError("无法找到转换器");
                return;
            }
            var pair = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value)[0];
            dataTable.Clear();
            if (IOUtils.TryInputJson($"{PathUtils.NewPath}\\{pair.Name}.json", dictConverter, out var dict))
            {
                if(dict == null)
                {
                    return;
                }
                foreach (var item in dict)
                {
                    var row = dataTable.NewRow();
                    row["Key"] = item.Key;
                    row["Value"] = item.Value;
                    dataTable.Rows.Add(row);
                }
            }
        }

        internal static void PasteClipboardData(DataGrid dataGridAddNew)
        {
            // 从剪贴板中获取文本数据
            string clipboardText = Clipboard.GetText();
            if (!string.IsNullOrEmpty(clipboardText))
            {
                // 将剪贴板内容按行分割（适应 Windows 与 Unix 行结束符）
                string[] rows = clipboardText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string row in rows)
                {
                    // 将每行文本按制表符分割，假设数据为制表符分隔格式
                    string[] cells = row.Split('\t');

                    // 创建 DataTable 的新行，假设 myDataTable 为你绑定到 DataGrid 的 DataTable 对象
                    DataRow newRow = dataTable.NewRow();

                    // 遍历每个单元格数据并赋值到新行中
                    for (int i = 0; i < cells.Length && i < dataTable.Columns.Count; i++)
                    {
                        newRow[i] = cells[i];
                    }

                    // 将新行添加到 DataTable 中
                    dataTable.Rows.Add(newRow);
                }
            }
        }
    }
}
