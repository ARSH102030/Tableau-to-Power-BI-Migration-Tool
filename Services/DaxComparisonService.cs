using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AnalysisServices.Tabular;
using TableauToPbi.Models;

namespace TableauToPbi.Services
{
    public class DaxComparisonRow
    {
        public string MeasureName       { get; set; } = "";
        public string DataSource        { get; set; } = "";
        public string TableauFormula    { get; set; } = "";
        public string ConvertedDax      { get; set; } = "";
        public string PbiDax            { get; set; } = "";
        public string DiffStatus        { get; set; } = "";
        public string DiffStatusColor   { get; set; } = "#888888";
        public string Notes             { get; set; } = "";
    }

    public static class DaxComparisonService
    {
        public static List<DaxComparisonRow> Compare(
            TableauWorkbook workbook,
            string pbiConnectionString,
            TableauToDaxConverter converter)
        {
            var rows = new List<DaxComparisonRow>();

            // Load PBI measures
            var pbiMeasures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var server = new Server();
                server.Connect(pbiConnectionString);
                var db = server.Databases[0];
                foreach (var table in db.Model.Tables)
                    foreach (var measure in table.Measures)
                        pbiMeasures[measure.Name] = measure.Expression ?? "";
                server.Disconnect();
            }
            catch (Exception ex)
            {
                rows.Add(new DaxComparisonRow
                {
                    MeasureName = "Connection Error",
                    DiffStatus = "✖ Error",
                    DiffStatusColor = "#D83B01",
                    Notes = ex.Message
                });
                return rows;
            }

            // Compare each calculated field
            foreach (var ds in workbook.DataSources)
            {
                foreach (var field in ds.Fields.Where(f => f.IsCalculated))
                {
                    var convResult = converter.Convert(field.Formula ?? "");
                    string convertedDax = convResult.DaxExpression;

                    string pbiDax = "";
                    string status;
                    string color;
                    string notes = "";

                    if (pbiMeasures.TryGetValue(field.DisplayName, out string? raw))
                    {
                        pbiDax = raw;
                        double similarity = ComputeSimilarity(
                            NormalizeDax(convertedDax),
                            NormalizeDax(pbiDax));

                        if (similarity >= 0.90)
                        {
                            status = "✔ Match";
                            color  = "#107C10";
                        }
                        else if (similarity >= 0.60)
                        {
                            status = "⚠ Partial Match";
                            color  = "#C19A00";
                            notes  = $"Similarity: {similarity:P0}";
                        }
                        else
                        {
                            status = "✖ Mismatch";
                            color  = "#D83B01";
                            notes  = $"Similarity: {similarity:P0} — logic likely differs";
                        }
                    }
                    else
                    {
                        status = "— Not in PBI";
                        color  = "#888888";
                        notes  = "No matching measure found in Power BI model";
                    }

                    rows.Add(new DaxComparisonRow
                    {
                        MeasureName    = field.DisplayName,
                        DataSource     = ds.DisplayName,
                        TableauFormula = field.Formula ?? "",
                        ConvertedDax   = convertedDax,
                        PbiDax         = pbiDax,
                        DiffStatus     = status,
                        DiffStatusColor = color,
                        Notes          = notes
                    });
                }
            }

            return rows;
        }

        /// <summary>Normalise DAX for comparison: strip whitespace, lowercase, remove table prefixes.</summary>
        private static string NormalizeDax(string dax)
        {
            if (string.IsNullOrWhiteSpace(dax)) return "";
            dax = dax.ToLowerInvariant();
            // Remove quoted table names e.g. 'Sales'[Amount] → [amount]
            dax = Regex.Replace(dax, @"'[^']*'\[", "[");
            // Collapse whitespace
            dax = Regex.Replace(dax, @"\s+", " ").Trim();
            return dax;
        }

        /// <summary>Jaccard similarity on trigrams.</summary>
        private static double ComputeSimilarity(string a, string b)
        {
            if (a == b) return 1.0;
            if (a.Length < 3 || b.Length < 3) return a == b ? 1.0 : 0.0;

            var setA = GetTrigrams(a);
            var setB = GetTrigrams(b);

            int intersection = setA.Intersect(setB).Count();
            int union = setA.Union(setB).Count();
            return union == 0 ? 0 : (double)intersection / union;
        }

        private static HashSet<string> GetTrigrams(string s)
        {
            var set = new HashSet<string>();
            for (int i = 0; i < s.Length - 2; i++)
                set.Add(s.Substring(i, 3));
            return set;
        }
    }
}
