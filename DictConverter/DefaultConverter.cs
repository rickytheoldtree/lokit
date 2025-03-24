using Newtonsoft.Json;
using System.Collections.Generic;

namespace DictConverter
{
    public class DefaultConverter : IDictConverter.IDictConverter
    {
        public string NameForShow => "标准格式";
        public string Serialize(Dictionary<string, string> dict)
        {
            return JsonConvert.SerializeObject(dict);
        }
        public override string ToString()
        {
            return NameForShow;
        }

        public bool TryDeserialize(string text, out Dictionary<string, string>? dict)
        {
            dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(text);
            return dict != null;
        }
    }
}
