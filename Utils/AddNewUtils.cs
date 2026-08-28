using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
using System;
using System.Collections.Generic;
using System.Data;
using System.Windows.Controls;

namespace LocalizationTranslationWPF.Utils
{
    public static class AddNewUtils
    {
        private const string KeyColumn = "Key";
        private const string ValueColumn = "Value";

        private static readonly DataTable DataTable = new();

        static AddNewUtils()
        {
            DataTable.Columns.Add(new DataColumn
            {
                ColumnName = KeyColumn,
                DataType = typeof(string),
            });
            DataTable.Columns.Add(new DataColumn
            {
                ColumnName = ValueColumn,
                DataType = typeof(string),
            });
        }

        public static void Init(DataGrid dataGridAddNew)
        {
            dataGridAddNew.ItemsSource = DataTable.DefaultView;
        }

        public static void Save()
        {
            if (!TryGetSourcePair(out var pair))
            {
                return;
            }

            var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
            if (dictConverter == null)
            {
                LogUtils.LogError("无法找到转换器");
                return;
            }

            var dict = new Dictionary<string, string>();
            for (var i = 0; i < DataTable.Rows.Count; i++)
            {
                var row = DataTable.Rows[i];
                var key = row[KeyColumn].ToString();
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (dict.ContainsKey(key))
                {
                    LogUtils.LogError($"第{i + 1}行的key: {key} 重复了");
                    return;
                }
                dict[key] = row[ValueColumn].ToString() ?? "";
            }

            var path = PathUtils.NewFile(pair.Name);
            IOUtils.OutputJson(path, dict, dictConverter);
            LogUtils.Log($"保存完成，文件路径：{path}");
        }

        internal static void Load()
        {
            if (!TryGetSourcePair(out var pair))
            {
                return;
            }

            var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
            if (dictConverter == null)
            {
                LogUtils.LogError("无法找到转换器");
                return;
            }

            var path = PathUtils.NewFile(pair.Name);
            DataTable.Clear();
            if (!IOUtils.TryInputJson(path, dictConverter, out var dict) || dict == null)
            {
                LogUtils.LogError($"读取{path}失败");
                return;
            }

            foreach (var item in dict)
            {
                var row = DataTable.NewRow();
                row[KeyColumn] = item.Key;
                row[ValueColumn] = item.Value;
                DataTable.Rows.Add(row);
            }
        }

        /// <summary>
        /// 把剪贴板里的制表符分隔文本按行追加到表格
        /// </summary>
        internal static void PasteClipboardData()
        {
            var clipboardText = ThreadUtils.GetClipboard();
            if (string.IsNullOrEmpty(clipboardText))
            {
                return;
            }

            //适应 Windows 与 Unix 行结束符
            var rows = clipboardText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var row in rows)
            {
                //每行按制表符分割
                var cells = row.Split('\t');
                var newRow = DataTable.NewRow();
                for (var i = 0; i < cells.Length && i < DataTable.Columns.Count; i++)
                {
                    newRow[i] = cells[i];
                }
                DataTable.Rows.Add(newRow);
            }
        }

        /// <summary>
        /// 新增页固定使用当前ISO表里的第一种语言
        /// </summary>
        private static bool TryGetSourcePair(out Pair pair)
        {
            var pairs = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value);
            if (pairs.Count == 0)
            {
                pair = default;
                LogUtils.LogError("未找到任何语言对");
                return false;
            }

            pair = pairs[0];
            return true;
        }
    }
}
