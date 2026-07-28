using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.AnalysisServices.AdomdClient;

namespace TableauToPbi.Services
{
    public class DataValueRow
    {
        public string Dimension     { get; set; } = "";
        public string TableauValue  { get; set; } = "";
        public string PbiValue      { get; set; } = "";
        public double? Variance     { get; set; }
        public string VariancePct   { get; set; } = "";
        public string Status        { get; set; } = "";
        public string StatusColor   { get; set; } = "#888888";
    }

    public class DataComparisonOptions
    {
        public double Threshold     { get; set; } = 0.01;  // 1% default
        public string DaxQuery      { get; set; } = "";
        public string DimensionColumn { get; set; } = "";  // column in CSV to key on
        public string ValueColumn   { get; set; } = "";    // numeric column to compare
    }

    public static class DataValuesComparisonService
    {
        /// <summary>
        /// Load a CSV file exported from Tableau.
        /// Returns the DataTable representation.
        /// </summary>
        public static DataTable LoadCsv(string filePath)
        {
            var dt = new DataTable();
            var lines = File.ReadAllLines(filePath);
            if (lines.Length == 0) return dt;

            // Header row
            string[] headers = SplitCsvLine(lines[0]);
            foreach (var h in headers)
                dt.Columns.Add(h.Trim('"').Trim());

            // Data rows
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                string[] parts = SplitCsvLine(lines[i]);
                var row = dt.NewRow();
                for (int j = 0; j < dt.Columns.Count && j < parts.Length; j++)
                    row[j] = parts[j].Trim('"').Trim();
                dt.Rows.Add(row);
            }

            return dt;
        }

        /// <summary>
        /// Execute a DAX query against PBI Desktop and return results as DataTable.
        /// </summary>
        public static DataTable QueryPbi(string connectionString, string daxQuery)
        {
            var dt = new DataTable();
            using var conn = new AdomdConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = daxQuery;
            using var reader = cmd.ExecuteReader();

            // Build columns from schema
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string name = reader.GetName(i);
                // Sanitise column name for DataGrid binding
                name = name.Replace("[", "(").Replace("]", ")");
                dt.Columns.Add(name);
            }

            while (reader.Read())
            {
                var row = dt.NewRow();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[i] = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
                dt.Rows.Add(row);
            }

            conn.Close();
            return dt;
        }

        /// <summary>
        /// Compare CSV (Tableau export) vs PBI query results row by row.
        /// </summary>
        public static List<DataValueRow> Compare(
            DataTable csvData,
            DataTable pbiData,
            DataComparisonOptions opts)
        {
            var rows = new List<DataValueRow>();

            if (string.IsNullOrWhiteSpace(opts.DimensionColumn) ||
                string.IsNullOrWhiteSpace(opts.ValueColumn))
            {
                rows.Add(new DataValueRow
                {
                    Dimension = "Configuration Error",
                    Status = "✖ Error",
                    StatusColor = "#D83B01",
                    TableauValue = "Select dimension and value columns to compare"
                });
                return rows;
            }

            // Build lookup from PBI: dim → value
            var pbiLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (pbiData.Columns.Contains(opts.DimensionColumn) &&
                pbiData.Columns.Contains(opts.ValueColumn))
            {
                foreach (DataRow r in pbiData.Rows)
                {
                    string key = r[opts.DimensionColumn]?.ToString() ?? "";
                    string val = r[opts.ValueColumn]?.ToString() ?? "";
                    pbiLookup[key] = val;
                }
            }

            foreach (DataRow csvRow in csvData.Rows)
            {
                if (!csvData.Columns.Contains(opts.DimensionColumn) ||
                    !csvData.Columns.Contains(opts.ValueColumn)) continue;

                string dim = csvRow[opts.DimensionColumn]?.ToString() ?? "";
                string tableauVal = csvRow[opts.ValueColumn]?.ToString() ?? "";

                if (!pbiLookup.TryGetValue(dim, out string? pbiVal))
                {
                    rows.Add(new DataValueRow
                    {
                        Dimension    = dim,
                        TableauValue = tableauVal,
                        PbiValue     = "—",
                        VariancePct  = "—",
                        Status       = "✖ Missing in PBI",
                        StatusColor  = "#D83B01"
                    });
                    continue;
                }

                bool tableauIsNum = TryParseNum(tableauVal, out double tNum);
                bool pbiIsNum     = TryParseNum(pbiVal, out double pNum);

                if (tableauIsNum && pbiIsNum)
                {
                    double absRef = Math.Abs(tNum) > 0 ? Math.Abs(tNum) : 1;
                    double variancePct = Math.Abs(tNum - pNum) / absRef;
                    bool ok = variancePct <= opts.Threshold;

                    rows.Add(new DataValueRow
                    {
                        Dimension    = dim,
                        TableauValue = tableauVal,
                        PbiValue     = pbiVal,
                        Variance     = Math.Abs(tNum - pNum),
                        VariancePct  = variancePct.ToString("P2"),
                        Status       = ok ? "✔ Match" : "✖ Variance",
                        StatusColor  = ok ? "#107C10" : "#D83B01"
                    });
                }
                else
                {
                    bool match = string.Equals(tableauVal, pbiVal, StringComparison.OrdinalIgnoreCase);
                    rows.Add(new DataValueRow
                    {
                        Dimension    = dim,
                        TableauValue = tableauVal,
                        PbiValue     = pbiVal,
                        VariancePct  = match ? "—" : "Text differs",
                        Status       = match ? "✔ Match" : "✖ Mismatch",
                        StatusColor  = match ? "#107C10" : "#D83B01"
                    });
                }
            }

            return rows;
        }

        private static bool TryParseNum(string s, out double d)
            => double.TryParse(s?.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out d);

        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuote = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuote && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuote = !inQuote;
                    }
                }
                else if (c == ',' && !inQuote)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
