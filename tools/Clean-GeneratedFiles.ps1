[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if ($Force) {
    $ConfirmPreference = 'None'
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectRootWithSeparator = $projectRoot.TrimEnd('\') + '\'

$generatedDirectoryNames = @(
    'bin',
    'obj',
    '.vs',
    'publish',
    'TestResults',
    'artifacts',
    'out',
    'AppPackages',
    'BundleArtifacts',
    'packages'
)

$generatedExtensions = @(
    '.log',
    '.binlog',
    '.trace',
    '.etl',
    '.msi',
    '.exe',
    '.wixpdb',
    '.cab',
    '.msix',
    '.msixbundle',
    '.appx',
    '.appxbundle',
    '.appinstaller',
    '.msixupload',
    '.appxsym',
    '.nupkg',
    '.snupkg',
    '.tmp',
    '.temp',
    '.cache',
    '.bak',
    '.orig'
)

function Test-IsInsideProjectRoot {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    return $fullPath.StartsWith($projectRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-GeneratedItems {
    $items = [System.Collections.Generic.List[System.IO.FileSystemInfo]]::new()
    $generatedDirectories = @(
        Get-ChildItem -LiteralPath $projectRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -in $generatedDirectoryNames -and
                (Test-IsInsideProjectRoot $_.FullName)
            }
    )

    $generatedDirectories |
        ForEach-Object {
            $items.Add($_)
        }

    Get-ChildItem -LiteralPath $projectRoot -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $filePath = $_.FullName
            $_.Extension.ToLowerInvariant() -in $generatedExtensions -and
            (Test-IsInsideProjectRoot $_.FullName) -and
            -not ($generatedDirectories | Where-Object {
                $filePath.StartsWith(
                    $_.FullName.TrimEnd('\') + '\',
                    [System.StringComparison]::OrdinalIgnoreCase)
            })
        } |
        ForEach-Object {
            $items.Add($_)
        }

    return $items | Sort-Object FullName -Unique
}

$generatedItems = @(Get-GeneratedItems)

if ($generatedItems.Count -eq 0) {
    Write-Host "没有发现需要清理的生成文件。" -ForegroundColor Green
    exit 0
}

$fileCount = @($generatedItems | Where-Object { -not $_.PSIsContainer }).Count
$directoryCount = @($generatedItems | Where-Object { $_.PSIsContainer }).Count
$size = ($generatedItems |
    Where-Object { -not $_.PSIsContainer } |
    Measure-Object -Property Length -Sum).Sum

Write-Host "项目根目录：$projectRoot"
Write-Host "待清理：$directoryCount 个目录，$fileCount 个文件，共 $([math]::Round($size / 1MB, 2)) MB"

if (-not $Force -and -not $WhatIfPreference) {
    $answer = Read-Host '确认清理这些生成文件吗？输入 YES 继续'
    if ($answer -cne 'YES') {
        Write-Host '已取消，未删除任何文件。' -ForegroundColor Yellow
        exit 0
    }
}

foreach ($item in $generatedItems) {
    if ($PSCmdlet.ShouldProcess($item.FullName, '删除生成文件')) {
        Remove-Item -LiteralPath $item.FullName -Recurse:$item.PSIsContainer -Force
    }
}

if (-not $WhatIfPreference) {
    Write-Host "清理完成：删除了 $directoryCount 个目录和 $fileCount 个文件。" -ForegroundColor Green
}
