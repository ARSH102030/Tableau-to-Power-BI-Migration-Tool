using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AnalysisServices.Tabular;

namespace TableauToPbi.Services
{
    public class BestPracticeRow
    {
        public string Category      { get; set; } = "";
        public string Object        { get; set; } = "";
        public string Rule          { get; set; } = "";
        public string Severity      { get; set; } = "";  // Error / Warning / Info
        public string SeverityColor { get; set; } = "#888888";
        public string Recommendation { get; set; } = "";
    }

    public static class BestPracticesService
    {
        public static List<BestPracticeRow> Analyze(string pbiConnectionString)
        {
            var rows = new List<BestPracticeRow>();

            Server server;
            Model model;

            try
            {
                server = new Server();
                server.Connect(pbiConnectionString);
                model = server.Databases[0].Model;
            }
            catch (Exception ex)
            {
                rows.Add(new BestPracticeRow
                {
                    Category = "Connection",
                    Object = "PBI Model",
                    Rule = "Cannot connect",
                    Severity = "Error",
                    SeverityColor = "#D83B01",
                    Recommendation = ex.Message
                });
                return rows;
            }

            // Gather context
            var allMeasures  = model.Tables.SelectMany(t => t.Measures).ToList();
            var allColumns   = model.Tables.SelectMany(t => t.Columns
                .Where(c => c.Type != ColumnType.RowNumber)).ToList();
            var allRelations = model.Relationships.ToList();

            // Track which columns are used in relationships
            var relationshipCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SingleColumnRelationship rel in allRelations.OfType<SingleColumnRelationship>())
            {
                relationshipCols.Add($"{rel.FromTable.Name}.{rel.FromColumn.Name}");
                relationshipCols.Add($"{rel.ToTable.Name}.{rel.ToColumn.Name}");
            }

            // 1. Measures without descriptions
            foreach (var m in allMeasures.Where(m => string.IsNullOrWhiteSpace(m.Description)))
                rows.Add(new BestPracticeRow
                {
                    Category = "Documentation",
                    Object = $"[{m.Name}]",
                    Rule = "Missing description",
                    Severity = "Info",
                    SeverityColor = "#0078D4",
                    Recommendation = "Add a description to help report consumers understand the measure."
                });

            // 2. Bidirectional relationships
            foreach (SingleColumnRelationship rel in allRelations.OfType<SingleColumnRelationship>()
                .Where(r => r.CrossFilteringBehavior == CrossFilteringBehavior.BothDirections))
                rows.Add(new BestPracticeRow
                {
                    Category = "Relationship",
                    Object = $"{rel.FromTable.Name} ↔ {rel.ToTable.Name}",
                    Rule = "Bidirectional cross-filter",
                    Severity = "Warning",
                    SeverityColor = "#C19A00",
                    Recommendation = "Bidirectional relationships can cause ambiguity. Consider using CROSSFILTER() in DAX instead."
                });

            // 3. Many-to-many relationships
            foreach (SingleColumnRelationship rel in allRelations.OfType<SingleColumnRelationship>()
                .Where(r => r.FromCardinality == RelationshipEndCardinality.Many
                         && r.ToCardinality   == RelationshipEndCardinality.Many))
                rows.Add(new BestPracticeRow
                {
                    Category = "Relationship",
                    Object = $"{rel.FromTable.Name} → {rel.ToTable.Name}",
                    Rule = "Many-to-many relationship",
                    Severity = "Warning",
                    SeverityColor = "#C19A00",
                    Recommendation = "Many-to-many relationships may cause unexpected aggregations. Use bridge tables where possible."
                });

            // 4. Tables with no relationships
            var connectedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SingleColumnRelationship rel in allRelations.OfType<SingleColumnRelationship>())
            {
                connectedTables.Add(rel.FromTable.Name);
                connectedTables.Add(rel.ToTable.Name);
            }
            foreach (var table in model.Tables.Where(t => !connectedTables.Contains(t.Name)))
                rows.Add(new BestPracticeRow
                {
                    Category = "Relationship",
                    Object = table.Name,
                    Rule = "Isolated table (no relationships)",
                    Severity = "Warning",
                    SeverityColor = "#C19A00",
                    Recommendation = "This table has no relationships. Verify it is intentional (e.g. disconnected table)."
                });

            // 5. Naming conventions — spaces in measure names
            foreach (var m in allMeasures.Where(m => m.Name.Contains(' ')))
                rows.Add(new BestPracticeRow
                {
                    Category = "Naming",
                    Object = $"[{m.Name}]",
                    Rule = "Measure name contains spaces",
                    Severity = "Info",
                    SeverityColor = "#0078D4",
                    Recommendation = "Consider using PascalCase or prefixes (e.g. '# Sales' vs 'Number of Sales')."
                });

            // 6. Visible string columns not in any relationship
            foreach (var col in allColumns.Where(c =>
                c.DataType == DataType.String &&
                !c.IsHidden))
            {
                // Only flag if NOT in a relationship (likely a fact key or descriptor)
                string key = $"{col.Table.Name}.{col.Name}";
                if (!relationshipCols.Contains(key))
                    rows.Add(new BestPracticeRow
                    {
                        Category = "Performance",
                        Object = $"{col.Table.Name}[{col.Name}]",
                        Rule = "Visible string column not in any relationship",
                        Severity = "Info",
                        SeverityColor = "#0078D4",
                        Recommendation = "Consider hiding or removing columns not used in relationships, measures or visuals."
                    });
            }

            // 7. Hidden columns used in active relationships  
            foreach (SingleColumnRelationship rel in allRelations.OfType<SingleColumnRelationship>())
            {
                if (rel.FromColumn.IsHidden)
                    rows.Add(new BestPracticeRow
                    {
                        Category = "Relationship",
                        Object = $"{rel.FromTable.Name}[{rel.FromColumn.Name}]",
                        Rule = "Hidden column used in relationship",
                        Severity = "Warning",
                        SeverityColor = "#C19A00",
                        Recommendation = "Hidden columns in relationships may cause confusion. Ensure they are intentionally hidden."
                    });
                if (rel.ToColumn.IsHidden)
                    rows.Add(new BestPracticeRow
                    {
                        Category = "Relationship",
                        Object = $"{rel.ToTable.Name}[{rel.ToColumn.Name}]",
                        Rule = "Hidden column used in relationship",
                        Severity = "Warning",
                        SeverityColor = "#C19A00",
                        Recommendation = "Hidden columns in relationships may cause confusion. Ensure they are intentionally hidden."
                    });
            }

            // 8. Measures referencing deprecated CALCULATE patterns (heuristic)
            foreach (var m in allMeasures.Where(m =>
                m.Expression != null &&
                m.Expression.Contains("CALCULATE(", StringComparison.OrdinalIgnoreCase) &&
                m.Expression.Contains("ALL(", StringComparison.OrdinalIgnoreCase) &&
                m.Expression.Contains("ALL(", StringComparison.OrdinalIgnoreCase)))
            {
                // Only flag if ALL is used alongside CALCULATE without REMOVEFILTERS (common anti-pattern pre-2019)
                if (!m.Expression.Contains("REMOVEFILTERS(", StringComparison.OrdinalIgnoreCase))
                    rows.Add(new BestPracticeRow
                    {
                        Category = "DAX",
                        Object = $"[{m.Name}]",
                        Rule = "CALCULATE + ALL without REMOVEFILTERS",
                        Severity = "Info",
                        SeverityColor = "#0078D4",
                        Recommendation = "Consider replacing ALL() with REMOVEFILTERS() for clearer intent (Power BI 2019+)."
                    });
            }

            server.Disconnect();
            return rows;
        }
    }
}
