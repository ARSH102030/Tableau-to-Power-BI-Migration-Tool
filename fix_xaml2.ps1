$file = "C:\Users\ArshAnwar\OneDrive - Delphi Consulting\Desktop\Tableu-Power BI\TableauToPbi\MainWindow.xaml"
$c = Get-Content $file -Raw -Encoding UTF8

# Fix title (arrow stripped leaving double space)
$c = $c -replace 'Tableau  Power BI Converter', 'Tableau to Power BI Converter'
$c = $c -replace '"Tableau  Power BI Converter"', '"Tableau to Power BI Converter"'

# Fix subtitle bullet points
$c = $c -replace 'Formula conversion  Calculated fields  LOD expressions  Function reference',
                  'Formula conversion | Calculated fields | LOD expressions | Function reference'

# Fix tab headers - trim leading spaces from stripped emoji
$c = $c -replace 'Header="  File Overview"',    'Header="File Overview"'
$c = $c -replace 'Header="  Formula Converter"', 'Header="Formula Converter"'
$c = $c -replace 'Header="  Calculated Fields"', 'Header="Calculated Fields"'
$c = $c -replace 'Header="  Function Reference"','Header="Function Reference"'
$c = $c -replace 'Header="  Data Types"',        'Header="Data Types"'
$c = $c -replace 'Header="  LOD Expressions"',   'Header="LOD Expressions"'
$c = $c -replace 'Header="  Schema Check"',      'Header="Schema Check"'
$c = $c -replace 'Header="  DAX Comparison"',    'Header="DAX Comparison"'
$c = $c -replace 'Header="  Data Values"',       'Header="Data Values"'
$c = $c -replace 'Header="  Best Practices"',    'Header="Best Practices"'

# Fix toolbar text
$c = $c -replace '" Power BI Validation"', '"Power BI Validation"'

# Fix button text with leading spaces from stripped arrows
$c = $c -replace '"  Run Schema Check"',   '"Run Schema Check"'
$c = $c -replace '"  Run DAX Comparison"', '"Run DAX Comparison"'
$c = $c -replace '"  Compare Values"',     '"Compare Values"'
$c = $c -replace '"  Analyze Model"',      '"Analyze Model"'
$c = $c -replace '"> Refresh"',            '"Refresh"'

# Status badge text
$c = $c -replace '"  Loaded"',    '"Loaded"'
$c = $c -replace '"  Connected"', '"Connected"'
$c = $c -replace '" Connected',   '"Connected'

# Fix Convert button in formula tab
$c = $c -replace 'Content="  Convert"', 'Content="Convert"'
$c = $c -replace 'Content=" Convert"',  'Content="Convert"'

Set-Content $file -Value $c -Encoding UTF8 -NoNewline
Write-Host "Done"
