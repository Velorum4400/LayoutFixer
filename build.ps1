$ErrorActionPreference = "Stop"

try {
    Write-Host "Closing running LayoutFixer..."
    Get-Process -Name "LayoutFixer" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    Start-Sleep -Milliseconds 300

    $projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    Set-Location $projectDir

    $dist = Join-Path $projectDir "dist"

    Write-Host "Building LayoutFixer..."

    & dotnet publish `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $dist `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true

    # Native programs such as dotnet do not reliably trigger
    # $ErrorActionPreference in Windows PowerShell 5.1.
    # Check their exit code explicitly.
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $exe = Join-Path $dist "LayoutFixer.exe"

    if (-not (Test-Path $exe)) {
        throw "Build reported success, but LayoutFixer.exe was not created."
    }

    Write-Host ""
    Write-Host "Build completed successfully."
    Write-Host "EXE: $exe"

    exit 0
}
catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "BUILD FAILED" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    exit 1
}
