$ErrorActionPreference = "Stop"

try {
    $projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    Set-Location $projectDir

    Write-Host "Building LayoutFixer..."
    & dotnet build .\LayoutFixer.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "Build completed successfully." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ""
    Write-Host "BUILD FAILED" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
