using System.IO;
using System.Text;

namespace LocalizationTranslationWPF.Utils
{
    public static class IOUtils
    {
        public static void OutputJson(string path, Dictionary<string, string> dic,
            IDictConverter.IDictConverter converter)
        {
            // 先完成序列化，再接触目标文件；转换器异常不会破坏原文件。
            AtomicWriteAllText(path, converter.Serialize(dic));
        }

        /// <summary>
        /// 在目标文件所在目录写入临时文件，完整写入后再替换目标文件。
        /// 这样进程退出、磁盘写满等异常不会留下半份 JSON。
        /// </summary>
        public static void AtomicWriteAllText(string path, string content)
            => AtomicWriteAllBytes(path, Encoding.UTF8.GetBytes(content));

        public static void AtomicWriteAllBytes(string path, byte[] content, bool overwrite = true)
        {
            var fullPath = Path.GetFullPath(path);
            var folder = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("目标文件没有有效目录");
            Directory.CreateDirectory(folder);

            var tempPath = Path.Combine(folder, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(content);
                    stream.Flush(true);
                }
                File.Move(tempPath, fullPath, overwrite);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        public static bool TryInputJson(string originJsonPath, IDictConverter.IDictConverter converter,
            out Dictionary<string, string>? dict)
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
