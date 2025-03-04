using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Utils;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using System.Windows.Controls;

namespace LocalizationTranslationWPF.Editor
{
    public static class EditorUtils
    {
        private static DataGrid? dataGrid;
        private static Dictionary<string, string>? dataBase;
        private static List<Pair>? pairs;
        // 新增一个当前编辑的 Pair 记录
        private static Pair CurrentPair { get; set; }

        public static void Init(DataGrid dataGrid, List<Pair> pairs)
        {
            EditorUtils.dataGrid = dataGrid;
            EditorUtils.pairs = pairs;
            dataBase?.Clear();
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
            var originJsonPath = $"{PathUtils.EditPath}\\{pair.Name}.json";
            if (!File.Exists(originJsonPath))
            {
                LogUtils.LogError($"{PathUtils.EditPath}\\{pair.Name}.json不存在");
                return;
            }
            if (!TaskUtils.TryInputJson(originJsonPath, dictConverter, out dataBase) || dataBase == null)
            {
                LogUtils.LogError($"读取{originJsonPath}失败");
                return;
            }

            // Create a new DataTable
            var dataTable = new DataTable();

            // Add a column for the translation key.
            dataTable.Columns.Add(new DataColumn()
            {
                ColumnName = "Key",
                ReadOnly = true,
                DataType = typeof(string),
            });
            dataTable.Columns.Add(new DataColumn()
            {
                ColumnName = pair.Name,
                DataType = typeof(string),
            });

            // Populate the DataTable rows using dataBase
            foreach (var entry in dataBase)
            {
                // entry.Key is the translation key
                var row = dataTable.NewRow();
                row["Key"] = entry.Key;
                row[pair.Name] = entry.Value;
                dataTable.Rows.Add(row);
            }

            // Finally, bind the DataTable to the DataGrid
            dataGrid.ItemsSource = dataTable.DefaultView;
            //设置第一列不可编辑

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
                string? key = rowView["Key"]?.ToString();
                string? value = rowView[CurrentPair.Name]?.ToString();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    newDataBase[key] = value;
                }
            }

            var originJsonPath = $"{PathUtils.EditPath}\\{CurrentPair.Name}.json";
            TaskUtils.OutputJson(originJsonPath, newDataBase, dictConverter);
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

            List<string> keys = new();
            foreach (var item in dataGrid.SelectedItems)
            {
                if (item is not DataRowView rowView)
                {
                    continue;
                }
                string? key = rowView["Key"].ToString();
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
                string filePath = $"{PathUtils.EditPath}\\{pair.Name}.json";
                if (!File.Exists(filePath))
                {
                    LogUtils.LogError($"{filePath}不存在");
                    continue;
                }

                if (!TaskUtils.TryInputJson(filePath, dictConverter, out var dict) || dict == null)
                {
                    LogUtils.LogError($"读取 {filePath} 失败");
                    continue;
                }

                foreach (var key in keys)
                {
                    if (dict.ContainsKey(key))
                    {
                        dict.Remove(key);
                    }
                }
                TaskUtils.OutputJson(filePath, dict, dictConverter);
            }

            UpdateGrid(CurrentPair);
        }
    }
}
