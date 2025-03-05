using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    }
}
