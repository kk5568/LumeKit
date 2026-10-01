param(
    [ValidateSet('Patch', 'Minor', 'Major')]
    [string]$Bump = 'Patch',
    [string]$OutputDir = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir '..')).Path
$projectDir = Join-Path $repoRoot 'BluetoothAudioReceiver'
$installerDir = (Resolve-Path $scriptDir).Path
$outputDir = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    (Get-Location).Path
} else {
    $OutputDir
}
$propsPath = Join-Path $repoRoot 'Directory.Build.props'
$publishDir = Join-Path $projectDir 'bin\Release\net10.0-windows10.0.22621.0\win-x64\publish'
$wixToolDir = Join-Path $repoRoot '.wix'
$dotnetHome = Join-Path $repoRoot '.dotnet-home'
$nugetConfigPath = Join-Path $dotnetHome 'NuGet\NuGet.Config'

New-Item -ItemType Directory -Path $dotnetHome -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $nugetConfigPath) -Force | Out-Null
$env:DOTNET_CLI_HOME = $dotnetHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:APPDATA = $dotnetHome
$env:NUGET_PACKAGES = Join-Path $dotnetHome 'packages'

if (-not (Test-Path -LiteralPath $nugetConfigPath)) {
    @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
'@ | Set-Content -LiteralPath $nugetConfigPath -Encoding utf8
}

function Get-PropsValue {
    param(
        [string]$Path,
        [string]$PropertyName
    )

    $content = Get-Content -LiteralPath $Path -Raw
    $match = [regex]::Match($content, "<$PropertyName>([^<]+)</$PropertyName>")
    if (-not $match.Success) {
        throw "Cannot find <$PropertyName> in $Path"
    }

    return $match.Groups[1].Value
}

function Get-NextVersion {
    param(
        [string]$Current,
        [ValidateSet('Patch', 'Minor', 'Major')]
        [string]$BumpKind
    )

    $today = Get-Date
    $year = $today.Year
    $month = $today.Month
    $day = $today.Day

    $tailMajor = 0
    $tailMinor = 0

    if (-not [string]::IsNullOrWhiteSpace($Current)) {
        $parts = $Current.Split('.', [System.StringSplitOptions]::RemoveEmptyEntries)
        switch ($parts.Length) {
            5 {
                $tailMajor = [int]$parts[3]
                $tailMinor = [int]$parts[4]
            }
            4 {
                $tailMajor = [int]$parts[2]
                $tailMinor = [int]$parts[3]
            }
            3 {
                $tailMajor = [int]$parts[1]
                $legacyPatch = [int]$parts[2]
                $tailMajor += [int][math]::Floor($legacyPatch / 10)
                $tailMinor = $legacyPatch % 10
            }
            2 {
                $tailMajor = [int]$parts[0]
                $tailMinor = [int]$parts[1]
            }
            default {
                throw "Unsupported version format: $Current"
            }
        }
    }

    switch ($BumpKind) {
        'Major' {
            $tailMajor++
            $tailMinor = 0
        }
        'Minor' {
            $tailMajor++
            $tailMinor = 0
        }
        default {
            $tailMinor++
        }
    }

    if ($tailMinor -ge 10) {
        $tailMajor += [int][math]::Floor($tailMinor / 10)
        $tailMinor = $tailMinor % 10
    }

    return "$year.$month.$day.$tailMajor.$tailMinor"
}

function Convert-ToPackageVersion {
    param([string]$DisplayVersion)

    $parts = $DisplayVersion.Split('.', [System.StringSplitOptions]::RemoveEmptyEntries)
    if ($parts.Length -ne 5) {
        throw "Unsupported display version format: $DisplayVersion"
    }

    $year = [int]$parts[0]
    $month = [int]$parts[1]
    $day = [int]$parts[2]
    $tailMajor = [int]$parts[3]
    $tailMinor = [int]$parts[4]
    $serial = ($tailMajor * 10) + $tailMinor

    return "$($year % 100).$month.$day.$serial"
}

function Set-PropsValue {
    param(
        [string]$Path,
        [string]$PropertyName,
        [string]$VersionValue
    )

    $content = Get-Content -LiteralPath $Path -Raw
    $updated = [regex]::Replace(
        $content,
        "<$PropertyName>[^<]+</$PropertyName>",
        "<$PropertyName>$VersionValue</$PropertyName>",
        1)

    if ($updated -eq $content) {
        throw "Cannot update <$PropertyName> in $Path"
    }

    Set-Content -LiteralPath $Path -Value $updated -Encoding utf8
}

function Get-WixCommand {
    $wix = Get-Command wix -ErrorAction SilentlyContinue
    if ($null -ne $wix) {
        return $wix.Source
    }

    $wixExe = Join-Path $wixToolDir 'wix.exe'
    if (-not (Test-Path -LiteralPath $wixExe)) {
        Write-Host "WiX CLI was not found. Installing it in $wixToolDir..."
        New-Item -ItemType Directory -Path $wixToolDir -Force | Out-Null
        dotnet tool install wix --tool-path $wixToolDir --version 6.* --no-cache --configfile $nugetConfigPath |
            Out-Host
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $wixExe)) {
            throw "Unable to install WiX CLI. Install WiX 6 manually or ensure NuGet access is available, then run this script again."
        }
    }

    return $wixExe
}

$currentVersionText = Get-PropsValue -Path $propsPath -PropertyName 'LumeKitVersion'
$nextDisplayVersion = Get-NextVersion -Current $currentVersionText -BumpKind $Bump
$nextPackageVersion = Convert-ToPackageVersion -DisplayVersion $nextDisplayVersion
$msiName = "LumeKit-$nextDisplayVersion-x64.msi"
$resolvedOutputDir = [System.IO.Path]::GetFullPath($outputDir)
$msiPath = Join-Path $resolvedOutputDir $msiName

if (-not (Test-Path -LiteralPath $resolvedOutputDir)) {
    New-Item -ItemType Directory -Path $resolvedOutputDir | Out-Null
}

Write-Host "Bumping display version $currentVersionText -> $nextDisplayVersion"
Write-Host "Using package version $nextPackageVersion"
Set-PropsValue -Path $propsPath -PropertyName 'LumeKitVersion' -VersionValue $nextDisplayVersion
Set-PropsValue -Path $propsPath -PropertyName 'LumeKitPackageVersion' -VersionValue $nextPackageVersion

Write-Host "Syncing version artifacts for $nextPackageVersion..."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $installerDir 'Sync-Version.ps1') -Version $nextPackageVersion

Write-Host "Publishing Release x64..."
dotnet publish (Join-Path $projectDir 'BluetoothAudioReceiver.csproj') `
    -c Release `
    -p:Platform=x64 `
    -p:PublishReadyToRun=false `
    -r win-x64 `
    --self-contained true `
    --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'BluetoothAudioReceiver.exe'))) {
    throw "Published application was not found: $publishDir"
}

if (Test-Path -LiteralPath $msiPath) {
    Remove-Item -LiteralPath $msiPath -Force
}

Write-Host "Building MSI $msiName..."
$wixCommand = Get-WixCommand
Push-Location $installerDir
try {
    & $wixCommand build BluetoothAudioReceiver.wxs -arch x64 -out $msiPath
    if ($LASTEXITCODE -ne 0) {
        throw "WiX build failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $msiPath)) {
    throw "MSI was not created: $msiPath"
}

Get-Item -LiteralPath $msiPath | Select-Object FullName,Length,LastWriteTime | Format-List
