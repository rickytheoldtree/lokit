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
        private static DataGrid DataGrid { get; set; } = new DataGrid();
        private static Dictionary<string, string> dataBase = new Dictionary<string, string>();
        private static List<Pair> Pairs { get; set; } = new List<Pair>();
        // 新增一个当前编辑的 Pair 记录
        private static Pair CurrentPair { get; set; }

        public static void Init(DataGrid dataGrid, List<Pair> pairs)
        {
            DataGrid = dataGrid;
            Pairs = pairs;
            dataBase.Clear();
        }

        public static void UpdateGrid(Pair pair)
        {
            CurrentPair = pair;
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
            dataTable.Columns.Add("Key", typeof(string));
            dataTable.Columns.Add(pair.Name, typeof(string));

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
            DataGrid.ItemsSource = dataTable.DefaultView;
        }

        /// <summary>
        /// 保存当前 Pair 的翻译数据
        /// </summary>
        internal static void Save()
        {
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

            // 从 DataGrid 获取最新数据（DataGrid.ItemsSource 为 DataView）
            if (!(DataGrid.ItemsSource is DataView dataView))
            {
                LogUtils.LogError("DataGrid数据源不正确");
                return;
            }

            var newDataBase = new Dictionary<string, string>();
            foreach (DataRowView rowView in dataView)
            {
                string key = rowView["Key"]?.ToString();
                string value = rowView[CurrentPair.Name]?.ToString();
                if (!string.IsNullOrEmpty(key))
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
        internal static void DeleteKey()
        {
            // 获取 DataGrid 中选中的行
            if (DataGrid.SelectedItem == null)
            {
                LogUtils.LogError("未选中删除项");
                return;
            }

            if (!(DataGrid.SelectedItem is DataRowView rowView))
            {
                LogUtils.LogError("选中项无效");
                return;
            }

            string keyToDelete = rowView["Key"].ToString();
            if (string.IsNullOrEmpty(keyToDelete))
            {
                LogUtils.LogError("无效的 Key");
                return;
            }

            var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
            if (dictConverter == null)
            {
                LogUtils.LogError("无法找到转换器");
                return;
            }

            // 遍历所有 Pair，删除对应 key 并保存
            foreach (var pair in Pairs)
            {
                string filePath = $"{PathUtils.EditPath}\\{pair.Name}.json";
                if (!File.Exists(filePath))
                {
                    LogUtils.LogError($"{filePath}不存在");
                    continue;
                }

                // 读取 JSON 文件到字典
                Dictionary<string, string> dict;
                if (!TaskUtils.TryInputJson(filePath, dictConverter, out dict) || dict == null)
                {
                    LogUtils.LogError($"读取 {filePath} 失败");
                    continue;
                }

                // 如果存在则删除 key
                if (dict.ContainsKey(keyToDelete))
                {
                    dict.Remove(keyToDelete);
                }

                TaskUtils.OutputJson(filePath, dict, dictConverter);
            }

            // 删除后刷新当前 Pair 的 DataGrid 展示
            UpdateGrid(CurrentPair);
        }
    }
}
