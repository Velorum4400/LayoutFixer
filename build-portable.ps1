$ErrorActionPreference = "Stop"

try {
    Write-Host "Closing running LayoutFixer..."
    Get-Process -Name "LayoutFixer" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    Start-Sleep -Milliseconds 300

    $projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    Set-Location $projectDir

    $projectFile = Join-Path $projectDir "LayoutFixer.csproj"
    [xml]$projectXml = Get-Content $projectFile
    $version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Could not read Version from LayoutFixer.csproj."
    }

    $portableRoot = Join-Path $projectDir "portable"
    $packageName = "LayoutFixer-Portable-$version"
    $packageDir = Join-Path $portableRoot $packageName
    $zipPath = Join-Path $portableRoot "$packageName.zip"

    if (Test-Path $packageDir) {
        Remove-Item $packageDir -Recurse -Force
    }

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $packageDir "data") -Force | Out-Null

    Write-Host "Building portable LayoutFixer v$version..."

    & dotnet publish `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $packageDir `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $exe = Join-Path $packageDir "LayoutFixer.exe"
    if (-not (Test-Path $exe)) {
        throw "Build reported success, but LayoutFixer.exe was not created."
    }

    # This marker makes the same executable use local portable data instead
    # of the installed version's %APPDATA% settings.
    Set-Content -Path (Join-Path $packageDir "portable.mode") -Value "LayoutFixer portable mode" -Encoding ASCII

    $readme = @"
LayoutFixer Portable $version

Run LayoutFixer.exe directly. No installation is required.

Portable settings are stored in the data folder next to LayoutFixer.exe.
Diagnostic logs (diagnostic.log and scanner_diagnostic.log) are stored there too.
The Start with Windows option is supported. It creates a separate
LayoutFixerPortable startup entry and does not overwrite the installed
LayoutFixer startup entry.

Important: if Start with Windows is enabled and you move this folder,
launch LayoutFixer.exe once from the new location so the startup path is updated.
"@
    Set-Content -Path (Join-Path $packageDir "README-PORTABLE.txt") -Value $readme -Encoding UTF8

    Write-Host "Creating portable ZIP..."
    Compress-Archive -Path $packageDir -DestinationPath $zipPath -CompressionLevel Optimal -Force

    if (-not (Test-Path $zipPath)) {
        throw "Portable ZIP was not created."
    }

    Write-Host ""
    Write-Host "Portable build completed successfully." -ForegroundColor Green
    Write-Host "Folder: $packageDir"
    Write-Host "ZIP:    $zipPath"

    exit 0
}
catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "PORTABLE BUILD FAILED" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    exit 1
}
