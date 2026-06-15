using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Controls;

namespace HR_Codex_v0.Helpers
{
    public static class CsvExporter
    {
        public static void SaveDataGridToCsv(DataGrid dataGrid, string filePath = null)
        {
            if (filePath == null)
                filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"data_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            foreach (var col in dataGrid.Columns)
            {
                if (sb.Length > 0) sb.Append(",");
                sb.Append(Escape(col.Header.ToString()));
            }
            sb.AppendLine();

            foreach (var item in dataGrid.Items)
            {
                bool first = true;
                foreach (var col in dataGrid.Columns)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    var val = col.GetCellContent(item) is TextBlock tb ? tb.Text : "";
                    sb.Append(Escape(val));
                }
                sb.AppendLine();
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string Escape(string field)
        {
            if (string.IsNullOrEmpty(field)) return "\"\"";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\r") || field.Contains("\n"))
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            return field;
        }
    }
}

