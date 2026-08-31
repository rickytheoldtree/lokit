using System;
using System.Collections.Generic;
using System.Windows;

namespace LocalizationTranslationWPF.Utils
{
    public enum LogLevel
    {
        Information,
        Error,
    }

    public sealed record LogEntry(DateTime Timestamp, string Text, LogLevel Level);

    public static class LogUtils
    {
        private const int MaxEntries = 5000;

        private static readonly object SyncRoot = new();
        private static readonly List<LogEntry> Entries = new();
        public static event Action<LogEntry>? EntryAdded;

        public static void Log(string text) => Append(text, LogLevel.Information);

        public static void LogError(string text) => Append(text, LogLevel.Error);

        public static IReadOnlyList<LogEntry> GetEntries()
        {
            lock (SyncRoot)
            {
                return Entries.ToArray();
            }
        }

        public static void Clear()
        {
            lock (SyncRoot)
            {
                Entries.Clear();
            }
        }

        private static void Append(string text, LogLevel level)
        {
            var entry = new LogEntry(DateTime.Now, text, level);
            lock (SyncRoot)
            {
                Entries.Add(entry);
                if (Entries.Count > MaxEntries)
                {
                    Entries.RemoveRange(0, Entries.Count - MaxEntries);
                }
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                RaiseEntryAdded(entry);
                return;
            }

            if (dispatcher.CheckAccess())
            {
                RaiseEntryAdded(entry);
            }
            else
            {
                dispatcher.BeginInvoke(() => RaiseEntryAdded(entry));
            }
        }

        private static void RaiseEntryAdded(LogEntry entry)
        {
            try
            {
                EntryAdded?.Invoke(entry);
            }
            catch
            {
                // 日志显示失败不应中断正在执行的翻译任务。
            }
        }
    }
}
