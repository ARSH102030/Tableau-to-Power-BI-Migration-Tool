using System.Collections.Generic;

namespace TableauToPbi.Models
{
    public class TableauWorkbook
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string SourceBuild { get; set; } = string.Empty;
        public List<TableauDataSource> DataSources { get; set; } = new();
        public List<string> WorksheetNames { get; set; } = new();
        public List<string> DashboardNames { get; set; } = new();
        public int WorksheetCount => WorksheetNames.Count;
        public int DashboardCount => DashboardNames.Count;
        public int CalculatedFieldCount
        {
            get
            {
                int count = 0;
                foreach (var ds in DataSources)
                    foreach (var f in ds.Fields)
                        if (f.IsCalculated) count++;
                return count;
            }
        }
    }

    public class TableauDataSource
    {
        public string Name { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string ConnectionType { get; set; } = string.Empty;
        public List<TableauField> Fields { get; set; } = new();
        public string DisplayName => !string.IsNullOrWhiteSpace(Caption) ? Caption : Name;
    }

    public class TableauField
    {
        public string Name { get; set; } = string.Empty;          // e.g. "[Calculation_123]"
        public string Caption { get; set; } = string.Empty;       // e.g. "Sales Growth"
        public string DataType { get; set; } = string.Empty;      // "real", "string", "integer", etc.
        public string Role { get; set; } = string.Empty;          // "measure", "dimension"
        public string FieldType { get; set; } = string.Empty;     // "quantitative", "nominal", "ordinal"
        public string? Formula { get; set; }                      // null if not calculated
        public bool IsCalculated => !string.IsNullOrWhiteSpace(Formula);
        public string DisplayName => !string.IsNullOrWhiteSpace(Caption) ? Caption : Name.Trim('[', ']');

        // Filled after conversion
        public string? DaxFormula { get; set; }
        public ConversionStatus ConversionStatus { get; set; } = ConversionStatus.NotConverted;
        public List<string> ConversionWarnings { get; set; } = new();
        public List<string> ConversionNotes { get; set; } = new();

        public string StatusLabel => ConversionStatus switch
        {
            ConversionStatus.Success => "✔ Converted",
            ConversionStatus.PartialConversion => "⚠ Partial",
            ConversionStatus.ManualRequired => "✖ Manual Required",
            ConversionStatus.NotConverted => "—",
            _ => ""
        };
    }

    public class FunctionReference
    {
        public string Category { get; set; } = string.Empty;
        public string TableauFunction { get; set; } = string.Empty;
        public string DaxEquivalent { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public class DataTypeMapping
    {
        public string TableauType { get; set; } = string.Empty;
        public string PowerBiType { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public class CalculatedFieldRow
    {
        public string FieldName { get; set; } = string.Empty;
        public string DataSource { get; set; } = string.Empty;
        public string TableauFormula { get; set; } = string.Empty;
        public string DaxFormula { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusColor { get; set; } = "#888888";
        public string Notes { get; set; } = string.Empty;
        public TableauField? SourceField { get; set; }
    }

    public class FieldDependencyRow
    {
        public string FieldName       { get; set; } = string.Empty;
        public string DataSource      { get; set; } = string.Empty;
        public string Formula         { get; set; } = string.Empty;

        /// <summary>Comma-separated display string of direct dependencies.</summary>
        public string DependsOn       { get; set; } = "—";
        public List<string> DependsOnList { get; set; } = new();

        /// <summary>Comma-separated display string of fields that reference this field.</summary>
        public string UsedBy          { get; set; } = "—";
        public List<string> UsedByList { get; set; } = new();

        public int  DepthLevel        { get; set; }   // longest path from a leaf to this node
        public int  DirectDepsCount   { get; set; }
        public int  UsedByCount       { get; set; }

        /// <summary>Base / Intermediate / Top-level / Standalone</summary>
        public string Kind            { get; set; } = "Standalone";
        public string KindColor       { get; set; } = "#888888";

        public bool   HasCircular     { get; set; }
        public string CircularPath    { get; set; } = string.Empty;
        public string CircularLabel   => HasCircular ? "Yes" : "";
        public string CircularColor   => HasCircular ? "#D83B01" : "Transparent";
    }
}
