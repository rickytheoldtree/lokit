using System.ComponentModel;
using System.Data;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.Projects;

namespace LocalizationTranslationWPF.Editing
{
    /// <summary>A language editor bound to a project snapshot, with no file paths or controls.</summary>
    public sealed class EditorSession : IDisposable
    {
        public const string KeyColumn = "Key";
        public const string ValueColumn = "Value";
        private DataTable? table;
        private ProjectWorkspace? workspace;
        private Dictionary<string, string>? loadedValues;
        private Pair currentPair;
        private long loadedRevision;
        private string filterText = string.Empty;
        private bool suppressChanges;

        public event Action? StateChanged;
        public IReadOnlyList<Pair> Pairs { get; private set; } = Array.Empty<Pair>();
        public DataView? View => table?.DefaultView;
        public bool HasLoadedData => table != null;
        public bool IsDirty => table != null && EditingTable.HasChanges(table);
        public bool NeedsReload => workspace != null && workspace.Revision != loadedRevision;
        public string CurrentLanguageName => currentPair.Name ?? string.Empty;
        public int TotalRowCount => table?.Rows.Count ?? 0;
        public int VisibleRowCount => View?.Count ?? 0;
        public int EmptyValueCount => table?.Rows.Cast<DataRow>().Count(row => string.IsNullOrWhiteSpace(Text(row, ValueColumn))) ?? 0;

        public void RefreshPairs()
        {
            Pairs = ProjectContext.Current?.Pairs ?? Array.Empty<Pair>();
            Notify();
        }

        public bool Load(Pair pair)
        {
            var project = ProjectContext.Current ?? throw new InvalidOperationException("请先创建或打开项目。");
            var values = project.ReadLanguage(pair.Name);
            var next = new DataTable { CaseSensitive = false };
            next.Columns.Add(new DataColumn(KeyColumn, typeof(string)) { ReadOnly = true });
            next.Columns.Add(ValueColumn, typeof(string));
            foreach (var entry in values) next.Rows.Add(entry.Key, entry.Value);
            next.AcceptChanges();
            Detach();
            table = next;
            workspace = project;
            loadedValues = values;
            loadedRevision = project.Revision;
            currentPair = pair;
            table.ColumnChanged += ColumnChanged;
            table.RowChanged += RowChanged;
            table.DefaultView.ListChanged += ViewChanged;
            ApplyFilter(filterText);
            return true;
        }

        public bool Save()
        {
            if (table == null || workspace == null) throw new InvalidOperationException("没有可保存的项目数据。");
            var values = table.Rows.Cast<DataRow>().ToDictionary(row => Text(row, KeyColumn), row => Text(row, ValueColumn), StringComparer.Ordinal);
            workspace.SaveLanguage(currentPair.Name, values, loadedValues!);
            return Load(currentPair);
        }

        public bool DeleteKeys(IEnumerable<string> keys)
        {
            if (workspace == null) throw new InvalidOperationException("没有打开项目。");
            workspace.DeleteKeys(keys);
            return Load(currentPair);
        }

        public bool DiscardChanges() => !IsDirty || UndoChanges();
        public bool UndoChanges()
        {
            if (table == null || !IsDirty) return false;
            suppressChanges = true;
            try { table.RejectChanges(); }
            finally { suppressChanges = false; }
            ApplyFilter(filterText);
            return true;
        }

        public void ApplyFilter(string text)
        {
            filterText = text?.Trim() ?? string.Empty;
            var wasSuppressingChanges = suppressChanges;
            suppressChanges = true;
            try
            {
                if (table != null)
                {
                    var escaped = EditingTable.EscapeFilterValue(filterText);
                    table.DefaultView.RowFilter = filterText.Length == 0 ? string.Empty
                        : $"[{KeyColumn}] LIKE '%{escaped}%' OR [{ValueColumn}] LIKE '%{escaped}%'";
                }
            }
            finally
            {
                suppressChanges = wasSuppressingChanges;
            }
            Notify();
        }

        public void Reset()
        {
            Detach();
            table = null;
            workspace = null;
            loadedValues = null;
            currentPair = default;
            filterText = string.Empty;
            Notify();
        }

        private void Detach()
        {
            if (table == null) return;
            table.ColumnChanged -= ColumnChanged;
            table.RowChanged -= RowChanged;
            table.DefaultView.ListChanged -= ViewChanged;
            table.Dispose();
        }

        public void Dispose()
        {
            Detach();
            table = null;
            workspace = null;
            loadedValues = null;
            GC.SuppressFinalize(this);
        }

        private void ColumnChanged(object sender, DataColumnChangeEventArgs e) => Notify();
        private void RowChanged(object? sender, DataRowChangeEventArgs e) => Notify();
        private void ViewChanged(object? sender, ListChangedEventArgs e) => Notify();
        private void Notify() { if (!suppressChanges) StateChanged?.Invoke(); }
        private static string Text(DataRow row, string column) => row[column]?.ToString() ?? string.Empty;
    }
}
