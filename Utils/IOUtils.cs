using System.Collections.Generic;
using System.IO;

namespace LocalizationTranslationWPF.Utils
{
    public static class IOUtils
    {
        public static void OutputJson(string path, Dictionary<string, string> dic, IDictConverter.IDictConverter converter)
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
            File.WriteAllText(path, converter.Serialize(dic));
        }

        public static bool TryInputJson(string originJsonPath, IDictConverter.IDictConverter converter, out Dictionary<string, string>? dict)
        {
            try
            {
                return converter.TryDeserialize(File.ReadAllText(originJsonPath), out dict);
            }
            catch
            {
                dict = null;
                return false;
            }
        }
    }
}
