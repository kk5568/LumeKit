<#
    LumeKit 旧文件清理脚本。

    默认只列出「将要删除什么」，不会真删；确认无误后加 -Apply 才执行删除。
    可组合的删除范围：
      -Temp       旧版打包脚本残留在仓库里的 WiX 临时目录 installer\temp，
                  以及排查 cab 失败时在仓库根建的测试目录 _wixtest
      -OldMsi     installer\output 里除最新一个之外的历史 MSI 与 wixpdb
      -DeadPages  已废弃的 3 组 Hub 页 + ViewModel，以及 Views\Pages\_writetest.tmp
      -All        上面三项全选

    用法（在你自己的 PowerShell 窗口里跑）：
      powershell -ExecutionPolicy Bypass -File installer\Clean-OldVersions.ps1 -All
      powershell -ExecutionPolicy Bypass -File installer\Clean-OldVersions.ps1 -All -Apply
#>
[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [switch]$Temp,
    [switch]$OldMsi,
    [switch]$DeadPages,
    [switch]$All,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

# 注意：Windows PowerShell 5.1 里 $PSScriptRoot 在 param() 默认值求值时是空的，
# 所以默认路径必须在脚本正文里算。
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}

if (-not (Test-Path -LiteralPath $ProjectRoot)) {
    throw "项目根目录不存在：$ProjectRoot"
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path

if ($All) {
    $Temp = $true
    $OldMsi = $true
    $DeadPages = $true
}
if (-not ($Temp -or $OldMsi -or $DeadPages)) {
    Write-Host '没有指定删除范围。请加 -Temp / -OldMsi / -DeadPages，或 -All。' -ForegroundColor Yellow
    Write-Host '示例：powershell -ExecutionPolicy Bypass -File installer\Clean-OldVersions.ps1 -All'
    exit 1
}

$installerRoot = Join-Path $ProjectRoot 'installer'
$targets = New-Object System.Collections.ArrayList

function Add-Target {
    param([string]$Path, [string]$Reason, [switch]$Directory)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    [void]$targets.Add([pscustomobject]@{ Path = $Path; Reason = $Reason; Directory = [bool]$Directory })
}

if ($Temp) {
    Add-Target -Path (Join-Path $installerRoot 'temp') -Reason '旧版打包脚本留在仓库里的 WiX 临时目录（随机子目录 + cab）' -Directory
    Add-Target -Path (Join-Path $ProjectRoot '_wixtest') -Reason '排查 WiX cab 失败时的测试目录' -Directory
}

if ($DeadPages) {
    $deadFiles = @(
        'BluetoothAudioReceiver\Views\Pages\AudioNotificationHubPage.xaml',
        'BluetoothAudioReceiver\Views\Pages\AudioNotificationHubPage.xaml.cs',
        'BluetoothAudioReceiver\Views\Pages\ThemeBrightnessHubPage.xaml',
        'BluetoothAudioReceiver\Views\Pages\ThemeBrightnessHubPage.xaml.cs',
        'BluetoothAudioReceiver\Views\Pages\EfficiencySettingsHubPage.xaml',
        'BluetoothAudioReceiver\Views\Pages\EfficiencySettingsHubPage.xaml.cs',
        'BluetoothAudioReceiver\ViewModels\Pages\AudioNotificationHubPageViewModel.cs',
        'BluetoothAudioReceiver\ViewModels\Pages\ThemeBrightnessHubPageViewModel.cs',
        'BluetoothAudioReceiver\ViewModels\Pages\EfficiencySettingsHubPageViewModel.cs',
        'BluetoothAudioReceiver\Views\Pages\_writetest.tmp'
    )
    foreach ($relative in $deadFiles) {
        Add-Target -Path (Join-Path $ProjectRoot $relative) -Reason '已废弃的旧 Hub 页 / ViewModel / 探测残留'
    }
}

function Compare-PackageVersion {
    # 逐段数值比较（版本号是 YYYY.MM.DD.h.m 这种多段数字，System.Version 最多只吃 4 段）。
    param([string]$Left, [string]$Right)
    $leftParts = $Left -split '\.'
    $rightParts = $Right -split '\.'
    $count = [Math]::Max($leftParts.Count, $rightParts.Count)
    for ($i = 0; $i -lt $count; $i++) {
        $leftValue = 0
        $rightValue = 0
        if ($i -lt $leftParts.Count -and $leftParts[$i] -match '^\d+$') { $leftValue = [int]$leftParts[$i] }
        if ($i -lt $rightParts.Count -and $rightParts[$i] -match '^\d+$') { $rightValue = [int]$rightParts[$i] }
        if ($leftValue -ne $rightValue) { return ($leftValue - $rightValue) }
    }
    return 0
}

if ($OldMsi) {
    $outputRoot = Join-Path $installerRoot 'output'
    if (Test-Path -LiteralPath $outputRoot) {
        # 按“版本号”分组，而不是按文件修改时间：一个版本有 .msi 和 .wixpdb 两个文件，
        # 它们写入时间不同，按时间取“最新一个文件”会把同版本的另一半也当成旧的删掉。
        $packages = Get-ChildItem -LiteralPath $outputRoot -File -Force |
            Where-Object { $_.Extension -in @('.msi', '.wixpdb') } |
            ForEach-Object {
                $versionText = if ($_.BaseName -match '^LumeKit-(?<v>.+)-x64$') { $Matches['v'] } else { $_.BaseName }
                [pscustomobject]@{ File = $_; Version = $versionText }
            }
        if ($packages.Count -gt 0) {
            $keepVersion = $packages[0].Version
            foreach ($package in $packages) {
                if ((Compare-PackageVersion $package.Version $keepVersion) -gt 0) { $keepVersion = $package.Version }
            }
            foreach ($package in $packages) {
                if ($package.Version -eq $keepVersion) { continue }
                Add-Target -Path $package.File.FullName -Reason "历史安装包（保留最新版本 $keepVersion）"
            }
        }
    }
}

if ($targets.Count -eq 0) {
    Write-Host '没有找到符合范围的旧文件，无需清理。' -ForegroundColor Green
    exit 0
}

function Get-TargetSize {
    param($Target)
    if (-not $Target.Directory) {
        return (Get-Item -LiteralPath $Target.Path).Length
    }
    $sum = (Get-ChildItem -LiteralPath $Target.Path -Recurse -Force -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    if ($null -eq $sum) { return 0 }
    return $sum
}

$totalBytes = 0
Write-Host ''
Write-Host "将要删除以下 $($targets.Count) 项（位于 $ProjectRoot 内）：" -ForegroundColor Cyan
foreach ($target in $targets) {
    $size = 0
    try { $size = Get-TargetSize -Target $target } catch { $size = 0 }
    $totalBytes = $totalBytes + $size
    $kind = '文件'
    if ($target.Directory) { $kind = '目录' }
    $relative = $target.Path.Substring($ProjectRoot.Length).TrimStart('\')
    Write-Host ("  [{0}] {1}  ({2} MB)  {3}" -f $kind, $relative, [math]::Round($size / 1MB, 2), $target.Reason) -ForegroundColor Gray
}
Write-Host ("合计：{0} MB" -f [math]::Round($totalBytes / 1MB, 2)) -ForegroundColor Cyan

if (-not $Apply) {
    Write-Host ''
    Write-Host '（当前是预览，没有删除任何东西。确认无误后加 -Apply 再跑一次。）' -ForegroundColor Yellow
    exit 0
}

Write-Host ''
foreach ($target in $targets) {
    $relative = $target.Path.Substring($ProjectRoot.Length).TrimStart('\')
    try {
        if ($target.Directory) {
            Remove-Item -LiteralPath $target.Path -Recurse -Force
        }
        else {
            Remove-Item -LiteralPath $target.Path -Force
        }
        Write-Host "已删除：$relative" -ForegroundColor Green
    }
    catch {
        Write-Host "删除失败：$relative —— $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ''
Write-Host '清理完成。' -ForegroundColor Green
