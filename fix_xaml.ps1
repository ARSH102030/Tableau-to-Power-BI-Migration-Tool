$file = "C:\Users\ArshAnwar\OneDrive - Delphi Consulting\Desktop\Tableu-Power BI\TableauToPbi\MainWindow.xaml"
$c = Get-Content $file -Raw -Encoding UTF8

# Remove any remaining non-ASCII characters entirely
$c = [regex]::Replace($c, '[^\x00-\x7F]', '')

Set-Content $file -Value $c -Encoding UTF8 -NoNewline
Write-Host "Done - non-ASCII stripped"
