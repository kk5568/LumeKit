<#
    LumeKit 打包脚本：递增版本号 → dotnet publish → WiX 生成 MSI。

    用法：
      installer\Build-MSI.cmd                      双击/命令行直接打包
      powershell -File installer\Build-MSI.ps1 -DryRun   只检查环境、版本号和路径，不发布、不打包、不改版本号

    脚本内的关键约定：
      · WiX 临时目录放在 %LOCALAPPDATA%\LumeKit\wix-temp，不放仓库里（仓库带沙箱 ACL 时 WiX 建 cab 会被拒）。
      · 检测到工作区带沙箱访问限制时，自动调用 installer\Fix-SandboxAcl.ps1 去掉（可用 -SkipAclFix 跳过）。
      · 构建失败会把 Directory.Build.props 的版本号按原字节回滚，不白占版本号。
#>
[CmdletBinding()]
param(
    # 只检查环境、版本号和路径，不发布、不打包、不改版本号文件。
    [switch]$DryRun,
    # 跳过「检查沙箱限制」阶段（默认检测到限制就自动修复）。
    [switch]$SkipAclFix
)

$ErrorActionPreference = 'Stop'

$installerRoot = $PSScriptRoot
$projectRoot = Split-Path -Parent $installerRoot
$projectFile = Join-Path $projectRoot 'BluetoothAudioReceiver\BluetoothAudioReceiver.csproj'
$propsFile = Join-Path $projectRoot 'Directory.Build.props'
$publishProfile = Join-Path $projectRoot 'BluetoothAudioReceiver\Properties\PublishProfiles\win-x64.pubxml'
$wixDll = Join-Path $projectRoot '.wix\.store\wix\6.0.2\wix\6.0.2\tools\net6.0\any\wix.dll'
$wixSource = Join-Path $installerRoot 'Product.wxs'
$outputRoot = Join-Path $installerRoot 'output'
# WiX 临时目录放到仓库外：仓库树带沙箱 ACL（Everyone DENY(DC)、沙箱 SID 无读权限、Low/Medium(NW) 标签）时，
# WiX 的原生工具在仓库内初始化 cab 会被拒绝访问（build.log 里的 Error 0x80070005）。
$wixTempRoot = Join-Path $env:LOCALAPPDATA 'LumeKit\wix-temp'
# 旧版脚本把临时目录放在仓库里（installer\temp），留着做一次残留清理。
$legacyWixTempRoot = Join-Path $installerRoot 'temp'
$aclFixScript = Join-Path $installerRoot 'Fix-SandboxAcl.ps1'
$publishDir = Join-Path $projectRoot 'BluetoothAudioReceiver\bin\Release\net10.0-windows10.0.22621.0\win-x64\publish'
$logFile = Join-Path $outputRoot 'build.log'

# 日志统一用「无 BOM 的 UTF-8」由本脚本自己写，避免不同命令各写一种编码导致日志乱码。
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
# 写回版本号前的原始字节。构建失败时按字节还原，保证不白占一个版本号。
$propsSnapshot = $null
$propsModified = $false
$buildSucceeded = $false
# Product.wxs 用到了 util:CloseApplication（安装前关闭旧版本），必须带上这个 WiX 扩展。
$requiredWixExtensions = @('WixToolset.Util.wixext')

function Add-Log([string]$text) {
    if ($null -eq $text) { $text = '' }
    [System.IO.File]::AppendAllText($logFile, "$text`r`n", $utf8NoBom)
}

function Write-Stage([string]$name) {
    $script:stage = $name
    Write-Host "$name..." -ForegroundColor Cyan
    Add-Log ''
    Add-Log "===== 阶段：$name ====="
}

function Test-SandboxAclRestriction([string]$Path) {
    # 只读探测：沙箱留下的 ACL 与完整性标签，命中任何一条就算带限制。
    $lines = & icacls $Path 2>&1
    return @($lines | Where-Object {
        $text = "$_"
        ($text -match 'S-1-4-') -or ($text -match '\(DENY\)') -or ($text -match 'Mandatory Label\\Low') -or ($text -match 'CodexSandbox')
    })
}

function Restore-VersionFile {
    if ($buildSucceeded -or (-not $propsModified) -or ($null -eq $propsSnapshot)) {
        return
    }
    [System.IO.File]::WriteAllBytes($propsFile, $propsSnapshot)
    $script:propsModified = $false
    Write-Host "构建未完成，已把版本号回滚到原值：$propsFile" -ForegroundColor Yellow
    Add-Log "构建未完成，已回滚版本号文件：$propsFile"
}

function Stop-Build([string]$stage, [string]$reason, [string]$details) {
    Restore-VersionFile
    if ([string]::IsNullOrWhiteSpace($reason)) {
        $reason = 'PowerShell 捕获到未提供简短描述的错误。'
    }
    if (-not [string]::IsNullOrWhiteSpace($details)) {
        Add-Log ''
        Add-Log '===== PowerShell 错误详情 ====='
        Add-Log $details
    }
    Write-Host ''
    Write-Host '================================' -ForegroundColor Red
    Write-Host ' LumeKit MSI 打包失败' -ForegroundColor Red
    Write-Host '================================' -ForegroundColor Red
    Write-Host ''
    Write-Host "失败阶段：$stage" -ForegroundColor Yellow
    Write-Host "原因：$reason" -ForegroundColor Yellow
    if (-not [string]::IsNullOrWhiteSpace($details)) {
        Write-Host ''
        Write-Host $details -ForegroundColor DarkYellow
    }
    Write-Host ''
    Write-Host "详细日志：$logFile" -ForegroundColor Yellow
    Read-Host '按 Enter 键退出'
    exit 1
}

if (-not $DryRun) {
    # -DryRun 只读检查，不建目录、不清日志。
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    [System.IO.File]::WriteAllText($logFile, '', $utf8NoBom)
}
$stage = '初始化'

try {
    if (-not (Test-Path $projectFile)) {
        throw "找不到项目文件：$projectFile"
    }
    if (-not (Test-Path $publishProfile)) {
        throw "找不到 x64 发布配置：$publishProfile"
    }
    if (-not (Test-Path $propsFile)) {
        throw "找不到版本配置：$propsFile"
    }
    if (-not (Test-Path $wixDll)) {
        throw "找不到仓库内 WiX 工具：$wixDll"
    }

    # 读原始字节：既用于解析，也留作失败回滚的快照，同时记住原文件带不带 BOM。
    $propsBytes = [System.IO.File]::ReadAllBytes($propsFile)
    $propsHasBom = $propsBytes.Length -ge 3 -and $propsBytes[0] -eq 0xEF -and $propsBytes[1] -eq 0xBB -and $propsBytes[2] -eq 0xBF
    $propsText = [System.Text.Encoding]::UTF8.GetString($propsBytes)
    if ($propsHasBom) { $propsText = $propsText.Substring(1) }

    [xml]$props = $propsText
    $versionNode = $props.Project.PropertyGroup.LumeKitVersion | Select-Object -First 1
    $packageVersionNode = $props.Project.PropertyGroup.LumeKitPackageVersion | Select-Object -First 1
    $currentVersion = [string]$versionNode
    if ([string]::IsNullOrWhiteSpace($currentVersion)) {
        throw 'Directory.Build.props 中没有有效的 LumeKitVersion。'
    }
    if ([string]::IsNullOrWhiteSpace([string]$packageVersionNode)) {
        throw 'Directory.Build.props 中没有有效的 LumeKitPackageVersion。'
    }

    $currentVersion = $currentVersion.Trim()
    $versionParts = $currentVersion.Split('.')
    if ($versionParts.Count -ne 5) {
        throw "版本号格式无效：$currentVersion。格式应为 YYYY.M.D.Release.Build。"
    }
    if ($versionParts | Where-Object { $_ -notmatch '^\d+$' }) {
        throw "版本号格式无效：$currentVersion。每一段都必须是数字。"
    }

    $today = Get-Date
    $buildNumber = [int]$versionParts[4] + 1
    $appVersion = '{0}.{1}.{2}.{3}.{4}' -f $today.Year, $today.Month, $today.Day, $versionParts[3], $buildNumber
    $packageVersion = '{0}.{1}.{2}.{3}{4}' -f `
        ($today.Year % 100), `
        $today.Month, `
        $today.Day, `
        $versionParts[3], `
        $buildNumber
    if ($packageVersion.Length -gt 27) {
        throw "生成的 MSI 版本过长：$packageVersion。"
    }

    $updatedPropsText = $propsText `
        -replace '<LumeKitVersion>[^<]+</LumeKitVersion>', "<LumeKitVersion>$appVersion</LumeKitVersion>" `
        -replace '<LumeKitPackageVersion>[^<]+</LumeKitPackageVersion>', "<LumeKitPackageVersion>$packageVersion</LumeKitPackageVersion>"
    if ($updatedPropsText -eq $propsText) {
        throw '没有找到可更新的版本节点。'
    }

    # MSI 版本只接受 major.minor.build 三段，第三段上限 65535。
    # 第三段编码「当天第几分钟」：日 * 1440 + 时 * 60 + 分（最大 31*1440+1439 = 46079，不超上限）。
    # 只用墙上时间，版本号必然随时间变大；不再用「当天第几次打包」这种各副本各数各的计数器——
    # 那种算法下，两个共用 UpgradeCode 的副本会互相挡：计数多的版本更大，另一个的新包反被当降级拒绝。
    $msiBuildField = ($today.Day * 1440) + ($today.Hour * 60) + $today.Minute
    if ($msiBuildField -gt 65535) {
        throw "MSI 版本第三段超过上限 65535：$msiBuildField。"
    }
    $msiVersion = "$($today.Year % 100).$($today.Month).$msiBuildField"
    $msiName = "LumeKit-$appVersion-x64.msi"
    $msiPath = Join-Path $outputRoot $msiName

    if ($DryRun) {
        $dryRunRestrictions = Test-SandboxAclRestriction $projectRoot
        $restrictionText = '没有'
        if ($dryRunRestrictions.Count -gt 0) { $restrictionText = "有 $($dryRunRestrictions.Count) 条，打包时会自动修复" }
        Write-Host ''
        Write-Host '================================' -ForegroundColor Cyan
        Write-Host ' -DryRun：只检查，不发布、不打包、不改版本号' -ForegroundColor Cyan
        Write-Host '================================' -ForegroundColor Cyan
        Write-Host "项目文件：$projectFile"
        Write-Host "版本号文件：$propsFile"
        Write-Host "当前版本：$currentVersion"
        Write-Host "本次会用：$appVersion（MSI 版本 $msiVersion）"
        Write-Host "MSI 输出：$msiPath"
        Write-Host "发布目录：$publishDir"
        Write-Host "WiX 临时目录：$wixTempRoot"
        Write-Host "沙箱访问限制：$restrictionText"
        Write-Host ''
        Write-Host '确认无误后重跑本脚本（不带 -DryRun）即可打包。' -ForegroundColor Cyan
        exit 0
    }

    # 按「原字节快照 + 原编码」写回：只改版本号，不额外加空行，也不改变 BOM。
    $propsSnapshot = $propsBytes
    [System.IO.File]::WriteAllText($propsFile, $updatedPropsText, (New-Object System.Text.UTF8Encoding($propsHasBom)))
    $propsModified = $true

    Write-Stage '检查环境'
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '找不到 dotnet。'
    }

    Write-Stage '检查沙箱限制'
    if ($SkipAclFix) {
        Write-Host '已用 -SkipAclFix 跳过。' -ForegroundColor Yellow
    }
    else {
        $restrictions = Test-SandboxAclRestriction $projectRoot
        if ($restrictions.Count -eq 0) {
            Write-Host '工作区没有沙箱访问限制。' -ForegroundColor Green
        }
        else {
            Write-Host '检测到工作区带沙箱访问限制：' -ForegroundColor Yellow
            $restrictions | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkYellow }
            Add-Log '检测到工作区带沙箱访问限制：'
            $restrictions | ForEach-Object { Add-Log "  $_" }
            if (-not (Test-Path $aclFixScript)) {
                throw "工作区带沙箱限制，但找不到修复脚本：$aclFixScript"
            }
            Write-Host '正在调用 Fix-SandboxAcl.ps1 去掉限制...' -ForegroundColor Yellow
            $previousAclPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $aclFixScript -Path $projectRoot *>&1 |
                ForEach-Object { $line = "$_"; Write-Host $line; Add-Log $line }
            $aclFixExitCode = $LASTEXITCODE
            $ErrorActionPreference = $previousAclPreference
            if ($aclFixExitCode -ne 0) {
                throw "去沙箱限制失败（退出码 $aclFixExitCode）。请先用「以管理员身份运行」的 PowerShell 跑一次：$aclFixScript"
            }
            $restrictions = Test-SandboxAclRestriction $projectRoot
            if ($restrictions.Count -gt 0) {
                Write-Host '沙箱限制仍然存在，继续尝试打包。' -ForegroundColor Yellow
                Add-Log '沙箱限制仍然存在，继续尝试打包。'
            }
            else {
                Write-Host '沙箱限制已清除。' -ForegroundColor Green
            }
        }
    }

    Write-Stage '清理旧输出'
    # 旧安装包不做批量删除：历史上生成的每个 MSI 都留在 output 里，方便随时回退和对比。
    # 本次要产出的那个（$msiPath）即使已存在也不单独删，交给 WiX 的 -o 直接覆盖。
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }
    # 旧版脚本把 WiX 临时目录放在仓库里，失败时残留随机目录和 cab（可上百 MB），整棵删掉。
    if (Test-Path $legacyWixTempRoot) {
        Remove-Item $legacyWixTempRoot -Recurse -Force
    }

    # 同一分钟再打一次会得到完全相同的 MSI 版本号，装上去 Windows 会认为「版本没变」而不升级。
    # 与其产出一个装不上去的包，不如直接拦下来。
    if (Test-Path $msiPath) {
        throw "同一分钟内已经有同名安装包：$msiPath。等一分钟再打包，或先删掉它。"
    }

    Write-Stage '程序发布'
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & dotnet publish $projectFile -p:PublishProfile=$publishProfile -p:UseSharedCompilation=false --nologo *>&1 |
        ForEach-Object { $line = "$_"; Write-Host $line; Add-Log $line }
    $publishExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($publishExitCode -ne 0) {
        throw "dotnet publish 返回错误代码 $publishExitCode。"
    }
    if (-not (Test-Path (Join-Path $publishDir 'BluetoothAudioReceiver.exe'))) {
        throw "发布目录中找不到 BluetoothAudioReceiver.exe：$publishDir"
    }

    Write-Stage 'WiX 生成 MSI'
    New-Item -ItemType Directory -Path $wixTempRoot -Force | Out-Null
    $env:TEMP = $wixTempRoot
    $env:TMP = $wixTempRoot
    Write-Host "WiX 临时目录：$wixTempRoot"
    Add-Log "WiX 临时目录：$wixTempRoot"
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'

    # Product.wxs 用到的 WiX 扩展必须先装进缓存，否则会报 WIX0144 / WIX0200。
    $installedExtensions = @(& dotnet $wixDll extension list -g 2>&1)
    $wixExtensionArgs = @()
    foreach ($extension in $requiredWixExtensions) {
        if (-not ($installedExtensions | Where-Object { $_ -match [regex]::Escape($extension) })) {
            Write-Host "缺少 WiX 扩展 $extension，正在安装（需要联网，仅第一次）..." -ForegroundColor Yellow
            Add-Log "安装 WiX 扩展：$extension"
            & dotnet $wixDll extension add -g $extension *>&1 |
                ForEach-Object { $line = "$_"; Write-Host $line; Add-Log $line }
            if ($LASTEXITCODE -ne 0) {
                throw "WiX 扩展 $extension 安装失败。请手动执行：dotnet `"$wixDll`" extension add -g $extension"
            }
        }
        $wixExtensionArgs += @('-ext', $extension)
    }

    & dotnet $wixDll build $wixSource -arch x64 @wixExtensionArgs -d PublishDir=$publishDir -d MsiVersion=$msiVersion -o $msiPath *>&1 |
        ForEach-Object { $line = "$_"; Write-Host $line; Add-Log $line }
    $wixExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($wixExitCode -ne 0) {
        throw "WiX build 返回错误代码 $wixExitCode。"
    }
    if (-not (Test-Path $msiPath)) {
        throw "WiX 没有生成预期的 MSI：$msiPath"
    }
    $buildSucceeded = $true

    # 打包成功后清掉本次 WiX 临时目录（每次约 90 MB），别让 %LOCALAPPDATA% 越堆越大。
    # 失败时保留，方便排查 cab 相关问题。
    if (($env:TEMP -eq $wixTempRoot) -or ($env:TMP -eq $wixTempRoot)) {
        Remove-Item $wixTempRoot -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "已清理 WiX 临时目录：$wixTempRoot"
        Add-Log "已清理 WiX 临时目录：$wixTempRoot"
    }

    $msiInfo = Get-Item $msiPath
    Write-Host ''
    Write-Host '================================' -ForegroundColor Green
    Write-Host ' LumeKit MSI 打包完成' -ForegroundColor Green
    Write-Host '================================' -ForegroundColor Green
    Write-Host ''
    Write-Host "版本：$appVersion"
    Write-Host "MSI 版本：$msiVersion"
    Write-Host "MSI：$msiPath"
    Write-Host "大小：$([math]::Round($msiInfo.Length / 1MB, 2)) MB"
    Read-Host '按 Enter 键退出'
}
catch {
    $errorRecord = $_
    $details = $errorRecord | Format-List * -Force | Out-String
    Stop-Build $stage $errorRecord.Exception.Message $details
}
finally {
}
