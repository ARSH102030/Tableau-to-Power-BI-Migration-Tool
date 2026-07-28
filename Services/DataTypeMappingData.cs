using System.Collections.Generic;
using TableauToPbi.Models;

namespace TableauToPbi.Services
{
    public static class DataTypeMappingData
    {
        public static List<DataTypeMapping> GetAll() => new()
        {
            new() { TableauType = "string",   PowerBiType = "Text",           Notes = "Direct equivalent" },
            new() { TableauType = "integer",  PowerBiType = "Whole Number",   Notes = "Direct equivalent" },
            new() { TableauType = "real",     PowerBiType = "Decimal Number", Notes = "64-bit floating point in both" },
            new() { TableauType = "float",    PowerBiType = "Decimal Number", Notes = "Same as 'real'" },
            new() { TableauType = "boolean",  PowerBiType = "True/False",     Notes = "Direct equivalent" },
            new() { TableauType = "date",     PowerBiType = "Date",           Notes = "Date only (no time component)" },
            new() { TableauType = "datetime", PowerBiType = "Date/Time",      Notes = "Includes time component" },
            new() { TableauType = "spatial",  PowerBiType = "Text (WKT) or custom column", Notes = "Power BI does not natively support Tableau spatial type; use Well-Known Text (WKT) strings or ArcGIS visual" },
            new() { TableauType = "measure",  PowerBiType = "Measure (DAX)",  Notes = "Tableau measures → DAX calculated measures in the model" },
            new() { TableauType = "dimension", PowerBiType = "Calculated Column or Column", Notes = "Tableau dimensions → columns or calculated columns in Power BI" },
        };
    }
}
