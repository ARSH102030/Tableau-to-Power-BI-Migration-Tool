using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AnalysisServices.Tabular;
using TableauToPbi.Models;

namespace TableauToPbi.Services
{
    public enum SchemaMatchStatus { Found, Missing, TypeMismatch }

    public class SchemaComparisonRow
    {
        public string FieldName     { get; set; } = "";
        public string FieldType     { get; set; } = "";   // Measure / Dimension
        public string TableauType   { get; set; } = "";
        public string PbiObject     { get; set; } = "";   // Matched measure/column name in PBI
        public string PbiType       { get; set; } = "";
        public string Status        { get; set; } = "";
        public string StatusColor   { get; set; } = "#888888";
        public string Notes         { get; set; } = "";
    }

    public static class SchemaComparisonService
    {
        public static List<SchemaComparisonRow> Compare(
            TableauWorkbook workbook,
            string pbiConnectionString)
        {
            var rows = new List<SchemaComparisonRow>();

            // Build lookup from PBI model
            var pbiMeasures  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pbiColumns   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var server = new Server();
                server.Connect(pbiConnectionString);
                var db = server.Databases[0];
                var model = db.Model;

                foreach (var table in model.Tables)
                {
                    foreach (var measure in table.Measures)
                        pbiMeasures[measure.Name] = measure.DataType.ToString();

                    foreach (var col in table.Columns)
                        if (col.Type != ColumnType.RowNumber)
                            pbiColumns[$"{table.Name}.{col.Name}"] = col.DataType.ToString();
                }

                server.Disconnect();
            }
            catch (Exception ex)
            {
                rows.Add(new SchemaComparisonRow
                {
                    FieldName = "Connection Error",
                    Status = "✖ Error",
                    StatusColor = "#D83B01",
                    Notes = ex.Message
                });
                return rows;
            }

            // Compare each Tableau field
            foreach (var ds in workbook.DataSources)
            {
                foreach (var field in ds.Fields)
                {
                    string displayName = field.DisplayName;
                    bool isMeasure = field.Role?.Equals("measure", StringComparison.OrdinalIgnoreCase) == true
                                     || field.IsCalculated;

                    if (isMeasure)
                    {
                        if (pbiMeasures.TryGetValue(displayName, out string? pbiType))
                        {
                            string tType = MapTableauType(field.DataType);
                            bool typeOk = IsTypeCompatible(tType, pbiType);
                            rows.Add(new SchemaComparisonRow
                            {
                                FieldName   = displayName,
                                FieldType   = "Measure",
                                TableauType = field.DataType,
                                PbiObject   = displayName,
                                PbiType     = pbiType,
                                Status      = typeOk ? "✔ Found" : "⚠ Type Mismatch",
                                StatusColor = typeOk ? "#107C10" : "#C19A00",
                                Notes       = typeOk ? "" : $"Tableau: {tType} → PBI: {pbiType}"
                            });
                        }
                        else
                        {
                            rows.Add(new SchemaComparisonRow
                            {
                                FieldName   = displayName,
                                FieldType   = "Measure",
                                TableauType = field.DataType,
                                PbiObject   = "—",
                                PbiType     = "—",
                                Status      = "✖ Missing",
                                StatusColor = "#D83B01",
                                Notes       = "Measure not found in Power BI model"
                            });
                        }
                    }
                    else
                    {
                        // Dimension — look for matching column (any table)
                        var matchKey = pbiColumns.Keys.FirstOrDefault(k =>
                            k.EndsWith("." + displayName, StringComparison.OrdinalIgnoreCase));

                        if (matchKey != null)
                        {
                            rows.Add(new SchemaComparisonRow
                            {
                                FieldName   = displayName,
                                FieldType   = "Dimension",
                                TableauType = field.DataType,
                                PbiObject   = matchKey,
                                PbiType     = pbiColumns[matchKey],
                                Status      = "✔ Found",
                                StatusColor = "#107C10"
                            });
                        }
                        else
                        {
                            rows.Add(new SchemaComparisonRow
                            {
                                FieldName   = displayName,
                                FieldType   = "Dimension",
                                TableauType = field.DataType,
                                PbiObject   = "—",
                                PbiType     = "—",
                                Status      = "✖ Missing",
                                StatusColor = "#D83B01",
                                Notes       = "Column not found in any Power BI table"
                            });
                        }
                    }
                }
            }

            return rows;
        }

        private static string MapTableauType(string t) => t?.ToLowerInvariant() switch
        {
            "real" or "float"   => "Double",
            "integer"           => "Int64",
            "string"            => "String",
            "boolean"           => "Boolean",
            "date"              => "DateTime",
            "datetime"          => "DateTime",
            _                   => t ?? "Unknown"
        };

        private static bool IsTypeCompatible(string tableau, string pbi)
        {
            pbi = pbi.ToLowerInvariant();
            return tableau.ToLowerInvariant() switch
            {
                "double"   => pbi.Contains("double") || pbi.Contains("decimal"),
                "int64"    => pbi.Contains("int"),
                "string"   => pbi.Contains("string"),
                "boolean"  => pbi.Contains("bool"),
                "datetime" => pbi.Contains("date"),
                _          => true
            };
        }
    }
}
