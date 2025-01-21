using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LocalizationTranslationWPF.DictConverter
{
    public class HhjConverter : IDictConverter.IDictConverter
    {
        public string NameForShow => "Hhj格式";

        public string Serialize(Dictionary<string, string> dict)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var item in dict)
            {
                var kvp = new Kvp() { key = item.Key, value = item.Value };
                sb.AppendLine(JsonConvert.SerializeObject(kvp));
            }
            return sb.ToString();
        }
        public bool TryDeserialize(string text, out Dictionary<string, string>? dict)
        {
            dict = new Dictionary<string, string>();
            string[] list = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in list)
            {
                var kvp = JsonConvert.DeserializeObject<Kvp>(item);
                dict.Add(kvp.key, kvp.value);
            }
            return true;
        }
        struct Kvp
        {
            public string key;
            public string value;
        }
    }
}
