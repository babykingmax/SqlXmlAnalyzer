Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "         SqlXmlAnalyzer Local Publish Script" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Restoring dependencies..." -ForegroundColor Yellow
dotnet restore

Write-Host ""
Write-Host "Publishing application as single file (win-x64, Self-Contained)..." -ForegroundColor Yellow
dotnet publish SqlXmlAnalyzer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -o .\publish\win-x64

Write-Host ""
Write-Host "Publish complete! Output directory:" -ForegroundColor Green
$outDir = Join-Path $PWD "publish\win-x64"
Write-Host $outDir

Write-Host ""
Write-Host "Included files:" -ForegroundColor Green
Get-ChildItem -Path ".\publish\win-x64\SqlXmlAnalyzer.exe" | Select-Object Name, @{Name="Size(MB)";Expression={"{0:N2}" -f ($_.Length / 1MB)}} | Format-Table -AutoSize

Write-Host ""
Write-Host "Please share .\publish\win-x64\SqlXmlAnalyzer.exe with users. It can run directly on Windows without .NET 8.0 installed!" -ForegroundColor Cyan
