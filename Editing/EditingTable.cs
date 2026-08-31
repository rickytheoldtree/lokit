using System;
using System.Data;

namespace LocalizationTranslationWPF.Editing
{
    internal static class EditingTable
    {
        // DataRowState alone misses proposed edits and treats an edit back to its original value as dirty.
        public static bool HasChanges(DataTable table)
        {
            foreach (DataRow row in table.Rows)
            {
                if (row.RowState is DataRowState.Added or DataRowState.Deleted) return true;
                foreach (DataColumn column in table.Columns)
                {
                    if (column.ColumnMapping == MappingType.Hidden) continue;
                    if (!Equals(row[column], row[column, DataRowVersion.Original])) return true;
                }
            }
            return false;
        }

        public static string EscapeFilterValue(string value) => value
            .Replace("'", "''")
            .Replace("[", "[[]")
            .Replace("%", "[%]")
            .Replace("*", "[*]");
    }
}
