using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using TableauToPbi.Models;

namespace TableauToPbi.Services
{
    public static class TableauFileParser
    {
        public static TableauWorkbook Parse(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (extension == ".twbx")
                return ParseTwbx(filePath);
            else if (extension == ".twb")
                return ParseTwb(filePath);
            else
                throw new InvalidOperationException($"Unsupported file type: {extension}");
        }

        private static TableauWorkbook ParseTwbx(string filePath)
        {
            // .twbx is a ZIP file — extract the .twb XML inside it
            using var zip = ZipFile.OpenRead(filePath);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith(".twb", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = entry.Open();
                    var doc = XDocument.Load(stream);
                    var wb = ParseDocument(doc);
                    wb.FilePath = filePath;
                    wb.Name = Path.GetFileNameWithoutExtension(filePath);
                    return wb;
                }
            }
            throw new InvalidOperationException("No .twb file found inside the .twbx package.");
        }

        private static TableauWorkbook ParseTwb(string filePath)
        {
            var doc = XDocument.Load(filePath);
            var wb = ParseDocument(doc);
            wb.FilePath = filePath;
            wb.Name = Path.GetFileNameWithoutExtension(filePath);
            return wb;
        }

        private static TableauWorkbook ParseDocument(XDocument doc)
        {
            var wb = new TableauWorkbook();
            var root = doc.Root;
            if (root == null) return wb;

            wb.SourceBuild = (string?)root.Attribute("source-build") ?? "";

            // Data sources
            var datasourcesElem = root.Element("datasources");
            if (datasourcesElem != null)
            {
                foreach (var dsElem in datasourcesElem.Elements("datasource"))
                {
                    var ds = ParseDataSource(dsElem);
                    // Skip internal Tableau parameters datasource
                    if (ds.Name == "Parameters" || ds.Caption == "Parameters") continue;
                    wb.DataSources.Add(ds);
                }
            }

            // Worksheets
            var worksheetsElem = root.Element("worksheets");
            if (worksheetsElem != null)
            {
                foreach (var ws in worksheetsElem.Elements("worksheet"))
                {
                    var name = (string?)ws.Attribute("name") ?? "";
                    if (!string.IsNullOrWhiteSpace(name))
                        wb.WorksheetNames.Add(name);
                }
            }

            // Dashboards
            var dashboardsElem = root.Element("dashboards");
            if (dashboardsElem != null)
            {
                foreach (var db in dashboardsElem.Elements("dashboard"))
                {
                    var name = (string?)db.Attribute("name") ?? "";
                    if (!string.IsNullOrWhiteSpace(name))
                        wb.DashboardNames.Add(name);
                }
            }

            return wb;
        }

        private static TableauDataSource ParseDataSource(XElement dsElem)
        {
            var ds = new TableauDataSource
            {
                Name = (string?)dsElem.Attribute("name") ?? "",
                Caption = (string?)dsElem.Attribute("caption") ?? ""
            };

            // Connection type
            var connElem = dsElem.Element("connection");
            if (connElem != null)
                ds.ConnectionType = (string?)connElem.Attribute("class") ?? "";

            // Fields (columns)
            foreach (var colElem in dsElem.Elements("column"))
            {
                var field = ParseField(colElem);
                if (field != null)
                    ds.Fields.Add(field);
            }

            // Also check inside <aliases> (some versions put columns there)
            var columnsGroupElem = dsElem.Element("column-instance");
            // And nested datasource folders
            foreach (var folderElem in dsElem.Elements("folder"))
            {
                foreach (var folderItemElem in folderElem.Elements("folder-item"))
                {
                    // folder-item references columns by name — already parsed above
                }
            }

            return ds;
        }

        private static TableauField? ParseField(XElement colElem)
        {
            var name = (string?)colElem.Attribute("name") ?? "";
            if (string.IsNullOrWhiteSpace(name)) return null;

            // Skip internal Tableau auto-generated system columns
            if (name.StartsWith("[Number of Records]", StringComparison.OrdinalIgnoreCase))
            {
                // still include, it's useful
            }

            var field = new TableauField
            {
                Name = name,
                Caption = (string?)colElem.Attribute("caption") ?? "",
                DataType = (string?)colElem.Attribute("datatype") ?? "",
                Role = (string?)colElem.Attribute("role") ?? "",
                FieldType = (string?)colElem.Attribute("type") ?? ""
            };

            // Check for calculation child element
            var calcElem = colElem.Element("calculation");
            if (calcElem != null)
            {
                field.Formula = (string?)calcElem.Attribute("formula") ?? "";
                if (string.IsNullOrWhiteSpace(field.Formula))
                    field.Formula = null;
            }

            return field;
        }
    }
}
