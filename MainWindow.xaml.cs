using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TableauToPbi.Models;
using TableauToPbi.Services;

namespace TableauToPbi
{
    public partial class MainWindow : Window
    {
        // ─── State ────────────────────────────────────────────────────────────
        private TableauWorkbook? _workbook;
        private readonly TableauToDaxConverter _converter = new();
        private List<FunctionReference> _allFunctions = new();
        private List<CalculatedFieldRow> _allCalcRows = new();
        private string? _pendingFilePath;

        // PBI Connection state
        private List<PbiInstance> _pbiInstances = new();
        private string? _pbiConnectionString;

        // Data values state
        private DataTable? _csvData;
        private string? _pendingCsvPath;

        // Dependency analysis state
        private List<FieldDependencyRow> _allDepRows = new();
        private List<FieldDependencyRow> _filteredDepRows = new();

        // LOD examples
        private static readonly (string Label, string Formula)[] LodExamples =
        {
            ("FIXED — Sales per Customer (grand total level)",
             "{ FIXED [Customer Name] : SUM([Sales]) }"),

            ("FIXED — Category total (ignore all other dims)",
             "{ FIXED [Category] : SUM([Profit]) }"),

            ("FIXED — No dimension (absolute grand total)",
             "{ FIXED : SUM([Sales]) }"),

            ("INCLUDE — Sales by Sub-Category (add dim not in view)",
             "{ INCLUDE [Sub-Category] : SUM([Sales]) }"),

            ("EXCLUDE — Remove Region from context",
             "{ EXCLUDE [Region] : AVG([Discount]) }"),

            ("FIXED — % of Total pattern",
             "SUM([Sales]) / { FIXED : SUM([Sales]) }"),

            ("FIXED — Days since first purchase",
             "DATEDIFF('day', { FIXED [Customer Name] : MIN([Order Date]) }, [Order Date])"),
        };

        // ─── Constructor ──────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();
            LoadStaticData();
        }

        // ─── Static data initialisation ───────────────────────────────────────
        private void LoadStaticData()
        {
            // Function Reference tab
            _allFunctions = FunctionReferenceData.GetAll();
            var categories = new List<string> { "All" };
            categories.AddRange(_allFunctions.Select(f => f.Category).Distinct().OrderBy(c => c));
            CmbFunctionCategory.ItemsSource = categories;
            CmbFunctionCategory.SelectedIndex = 0;
            FunctionRefGrid.ItemsSource = _allFunctions;

            // Data Types tab
            DataTypeGrid.ItemsSource = DataTypeMappingData.GetAll();

            // LOD Examples
            CmbLodExamples.ItemsSource = LodExamples.Select(e => e.Label).ToList();

            // Dependency filter options
            CmbDepFilter.ItemsSource = new List<string>
                { "All", "Base", "Intermediate", "Top-level", "Standalone", "Circular Only" };
            CmbDepFilter.SelectedIndex = 0;
        }

        // ─── File Browse & Load ───────────────────────────────────────────────
        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open Tableau Workbook",
                Filter = "Tableau Files|*.twb;*.twbx|Tableau Workbook (*.twb)|*.twb|Tableau Packaged Workbook (*.twbx)|*.twbx|All Files|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                _pendingFilePath = dlg.FileName;
                TxtFilePath.Text = dlg.FileName;
                BtnLoad.IsEnabled = true;
            }
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pendingFilePath)) return;
            LoadFile(_pendingFilePath);
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 0) return;

            string file = files[0];
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".twb" && ext != ".twbx")
            {
                MessageBox.Show("Please drop a .twb or .twbx file.", "Invalid File",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _pendingFilePath = file;
            TxtFilePath.Text = file;
            BtnLoad.IsEnabled = true;
            LoadFile(file);
        }

        private void LoadFile(string filePath)
        {
            try
            {
                LoadingIndicator.Visibility = Visibility.Visible;
                FileLoadedBadge.Visibility = Visibility.Collapsed;
                BtnLoad.IsEnabled = false;

                _workbook = TableauFileParser.Parse(filePath);

                PopulateFileOverviewTab();
                PopulateCalculatedFieldsTab();
                PopulateConverterFileDropdown();

                // Enable dependency tab
                BtnAnalyzeDeps.IsEnabled = true;
                BtnExportDeps.IsEnabled = false;
                _allDepRows.Clear();
                _filteredDepRows.Clear();
                DepGrid.ItemsSource = null;
                TxtDepDetailTitle.Text = "Click 'Analyze Dependencies' to start";
                TxtDepFormula.Text = "";
                TxtDepUpstream.Text = "";
                TxtDepDownstream.Text = "";

                // Show success badge
                TxtFileLoadedBadge.Text = $"✔ {_workbook.CalculatedFieldCount} calculated fields";
                FileLoadedBadge.Visibility = Visibility.Visible;
                LoadingIndicator.Visibility = Visibility.Collapsed;

                // Switch to File Overview tab
                MainTabs.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                BtnLoad.IsEnabled = true;
                MessageBox.Show($"Failed to load file:\n\n{ex.Message}", "Load Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── Tab 1: File Overview ─────────────────────────────────────────────
        private void PopulateFileOverviewTab()
        {
            if (_workbook == null) return;

            DropZone.Visibility = Visibility.Collapsed;
            SummaryPanel.Visibility = Visibility.Visible;
            DataSourcesPanel.Visibility = Visibility.Visible;
            WorksheetsPanel.Visibility = Visibility.Visible;

            TxtWorkbookName.Text = _workbook.Name;
            TxtCalcFieldCount.Text = _workbook.CalculatedFieldCount.ToString();
            TxtWorksheetCount.Text = _workbook.WorksheetCount.ToString();
            TxtDashboardCount.Text = _workbook.DashboardCount.ToString();

            DataSourcesGrid.ItemsSource = _workbook.DataSources;

            // Worksheet + Dashboard tags
            WorksheetTagsPanel.Children.Clear();
            foreach (var ws in _workbook.WorksheetNames)
                WorksheetTagsPanel.Children.Add(MakeTag(ws, "#EBF3FB", "#0078D4"));
            foreach (var db in _workbook.DashboardNames)
                WorksheetTagsPanel.Children.Add(MakeTag(db, "#F0FAF0", "#107C10", "📊 "));
        }

        private static Border MakeTag(string text, string bgHex, string fgHex, string prefix = "")
        {
            return new Border
            {
                Background = (SolidColorBrush)new BrushConverter().ConvertFrom(bgHex)!,
                BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(fgHex)!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = prefix + text,
                    FontSize = 11,
                    Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(fgHex)!
                }
            };
        }

        // ─── Tab 2: Formula Converter ─────────────────────────────────────────
        private void PopulateConverterFileDropdown()
        {
            if (_workbook == null) return;
            var calcFields = _workbook.DataSources
                .SelectMany(ds => ds.Fields)
                .Where(f => f.IsCalculated)
                .ToList();

            CmbFileFields.ItemsSource = calcFields;
            CmbFileFields.IsEnabled = calcFields.Count > 0;
            BtnLoadField.IsEnabled = calcFields.Count > 0;
        }

        private void CmbFileFields_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Preview only — load on button click
        }

        private void BtnLoadField_Click(object sender, RoutedEventArgs e)
        {
            if (CmbFileFields.SelectedItem is TableauField field && field.Formula != null)
                TxtTableauFormula.Text = field.Formula;
        }

        private void BtnConvert_Click(object sender, RoutedEventArgs e)
        {
            string tableau = TxtTableauFormula.Text.Trim();
            if (string.IsNullOrWhiteSpace(tableau))
            {
                TxtDaxOutput.Text = string.Empty;
                return;
            }

            var result = _converter.Convert(tableau);
            TxtDaxOutput.Text = result.DaxExpression;

            // Status badge
            StatusBadge.Visibility = Visibility.Visible;
            StatusBadgeText.Text = result.StatusLabel;
            StatusBadge.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(result.StatusColor));

            // Warnings
            var allMessages = new List<string>();
            allMessages.AddRange(result.Warnings.Select(w => "⚠ " + w));
            allMessages.AddRange(result.Notes.Select(n => "ℹ " + n));

            if (allMessages.Count > 0)
            {
                TxtWarnings.Text = string.Join("\n\n", allMessages);
                WarningsPanelBorder.Visibility = Visibility.Visible;
                WarningsPanelBorder.BorderBrush = result.Status == ConversionStatus.ManualRequired
                    ? new SolidColorBrush(Color.FromRgb(0xD8, 0x3B, 0x01))
                    : new SolidColorBrush(Color.FromRgb(0xF2, 0x8E, 0x2B));
                WarningsPanelBorder.Background = result.Status == ConversionStatus.ManualRequired
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xEC))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xF0));
            }
            else
            {
                WarningsPanelBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnConverterClear_Click(object sender, RoutedEventArgs e)
        {
            TxtTableauFormula.Clear();
            TxtDaxOutput.Clear();
            StatusBadge.Visibility = Visibility.Collapsed;
            WarningsPanelBorder.Visibility = Visibility.Collapsed;
        }

        private void BtnCopyDax_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtDaxOutput.Text))
                Clipboard.SetText(TxtDaxOutput.Text);
        }

        // ─── Tab 3: Calculated Fields ─────────────────────────────────────────
        private void PopulateCalculatedFieldsTab()
        {
            if (_workbook == null) return;

            _allCalcRows = _workbook.DataSources
                .SelectMany(ds => ds.Fields
                    .Where(f => f.IsCalculated)
                    .Select(f => new CalculatedFieldRow
                    {
                        FieldName = f.DisplayName,
                        DataSource = ds.DisplayName,
                        TableauFormula = f.Formula ?? "",
                        DaxFormula = f.DaxFormula ?? "—",
                        Status = f.ConversionStatus == ConversionStatus.NotConverted ? "Not converted" : f.StatusLabel,
                        StatusColor = f.ConversionStatus == ConversionStatus.NotConverted ? "#888888" : GetStatusColor(f.ConversionStatus),
                        Notes = string.Join("; ", f.ConversionWarnings.Concat(f.ConversionNotes)),
                        SourceField = f
                    }))
                .ToList();

            if (_allCalcRows.Count > 0)
            {
                NoFileMessage.Visibility = Visibility.Collapsed;
                CalcFieldsGrid.Visibility = Visibility.Visible;
                BtnConvertAll.IsEnabled = true;
                BtnExportCsv.IsEnabled = true;
            }

            CalcFieldsGrid.ItemsSource = _allCalcRows;
        }

        private static string GetStatusColor(ConversionStatus status) => status switch
        {
            ConversionStatus.Success => "#107C10",
            ConversionStatus.PartialConversion => "#C19A00",
            ConversionStatus.ManualRequired => "#D83B01",
            _ => "#888888"
        };

        private void CalcFieldsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CalcFieldsGrid.SelectedItem is CalculatedFieldRow row && row.SourceField != null)
            {
                // Load into Formula Converter tab
                TxtTableauFormula.Text = row.TableauFormula;
                if (row.SourceField.DaxFormula != null)
                    TxtDaxOutput.Text = row.SourceField.DaxFormula;
            }
        }

        private void BtnConvertAll_Click(object sender, RoutedEventArgs e)
        {
            if (_workbook == null) return;

            int converted = 0, partial = 0, manual = 0;

            foreach (var row in _allCalcRows)
            {
                if (row.SourceField == null || string.IsNullOrWhiteSpace(row.TableauFormula)) continue;

                var result = _converter.Convert(row.TableauFormula);
                row.SourceField.DaxFormula = result.DaxExpression;
                row.SourceField.ConversionStatus = result.Status;
                row.SourceField.ConversionWarnings = result.Warnings;
                row.SourceField.ConversionNotes = result.Notes;

                row.DaxFormula = result.DaxExpression;
                row.Status = result.StatusLabel;
                row.StatusColor = result.StatusColor;
                row.Notes = string.Join("; ", result.Warnings.Concat(result.Notes));

                switch (result.Status)
                {
                    case ConversionStatus.Success: converted++; break;
                    case ConversionStatus.PartialConversion: partial++; break;
                    case ConversionStatus.ManualRequired: manual++; break;
                }
            }

            // Refresh grid
            CalcFieldsGrid.ItemsSource = null;
            CalcFieldsGrid.ItemsSource = _allCalcRows;

            MessageBox.Show(
                $"Conversion complete!\n\n" +
                $"✔  Converted:         {converted}\n" +
                $"⚠  Partial:           {partial}\n" +
                $"✖  Manual required:   {manual}",
                "Conversion Results",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void TxtCalcSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = TxtCalcSearch.Text.ToLowerInvariant();
            var filtered = string.IsNullOrWhiteSpace(q)
                ? _allCalcRows
                : _allCalcRows.Where(r =>
                    r.FieldName.ToLower().Contains(q) ||
                    r.TableauFormula.ToLower().Contains(q) ||
                    r.DaxFormula.ToLower().Contains(q)).ToList();

            CalcFieldsGrid.ItemsSource = filtered;
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export Calculated Fields to CSV",
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"{_workbook?.Name ?? "tableau"}_calculated_fields.csv"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Field Name,Data Source,Tableau Formula,DAX Expression,Status,Notes");

                foreach (var row in _allCalcRows)
                {
                    sb.AppendLine($"{CsvEscape(row.FieldName)},{CsvEscape(row.DataSource)}," +
                                  $"{CsvEscape(row.TableauFormula)},{CsvEscape(row.DaxFormula)}," +
                                  $"{CsvEscape(row.Status)},{CsvEscape(row.Notes)}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"Exported to:\n{dlg.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string CsvEscape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        // ─── Tab 4: Function Reference ────────────────────────────────────────
        private void CmbFunctionCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFunctionFilter();
        }

        private void TxtFunctionSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFunctionFilter();
        }

        private void ApplyFunctionFilter()
        {
            string category = CmbFunctionCategory.SelectedItem as string ?? "All";
            string search = TxtFunctionSearch.Text.ToLowerInvariant();

            var filtered = _allFunctions.AsEnumerable();

            if (category != "All")
                filtered = filtered.Where(f => f.Category == category);

            if (!string.IsNullOrWhiteSpace(search))
                filtered = filtered.Where(f =>
                    f.TableauFunction.ToLower().Contains(search) ||
                    f.DaxEquivalent.ToLower().Contains(search) ||
                    f.Notes.ToLower().Contains(search));

            FunctionRefGrid.ItemsSource = filtered.ToList();
        }

        // ─── Tab 6: LOD Expressions ───────────────────────────────────────────
        private void CmbLodExamples_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = CmbLodExamples.SelectedIndex;
            if (idx >= 0 && idx < LodExamples.Length)
                TxtLodTableau.Text = LodExamples[idx].Formula;
        }

        private void BtnLodConvert_Click(object sender, RoutedEventArgs e)
        {
            string tableau = TxtLodTableau.Text.Trim();
            if (string.IsNullOrWhiteSpace(tableau)) return;

            var result = _converter.Convert(tableau);
            TxtLodDax.Text = result.DaxExpression;
        }

        private void BtnLodClear_Click(object sender, RoutedEventArgs e)
        {
            TxtLodTableau.Clear();
            TxtLodDax.Clear();
            CmbLodExamples.SelectedIndex = -1;
        }

        private void BtnCopyLodDax_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtLodDax.Text))
                Clipboard.SetText(TxtLodDax.Text);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PBI CONNECTION
        // ═══════════════════════════════════════════════════════════════════════

        private void BtnRefreshPbi_Click(object sender, RoutedEventArgs e)
        {
            _pbiInstances = PbiDiscoveryService.DiscoverInstances();
            CmbPbiInstances.ItemsSource = _pbiInstances;
            if (_pbiInstances.Count > 0)
                CmbPbiInstances.SelectedIndex = 0;
            else
                MessageBox.Show("No Power BI Desktop instances found.\n\nMake sure a report is open in Power BI Desktop.",
                    "PBI Discovery", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnConnectPbi_Click(object sender, RoutedEventArgs e)
        {
            if (CmbPbiInstances.SelectedItem is not PbiInstance instance)
            {
                PbiErrorBadge.Visibility = Visibility.Visible;
                TxtPbiErrorBadge.Text = "✖ Select an instance first";
                PbiConnectedBadge.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                // Validate connection by opening and closing TOM server
                var server = new Microsoft.AnalysisServices.Tabular.Server();
                server.Connect(instance.ConnectionString);
                string dbName = server.Databases[0].Name;
                server.Disconnect();

                _pbiConnectionString = instance.ConnectionString;
                PbiConnectedBadge.Visibility = Visibility.Visible;
                TxtPbiConnectedBadge.Text = $"✔ Connected — {instance.ReportName}";
                PbiErrorBadge.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                _pbiConnectionString = null;
                PbiErrorBadge.Visibility = Visibility.Visible;
                TxtPbiErrorBadge.Text = $"✖ {ex.Message}";
                PbiConnectedBadge.Visibility = Visibility.Collapsed;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 7 — SCHEMA CHECK
        // ═══════════════════════════════════════════════════════════════════════

        private void BtnRunSchemaCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_workbook == null || string.IsNullOrEmpty(_pbiConnectionString))
            {
                SchemaNoFileBadge.Visibility = Visibility.Visible;
                return;
            }
            SchemaNoFileBadge.Visibility = Visibility.Collapsed;

            var rows = SchemaComparisonService.Compare(_workbook, _pbiConnectionString);
            SchemaGrid.ItemsSource = rows;

            int found    = rows.Count(r => r.Status.StartsWith("✔"));
            int missing  = rows.Count(r => r.Status.Contains("Missing"));
            int mismatch = rows.Count(r => r.Status.Contains("Mismatch") || r.Status.Contains("Type"));
            TxtSchemaStats.Text = $"{rows.Count} fields — ✔ {found} found  ✖ {missing} missing  ⚠ {mismatch} type mismatches";
        }

        private void BtnExportSchemaCheck_Click(object sender, RoutedEventArgs e)
        {
            if (SchemaGrid.ItemsSource is not IEnumerable<SchemaComparisonRow> rows) return;
            ExportToCsv(rows.Select(r => new[]
            {
                r.FieldName, r.FieldType, r.TableauType, r.PbiObject, r.PbiType, r.Status, r.Notes
            }), new[] { "Field Name", "Type", "Tableau Type", "PBI Object", "PBI Type", "Status", "Notes" },
            "schema_check.csv");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 8 — DAX COMPARISON
        // ═══════════════════════════════════════════════════════════════════════

        private void BtnRunDaxComparison_Click(object sender, RoutedEventArgs e)
        {
            if (_workbook == null || string.IsNullOrEmpty(_pbiConnectionString))
            {
                MessageBox.Show("Load a Tableau file and connect to Power BI first.",
                    "DAX Comparison", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rows = DaxComparisonService.Compare(_workbook, _pbiConnectionString, _converter);
            DaxCompGrid.ItemsSource = rows;

            int match    = rows.Count(r => r.DiffStatus.StartsWith("✔"));
            int partial  = rows.Count(r => r.DiffStatus.Contains("Partial"));
            int mismatch = rows.Count(r => r.DiffStatus.Contains("Mismatch"));
            int notFound = rows.Count(r => r.DiffStatus.Contains("Not in"));
            TxtDaxCompStats.Text = $"{rows.Count} measures — ✔ {match} match  ⚠ {partial} partial  ✖ {mismatch} mismatch  — {notFound} not in PBI";
        }

        private void BtnExportDaxComparison_Click(object sender, RoutedEventArgs e)
        {
            if (DaxCompGrid.ItemsSource is not IEnumerable<DaxComparisonRow> rows) return;
            ExportToCsv(rows.Select(r => new[]
            {
                r.MeasureName, r.DataSource, r.TableauFormula, r.ConvertedDax, r.PbiDax, r.DiffStatus, r.Notes
            }), new[] { "Measure Name", "Data Source", "Tableau Formula", "Converted DAX", "PBI DAX", "Status", "Notes" },
            "dax_comparison.csv");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 9 — DATA VALUES COMPARISON
        // ═══════════════════════════════════════════════════════════════════════

        private void BtnBrowseCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open Tableau Export CSV",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                _pendingCsvPath = dlg.FileName;
                TxtCsvPath.Text = dlg.FileName;
                BtnLoadCsv.IsEnabled = true;
                CsvLoadedBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnLoadCsv_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pendingCsvPath)) return;
            try
            {
                _csvData = DataValuesComparisonService.LoadCsv(_pendingCsvPath);
                var cols = _csvData.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
                CmbDimensionCol.ItemsSource = cols;
                CmbValueCol.ItemsSource = cols;
                if (cols.Count > 0) CmbDimensionCol.SelectedIndex = 0;
                if (cols.Count > 1) CmbValueCol.SelectedIndex = 1;

                CsvLoadedBadge.Visibility = Visibility.Visible;
                TxtCsvLoadedBadge.Text = $"✔ {_csvData.Rows.Count} rows loaded";
                BtnLoadCsv.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load CSV:\n{ex.Message}", "CSV Load Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRunDataComparison_Click(object sender, RoutedEventArgs e)
        {
            if (_csvData == null)
            {
                MessageBox.Show("Load a CSV file first.", "Data Comparison",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(_pbiConnectionString))
            {
                MessageBox.Show("Connect to Power BI Desktop first.", "Data Comparison",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string daxQuery = TxtDaxQueryForValues.Text.Trim();
            if (string.IsNullOrWhiteSpace(daxQuery) || daxQuery.StartsWith("EVALUATE SUMMARIZECOLUMNS(...)"))
            {
                MessageBox.Show("Enter a valid DAX query (e.g. EVALUATE SUMMARIZECOLUMNS(...)).",
                    "Data Comparison", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(TxtThreshold.Text, out double thresholdPct))
                thresholdPct = 1;

            var opts = new DataComparisonOptions
            {
                DaxQuery        = daxQuery,
                DimensionColumn = CmbDimensionCol.SelectedItem?.ToString() ?? "",
                ValueColumn     = CmbValueCol.SelectedItem?.ToString() ?? "",
                Threshold       = thresholdPct / 100.0
            };

            try
            {
                var pbiData = DataValuesComparisonService.QueryPbi(_pbiConnectionString, daxQuery);
                var rows    = DataValuesComparisonService.Compare(_csvData, pbiData, opts);
                DataCompGrid.ItemsSource = rows;

                int match    = rows.Count(r => r.Status.StartsWith("✔"));
                int mismatch = rows.Count(r => r.Status.Contains("Variance") || r.Status.Contains("Mismatch"));
                int missing  = rows.Count(r => r.Status.Contains("Missing"));
                TxtDataCompStats.Text = $"{rows.Count} rows — ✔ {match} match  ✖ {mismatch} variance  — {missing} missing in PBI";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Comparison failed:\n{ex.Message}", "Data Comparison Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportDataComparison_Click(object sender, RoutedEventArgs e)
        {
            if (DataCompGrid.ItemsSource is not IEnumerable<DataValueRow> rows) return;
            ExportToCsv(rows.Select(r => new[]
            {
                r.Dimension, r.TableauValue, r.PbiValue,
                r.Variance?.ToString("F4") ?? "", r.VariancePct, r.Status
            }), new[] { "Dimension", "Tableau Value", "PBI Value", "Abs Variance", "Variance %", "Status" },
            "data_comparison.csv");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAB 10 — BEST PRACTICES
        // ═══════════════════════════════════════════════════════════════════════

        private void BtnRunBestPractices_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pbiConnectionString))
            {
                MessageBox.Show("Connect to Power BI Desktop first.",
                    "Best Practices", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rows = BestPracticesService.Analyze(_pbiConnectionString);
            BpGrid.ItemsSource = rows;

            int errors   = rows.Count(r => r.Severity == "Error");
            int warnings = rows.Count(r => r.Severity == "Warning");
            int info     = rows.Count(r => r.Severity == "Info");
            TxtBpStats.Text = $"{rows.Count} findings — ✖ {errors} errors  ⚠ {warnings} warnings  ℹ {info} info";
        }

        private void BtnExportBestPractices_Click(object sender, RoutedEventArgs e)
        {
            if (BpGrid.ItemsSource is not IEnumerable<BestPracticeRow> rows) return;
            ExportToCsv(rows.Select(r => new[]
            {
                r.Severity, r.Category, r.Object, r.Rule, r.Recommendation
            }), new[] { "Severity", "Category", "Object", "Rule", "Recommendation" },
            "best_practices.csv");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // FIELD DEPENDENCIES TAB
        // ═══════════════════════════════════════════════════════════════════════

        private void BtnAnalyzeDeps_Click(object sender, RoutedEventArgs e)
        {
            if (_workbook == null) return;

            try
            {
                _allDepRows = DependencyAnalysisService.Analyze(_workbook);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dependency analysis failed:\n\n{ex.Message}", "Analysis Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ApplyDepFilter();
            BtnExportDeps.IsEnabled = _allDepRows.Count > 0;

            int total    = _allDepRows.Count;
            int circular = _allDepRows.Count(r => r.HasCircular);
            int bases    = _allDepRows.Count(r => r.Kind == "Base");
            int topLevel = _allDepRows.Count(r => r.Kind == "Top-level");
            int inter    = _allDepRows.Count(r => r.Kind == "Intermediate");
            int maxDepth = _allDepRows.Count > 0 ? _allDepRows.Max(r => r.DepthLevel) : 0;

            TxtDepStats.Text =
                $"{total} fields  |  Max depth: {maxDepth}  |  " +
                $"Base: {bases}  Intermediate: {inter}  Top-level: {topLevel}" +
                (circular > 0 ? $"  |  Circular: {circular}" : "");

            // Clear detail panel
            TxtDepDetailTitle.Text = "Select a row to see the full dependency chain";
            TxtDepFormula.Text = "";
            TxtDepUpstream.Text = "";
            TxtDepDownstream.Text = "";
        }

        private void ApplyDepFilter()
        {
            string filter  = CmbDepFilter.SelectedItem?.ToString() ?? "All";
            string search  = TxtDepSearch.Text.Trim();

            _filteredDepRows = _allDepRows.Where(r =>
            {
                bool kindOk = filter switch
                {
                    "Base"          => r.Kind == "Base",
                    "Intermediate"  => r.Kind == "Intermediate",
                    "Top-level"     => r.Kind == "Top-level",
                    "Standalone"    => r.Kind == "Standalone",
                    "Circular Only" => r.HasCircular,
                    _               => true
                };

                bool searchOk = string.IsNullOrWhiteSpace(search)
                    || r.FieldName.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || r.DependsOn.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || r.UsedBy.Contains(search, StringComparison.OrdinalIgnoreCase);

                return kindOk && searchOk;
            }).ToList();

            DepGrid.ItemsSource = _filteredDepRows;
        }

        private void CmbDepFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ApplyDepFilter();

        private void TxtDepSearch_TextChanged(object sender, TextChangedEventArgs e)
            => ApplyDepFilter();

        private void DepGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DepGrid.SelectedItem is not FieldDependencyRow row) return;

            try
            {
                // Title
                TxtDepDetailTitle.Text = $"{row.FieldName}  [{row.Kind}  |  Depth {row.DepthLevel}  |  " +
                    $"Uses {row.DirectDepsCount}  |  Used by {row.UsedByCount}]" +
                    (row.HasCircular ? "  *** CIRCULAR ***" : "");

                // Formula
                TxtDepFormula.Text = row.Formula;

                // Upstream chain
                TxtDepUpstream.Text = DependencyAnalysisService.BuildUpstreamChain(row.FieldName, _allDepRows);

                // Downstream chain
                TxtDepDownstream.Text = DependencyAnalysisService.BuildDownstreamChain(row.FieldName, _allDepRows);
            }
            catch (Exception ex)
            {
                TxtDepUpstream.Text   = $"Error building chain: {ex.Message}";
                TxtDepDownstream.Text = "";
            }
        }

        private void BtnExportDeps_Click(object sender, RoutedEventArgs e)
        {
            if (_allDepRows.Count == 0) return;
            ExportToCsv(
                _allDepRows.Select(r => new[]
                {
                    r.FieldName, r.DataSource, r.Kind,
                    r.DepthLevel.ToString(), r.DirectDepsCount.ToString(), r.UsedByCount.ToString(),
                    r.DependsOn, r.UsedBy,
                    r.HasCircular ? "Yes" : "", r.CircularPath
                }),
                new[] { "Field Name", "Data Source", "Kind", "Depth", "Uses (#)", "Used By (#)",
                        "Depends On", "Used By", "Circular?", "Circular Path" },
                "field_dependencies.csv");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SHARED EXPORT HELPER
        // ═══════════════════════════════════════════════════════════════════════

        private static void ExportToCsv(IEnumerable<string[]> rows, string[] headers, string defaultName)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export CSV",
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = defaultName
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));
            foreach (var row in rows)
                sb.AppendLine(string.Join(",", row.Select(CsvEscape)));

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"Exported to:\n{dlg.FileName}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
