using System.ComponentModel;
using System.Data;
using LocalizationTranslationWPF.Projects;
using LocalizationTranslationWPF.Utils;

namespace LocalizationTranslationWPF.Editing
{
    /// <summary>
    /// 待新增键值的编辑会话。负责校验、筛选和持久化，不引用 WPF 控件或剪贴板。
    /// </summary>
    public sealed class AddNewSession : IDisposable
    {
        public const string KeyColumn = "Key";
        public const string ValueColumn = "Value";
        public const string ValidationColumn = "ValidationMessage";

        private static readonly string[] LineSeparators = { "\r\n", "\n", "\r" };
        private readonly DataTable table = new() { CaseSensitive = false };
        private string filterText = string.Empty;
        private bool suppressChanges;
        private bool refreshingState;

        public AddNewSession()
        {
            table.Columns.Add(new DataColumn(KeyColumn, typeof(string)));
            table.Columns.Add(new DataColumn(ValueColumn, typeof(string)));
            table.Columns.Add(new DataColumn(ValidationColumn, typeof(string))
            {
                DefaultValue = string.Empty,
                ColumnMapping = MappingType.Hidden,
            });
            table.ColumnChanged += TableChanged;
            table.RowChanged += TableRowChanged;
            table.RowDeleted += TableRowChanged;
            table.DefaultView.ListChanged += ViewChanged;
        }

        public event Action? StateChanged;

        public DataView View => table.DefaultView;
        public bool IsDirty => EditingTable.HasChanges(table);
        public int TotalRowCount => table.Rows.Cast<DataRow>()
            .Count(row => row.RowState != DataRowState.Deleted);
        public int VisibleRowCount => View.Count;
        public int ValidationErrorCount => table.Rows.Cast<DataRow>()
            .Count(row => row.RowState != DataRowState.Deleted && row.HasErrors);

        public DataRowView AddEmptyRow()
        {
            filterText = string.Empty;
            table.DefaultView.RowFilter = string.Empty;
            var row = table.Rows.Add(string.Empty, string.Empty);
            return View.Cast<DataRowView>().Single(view => ReferenceEquals(view.Row, row));
        }

        public int AppendTabSeparatedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var added = 0;
            var rows = text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
            suppressChanges = true;
            try
            {
                foreach (var sourceRow in rows)
                {
                    var cells = sourceRow.Split('\t', 2);
                    table.Rows.Add(cells[0], cells.Length > 1 ? cells[1] : string.Empty);
                    added++;
                }
            }
            finally
            {
                suppressChanges = false;
                RefreshState();
            }
            return added;
        }

        public int DeleteRows(IEnumerable<DataRowView> selectedRows)
        {
            var rows = selectedRows.Select(view => view.Row)
                .Where(row => row.Table == table && row.RowState != DataRowState.Deleted &&
                              row.RowState != DataRowState.Detached)
                .Distinct().ToArray();
            suppressChanges = true;
            try
            {
                foreach (var row in rows)
                {
                    row.ClearErrors();
                    row.Delete();
                }
            }
            finally
            {
                suppressChanges = false;
                RefreshState();
            }
            return rows.Length;
        }

        public bool Save()
        {
            ValidateRows();
            if (ValidationErrorCount > 0)
            {
                LogUtils.LogError($"无法保存：表格中还有 {ValidationErrorCount} 行需要修正");
                return false;
            }

            var project = ProjectContext.Current ?? throw new InvalidOperationException("请先创建或打开项目。");
            var entries = table.Rows.Cast<DataRow>().Where(row => row.RowState != DataRowState.Deleted)
                .ToDictionary(row => GetString(row, KeyColumn), row => GetString(row, ValueColumn), StringComparer.Ordinal);
            project.AddSourceEntries(entries);
            Reset();
            LogUtils.Log($"已将 {entries.Count} 条文案加入项目源语言，可直接开始翻译。");
            return true;
        }

        public void Reset()
        {
            filterText = string.Empty;
            ReplaceData(new Dictionary<string, string>());
        }

        public bool DiscardChanges() => !IsDirty || UndoChanges();

        public bool UndoChanges()
        {
            if (!IsDirty) return false;
            suppressChanges = true;
            try
            {
                table.RejectChanges();
            }
            finally
            {
                suppressChanges = false;
            }

            ValidateRows();
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
                if (filterText.Length == 0)
                {
                    table.DefaultView.RowFilter = string.Empty;
                }
                else
                {
                    var value = EditingTable.EscapeFilterValue(filterText);
                    table.DefaultView.RowFilter = $"[{KeyColumn}] LIKE '%{value}%' OR [{ValueColumn}] LIKE '%{value}%'";
                }
            }
            finally
            {
                suppressChanges = wasSuppressingChanges;
            }
            if (!wasSuppressingChanges) RaiseStateChanged();
        }

        private void ReplaceData(Dictionary<string, string> data)
        {
            suppressChanges = true;
            try
            {
                table.Clear();
                foreach (var item in data) table.Rows.Add(item.Key, item.Value);
                table.AcceptChanges();
            }
            finally
            {
                suppressChanges = false;
            }

            ValidateRows();
            ApplyFilter(filterText);
        }

        private void ValidateRows()
        {
            var activeRows = table.Rows.Cast<DataRow>()
                .Where(row => row.RowState != DataRowState.Deleted)
                .ToArray();
            var errors = new Dictionary<DataRow, string>();
            foreach (var row in activeRows)
            {
                var key = GetString(row, KeyColumn);
                if (string.IsNullOrWhiteSpace(key))
                {
                    errors[row] = "Key 不能为空，请填写或移除此行";
                }
                else if (ProjectContext.Current?.ContainsSourceKey(key) == true)
                {
                    errors[row] = "此 Key 已在项目中存在，请在编辑页修改";
                }
            }

            var duplicates = activeRows
                .Where(row => !string.IsNullOrWhiteSpace(GetString(row, KeyColumn)))
                .GroupBy(row => GetString(row, KeyColumn), StringComparer.Ordinal)
                .Where(group => group.Count() > 1);
            foreach (var group in duplicates)
            {
                foreach (var row in group) errors[row] = $"Key“{group.Key}”重复";
            }

            var wasSuppressingChanges = suppressChanges;
            suppressChanges = true;
            try
            {
                foreach (var row in activeRows)
                {
                    var error = errors.GetValueOrDefault(row, string.Empty);
                    if (row.RowError != error) row.RowError = error;
                    if (row.GetColumnError(KeyColumn) != error) row.SetColumnError(KeyColumn, error);
                    // DataRow errors do not reliably notify nested WPF bindings. A hidden
                    // metadata column supplies normal DataRowView property notifications.
                    if (GetString(row, ValidationColumn) != error) row[ValidationColumn] = error;
                }
            }
            finally
            {
                suppressChanges = wasSuppressingChanges;
            }
        }

        private void TableChanged(object sender, DataColumnChangeEventArgs e)
        {
            if (!suppressChanges && e.Row.RowState != DataRowState.Detached) RefreshState();
        }

        private void TableRowChanged(object? sender, DataRowChangeEventArgs e)
        {
            if (!suppressChanges) RefreshState();
        }

        // WPF also calls this after Esc/CancelEdit: DataRow can discard a proposed value
        // without raising ColumnChanged or RowChanged.
        public void RefreshState()
        {
            if (refreshingState) return;
            refreshingState = true;
            try
            {
                ValidateRows();
                RaiseStateChanged();
            }
            finally
            {
                refreshingState = false;
            }
        }

        private void ViewChanged(object? sender, ListChangedEventArgs e)
        {
            if (!suppressChanges) RefreshState();
        }

        private void RaiseStateChanged() => StateChanged?.Invoke();

        private static string GetString(DataRow row, string column) => row[column] == DBNull.Value
            ? string.Empty
            : row[column]?.ToString() ?? string.Empty;

        public void Dispose()
        {
            table.ColumnChanged -= TableChanged;
            table.RowChanged -= TableRowChanged;
            table.RowDeleted -= TableRowChanged;
            table.DefaultView.ListChanged -= ViewChanged;
            table.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
