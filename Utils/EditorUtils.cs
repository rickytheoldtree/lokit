using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Windows.Controls;

namespace LocalizationTranslationWPF.Utils
{
    public static class EditorUtils
    {
        private const string KeyColumn = "Key";

        private static DataGrid? dataGrid;
        private static List<Pair>? pairs;

        /// <summary>
        /// 当前正在编辑的语言
        /// </summary>
        private static Pair CurrentPair { get; set; }

        public static void Init(DataGrid dataGridEditor, ComboBox comboBoxEditorTargetLanguage)
        {
            dataGrid = dataGridEditor;
            pairs = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value);

            comboBoxEditorTargetLanguage.ItemsSource = pairs;
            comboBoxEditorTargetLanguage.SelectedIndex = pairs.Count > 0 ? 0 : -1;
        }

        public static void UpdateGrid(Pair pair)
        {
            CurrentPair = pair;
            if (dataGrid == null || pairs == null)
            {
                LogUtils.LogError("未初始化");
                return;
            }

            var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
            if (dictConverter == null)
            {
                LogUtils.LogError("无法找到转换器");
                return;
            }

            var originJsonPath = PathUtils.EditFile(pair.Name);
            if (!File.Exists(originJsonPath))
            {
                LogUtils.LogError($"{originJsonPath}不存在");
                return;
            }

            if (!IOUtils.TryInputJson(originJsonPath, dictConverter, out var dataBase) || dataBase == null)
            {
                LogUtils.LogError($"读取{originJsonPath}失败");
                return;
            }

            var dataTable = new DataTable();
            //第一列是翻译key，不允许编辑
            dataTable.Columns.Add(new DataColumn
            {
                ColumnName = KeyColumn,
                ReadOnly = true,
                DataType = typeof(string),
            });
            dataTable.Columns.Add(new DataColumn
            {
                ColumnName = pair.Name,
                DataType = typeof(string),
            });

            foreach (var entry in dataBase)
            {
                var row = dataTable.NewRow();
                row[KeyColumn] = entry.Key;
                row[pair.Name] = entry.Value;
                dataTable.Rows.Add(row);
            }

            dataGrid.ItemsSource = dataTable.DefaultView;
        }

        /// <summary>
        /// 保存当前 Pair 的翻译数据
        /// </summary>
        internal static void Save()
        {
            if (dataGrid == null || pairs == null)
            {
                LogUtils.LogError("未初始化");
                return;
            }

            if (string.IsNullOrEmpty(CurrentPair.Name))
            {
                LogUtils.LogError("当前没有可保存的Pair");
                return;
            }

            var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
            if (dictConverter == null)
            {
                LogUtils.LogError("无法找到转换器");
                return;
            }

            if (dataGrid.ItemsSource is not DataView dataView)
            {
                LogUtils.LogError("DataGrid数据源不正确");
                return;
            }

            var newDataBase = new Dictionary<string, string>();
            foreach (DataRowView rowView in dataView)
            {
                var key = rowView[KeyColumn]?.ToString();
                var value = rowView[CurrentPair.Name]?.ToString();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    newDataBase[key] = value;
                }
            }

            var originJsonPath = PathUtils.EditFile(CurrentPair.Name);
            IOUtils.OutputJson(originJsonPath, newDataBase, dictConverter);
            LogUtils.Log($"{CurrentPair.Name}保存完成，文件路径：{originJsonPath}");
        }

        /// <summary>
        /// 删除选中的翻译 Key，并在所有 Pair 中同步删除该 Key
        /// </summary>
        internal static void DeleteKeys()
        {
            if (dataGrid == null || pairs == null)
            {
                LogUtils.LogError("未初始化");
                return;
            }

            if (dataGrid.SelectedItems.Count == 0)
            {
                LogUtils.LogError("选中项无效");
                return;
            }

            var keys = new List<string>();
            foreach (var item in dataGrid.SelectedItems)
            {
                if (item is not DataRowView rowView)
                {
                    continue;
                }

                var key = rowView[KeyColumn]?.ToString();
                if (string.IsNullOrEmpty(key))
                {
                    LogUtils.LogError("无效的 Key");
                    return;
                }
                keys.Add(key);
            }

            var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
            if (dictConverter == null)
            {
                LogUtils.LogError("无法找到转换器");
                return;
            }

            foreach (var pair in pairs)
            {
                var filePath = PathUtils.EditFile(pair.Name);
                if (!File.Exists(filePath))
                {
                    LogUtils.LogError($"{filePath}不存在");
                    continue;
                }

                if (!IOUtils.TryInputJson(filePath, dictConverter, out var dict) || dict == null)
                {
                    LogUtils.LogError($"读取 {filePath} 失败");
                    continue;
                }

                foreach (var key in keys)
                {
                    dict.Remove(key);
                }
                IOUtils.OutputJson(filePath, dict, dictConverter);
            }

            if (!string.IsNullOrEmpty(CurrentPair.Name))
            {
                UpdateGrid(CurrentPair);
            }
        }
    }
}
