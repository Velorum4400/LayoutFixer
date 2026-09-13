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

    $dist = Join-Path $projectDir "dist"
    $installerDir = Join-Path $projectDir "installer"
    $installerOutput = Join-Path $installerDir "output"
    $installerScript = Join-Path $installerDir "installer.iss"

    if (Test-Path $dist) {
        Remove-Item $dist -Recurse -Force
    }
    New-Item -ItemType Directory -Path $dist -Force | Out-Null

    if (Test-Path $installerOutput) {
        Remove-Item $installerOutput -Recurse -Force
    }
    New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null

    Write-Host "Building LayoutFixer v$version..."

    & dotnet publish `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -o $dist `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true

    # Native programs such as dotnet do not reliably trigger
    # $ErrorActionPreference in Windows PowerShell 5.1.
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $exe = Join-Path $dist "LayoutFixer.exe"
    if (-not (Test-Path $exe)) {
        throw "Build reported success, but LayoutFixer.exe was not created."
    }

    if (-not (Test-Path $installerScript)) {
        throw "Installer script was not found: $installerScript"
    }

    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)

    $isccCandidates = @(
        (Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe"),
        (Join-Path $programFiles "Inno Setup 6\ISCC.exe")
    )

    $iscc = $isccCandidates |
        Where-Object { $_ -and (Test-Path $_) } |
        Select-Object -First 1

    if (-not $iscc) {
        throw "Inno Setup 6 was not found. Install Inno Setup 6, then run build.cmd again."
    }

    Write-Host "Building installer..."
    & $iscc "/DAppVersion=$version" $installerScript

    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
    }

    $setupExe = Join-Path $installerOutput "LayoutFixer-Setup-$version.exe"
    if (-not (Test-Path $setupExe)) {
        throw "Installer build reported success, but the setup EXE was not created."
    }

    Write-Host ""
    Write-Host "Build completed successfully." -ForegroundColor Green
    Write-Host "Application: $exe"
    Write-Host "Installer:   $setupExe"

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
