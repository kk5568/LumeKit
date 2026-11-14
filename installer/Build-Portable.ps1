<#
    Build the unpackaged, self-contained x64 portable distribution.

    Usage:
      installer\Build-Portable.cmd
      powershell -NoProfile -ExecutionPolicy Bypass -File installer\Build-Portable.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$installerRoot = $PSScriptRoot
$projectRoot = Split-Path -Parent $installerRoot
$projectFile = Join-Path $projectRoot 'BluetoothAudioReceiver\BluetoothAudioReceiver.csproj'
$publishProfile = Join-Path $projectRoot 'BluetoothAudioReceiver\Properties\PublishProfiles\win-x64.pubxml'
$propsFile = Join-Path $projectRoot 'Directory.Build.props'
$outputRoot = Join-Path $installerRoot 'outpot_port'
$packageDirectory = Join-Path $outputRoot 'LumeKit'
$stagingDirectory = Join-Path $outputRoot '.LumeKit-staging'
$zipPath = Join-Path $outputRoot 'LumeKit-portable-win-x64.zip'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Write-Failure([string]$Message) {
    Write-Host "Portable build failed: $Message" -ForegroundColor Red
    Read-Host 'Press Enter to exit'
    exit 1
}

try {
    foreach ($path in @($projectFile, $publishProfile, $propsFile)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required file not found: $path"
        }
    }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet was not found. Install the .NET 10 SDK first.'
    }

    [xml]$props = Get-Content -LiteralPath $propsFile -Raw
    $version = [string]$props.Project.PropertyGroup.LumeKitVersion | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'Directory.Build.props does not contain a valid LumeKitVersion.'
    }

    Write-Host "Version: $version"
    Write-Host "Output directory: $packageDirectory"
    Write-Host "Archive: $zipPath"
    if ($DryRun) {
        Write-Host 'DryRun: checked inputs only; no publish or output changes.' -ForegroundColor Cyan
        exit 0
    }

    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

    Write-Host 'Publishing self-contained x64 app...' -ForegroundColor Cyan
    $publishDirectoryProperty = "-p:PublishDir=$stagingDirectory$([System.IO.Path]::DirectorySeparatorChar)"
    & dotnet publish $projectFile `
        -p:PublishProfile=$publishProfile `
        $publishDirectoryProperty `
        -p:UseSharedCompilation=false `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish returned exit code $LASTEXITCODE."
    }

    $executable = Join-Path $stagingDirectory 'BluetoothAudioReceiver.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Publish completed but the executable was not found: $executable"
    }

    [System.IO.File]::WriteAllText((Join-Path $stagingDirectory 'portable.flag'), 'portable', $utf8NoBom)
    $readme = @'
LumeKit Portable

Double-click BluetoothAudioReceiver.exe to run. Settings and logs are stored in the data folder beside the app.
Keep the LumeKit folder in a location writable by your user, such as Desktop or Documents. Do not place it under Program Files.
The portable app does not request administrator privileges. Windows may still restrict individual system-level features.
'@
    [System.IO.File]::WriteAllText((Join-Path $stagingDirectory 'README-portable.txt'), $readme, $utf8NoBom)

    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $stagingDirectory -Force | Copy-Item -Destination $packageDirectory -Recurse -Force
    Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force

    Write-Host ''
    Write-Host 'Portable package created.' -ForegroundColor Green
    Write-Host "Executable: $(Join-Path $packageDirectory 'BluetoothAudioReceiver.exe')"
    Write-Host "Archive: $zipPath"
    Read-Host 'Press Enter to exit'
}
catch {
    Write-Failure $_.Exception.Message
}
