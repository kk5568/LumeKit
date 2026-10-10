$ErrorActionPreference = 'Stop'

$installerRoot = $PSScriptRoot
$projectRoot = Split-Path -Parent $installerRoot
$projectFile = Join-Path $projectRoot 'BluetoothAudioReceiver\BluetoothAudioReceiver.csproj'
$propsFile = Join-Path $projectRoot 'Directory.Build.props'
$publishProfile = Join-Path $projectRoot 'BluetoothAudioReceiver\Properties\PublishProfiles\win-x64.pubxml'
$wixDll = Join-Path $projectRoot '.wix\.store\wix\6.0.2\wix\6.0.2\tools\net6.0\any\wix.dll'
$wixSource = Join-Path $installerRoot 'Product.wxs'
$outputRoot = Join-Path $installerRoot 'output'
$wixTempRoot = Join-Path $installerRoot 'temp'
$publishDir = Join-Path $projectRoot 'BluetoothAudioReceiver\bin\Release\net10.0-windows10.0.22621.0\win-x64\publish'
$logFile = Join-Path $outputRoot 'build.log'
$previousLogFile = Join-Path $outputRoot 'build.prev.log'
$stateFile = Join-Path $outputRoot '.build-running'

# 日志统一用「无 BOM 的 UTF-8」由本脚本自己写，避免不同命令各写一种编码导致日志乱码。
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
# 写回版本号前的原始字节。构建失败时按字节还原，保证不白占一个版本号。
$propsSnapshot = $null
$propsModified = $false
$buildSucceeded = $false
# Product.wxs 用到了 util:CloseApplication（安装前关闭旧版本），必须带上这个 WiX 扩展。
$requiredWixExtensions = @('WixToolset.Util.wixext')

# 运行状态：用来把「还在跑」和「被中断」区分开。
# .build-running 存在 = 上一次没跑到收尾（窗口被关 / 被中断）；正常结束会删掉它。
# 一律用 $script: 显式限定作用域，避免在函数里读到 $null。
$script:runStarted = Get-Date
$script:toolOutputLines = 0
$script:warningLinesTotal = 0
$script:warningLinesShown = 0
$script:lastConsoleOutputAt = Get-Date
# 收尾时写进日志的标记；日志里没有它，就说明那一次没跑完。
$script:endMarker = '本次运行已结束'

function Add-Log([string]$text) {
    if ($null -eq $text) { $text = '' }
    [System.IO.File]::AppendAllText($logFile, "$text`r`n", $utf8NoBom)
}

function Write-Stage([string]$name) {
    $script:stage = $name
    Write-Host "$name..." -ForegroundColor Cyan
    Add-Log ''
    Add-Log "===== 阶段：$name ====="
    $script:lastConsoleOutputAt = Get-Date
}

# 长时间没有输出时报一次「还在跑、别关窗口」，避免发布阶段看着像卡死。
function Write-Heartbeat {
    $now = Get-Date
    $idle = ($now - $script:lastConsoleOutputAt).TotalSeconds
    if ($idle -lt 20) { return }
    $script:lastConsoleOutputAt = $now
    $elapsed = $now - $script:runStarted
    Write-Host ("[ 已运行 {0:hh\:mm\:ss} ] 仍在构建中（已读到 $script:toolOutputLines 行输出，多为重复警告），请勿关闭窗口" -f $elapsed) -ForegroundColor DarkCyan
}

# 控制台只留进度和少量警告，逐行内容全部进日志。
# 以前 WMC1510 之类的警告会刷几百行，把真正的进度和错误挤出屏幕。
function Write-ToolOutput([string]$line) {
    if ($null -eq $line) { return }
    $script:toolOutputLines++
    Add-Log $line
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0) { return }

    if ($trimmed -match '(^|[\s:])error\s+[A-Za-z]+\d+') {
        Write-Host $line -ForegroundColor Red
        $script:lastConsoleOutputAt = Get-Date
        return
    }
    if ($trimmed -match '(^|[\s:])warning\s+[A-Za-z]+\d+') {
        $script:warningLinesTotal++
        if ($script:warningLinesShown -lt 3) {
            $script:warningLinesShown++
            Write-Host $line -ForegroundColor DarkYellow
            if ($script:warningLinesShown -eq 3) {
                Write-Host '（同类警告还有很多，之后只写进日志、不再刷屏）' -ForegroundColor DarkGray
            }
            $script:lastConsoleOutputAt = Get-Date
        }
        else {
            Write-Heartbeat
        }
        return
    }
    Write-Host $line
    $script:lastConsoleOutputAt = Get-Date
}

# stdin 被重定向（例如被别的程序调起来）时 Read-Host 会立刻返回，这里兜一下，别让退出码被异常盖掉。
function Wait-ForExit([string]$message) {
    try { Read-Host $message | Out-Null } catch { Start-Sleep -Seconds 2 }
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
    Add-Log ''
    Add-Log "===== $script:endMarker：失败（阶段：$stage，$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')）====="
    Remove-Item $stateFile -Force -ErrorAction SilentlyContinue
    Write-Host "上一次运行的日志副本：$previousLogFile" -ForegroundColor DarkGray
    Wait-ForExit '按 Enter 键退出'
    exit 1
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

# 先看上一次是不是没跑完（.build-running 还在），再轮转日志：上一次的日志留一份副本方便对比。
$previousRunAborted = $false
$previousRunInfo = ''
if (Test-Path $stateFile) {
    $previousRunAborted = $true
    try { $previousRunInfo = ([System.IO.File]::ReadAllText($stateFile)).Trim() } catch { }
    Remove-Item $stateFile -Force -ErrorAction SilentlyContinue
}
if (Test-Path $logFile) {
    try { Copy-Item $logFile $previousLogFile -Force } catch { }
}

[System.IO.File]::WriteAllText($logFile, '', $utf8NoBom)
Add-Log "===== LumeKit MSI 打包开始 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  PID=$PID ====="
try { [System.IO.File]::WriteAllText($stateFile, "开始时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`r`nPID：$PID", $utf8NoBom) } catch { }
$stage = '初始化'

Write-Host "本次日志：$logFile" -ForegroundColor DarkGray
if ($previousRunAborted) {
    $hint = '上一次打包没有正常结束（窗口被关闭或被中断），所以没有产出 MSI。'
    if (-not [string]::IsNullOrWhiteSpace($previousRunInfo)) { $hint = "$hint 上一次记录：$($previousRunInfo -replace "`r?`n", '，')" }
    Write-Host "提示：$hint" -ForegroundColor Yellow
    Write-Host "      上一次日志副本：$previousLogFile" -ForegroundColor DarkGray
    Add-Log "上一次运行未正常结束：$($previousRunInfo -replace "`r?`n", '，')"
}

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
    # 按「原字节快照 + 原编码」写回：只改版本号，不额外加空行，也不改变 BOM。
    $propsSnapshot = $propsBytes
    [System.IO.File]::WriteAllText($propsFile, $updatedPropsText, (New-Object System.Text.UTF8Encoding($propsHasBom)))
    $propsModified = $true

    # MSI 版本只接受 major.minor.build 三段，第三段上限 65535。
    # 旧写法只用 YY.M.D，同一天打包两次版本号完全相同，升级时 Windows 会认为「没有更新」而不卸旧版。
    # 把「当天第几次」并进第三段（5 号第 10 次 = 5010），保证每次打包的版本号都严格变大。
    $msiBuildField = ($today.Day * 1000) + $buildNumber
    if ($msiBuildField -gt 65535) {
        throw "MSI 版本第三段超过上限 65535：$msiBuildField。"
    }
    $msiVersion = "$($today.Year % 100).$($today.Month).$msiBuildField"
    $msiName = "LumeKit-$appVersion-x64.msi"
    $msiPath = Join-Path $outputRoot $msiName

    Add-Log "本次版本：$appVersion（MSI 版本 $msiVersion）"

    Write-Stage '检查环境'
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '找不到 dotnet。'
    }

    Write-Stage '清理旧输出'
    # 旧安装包不做批量删除：历史上生成的每个 MSI 都留在 output 里，方便随时回退和对比。
    # 本次要产出的那个（$msiPath）即使已存在也不单独删，交给 WiX 的 -o 直接覆盖。
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    Write-Stage '程序发布'
    Write-Host "正在发布并拷贝约 90MB 文件，通常 1~3 分钟。警告不再刷屏；拷贝阶段会安静几十秒，这是正常的，请勿关闭窗口。" -ForegroundColor DarkGray
    $script:lastConsoleOutputAt = Get-Date
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & dotnet publish $projectFile -p:PublishProfile=$publishProfile -p:UseSharedCompilation=false --nologo *>&1 |
        ForEach-Object { Write-ToolOutput "$_" }
    $publishExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($publishExitCode -ne 0) {
        throw "dotnet publish 返回错误代码 $publishExitCode。"
    }
    if (-not (Test-Path (Join-Path $publishDir 'BluetoothAudioReceiver.exe'))) {
        throw "发布目录中找不到 BluetoothAudioReceiver.exe：$publishDir"
    }
    Write-Host "发布阶段完成：共 $toolOutputLines 行输出，其中警告 $warningLinesTotal 行（控制台只显示前几条，全部已写进日志）。" -ForegroundColor DarkGray

    Write-Stage 'WiX 生成 MSI'
    New-Item -ItemType Directory -Path $wixTempRoot -Force | Out-Null
    $env:TEMP = $wixTempRoot
    $env:TMP = $wixTempRoot
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
                ForEach-Object { Write-ToolOutput "$_" }
            if ($LASTEXITCODE -ne 0) {
                throw "WiX 扩展 $extension 安装失败。请手动执行：dotnet `"$wixDll`" extension add -g $extension"
            }
        }
        $wixExtensionArgs += @('-ext', $extension)
    }

    & dotnet $wixDll build $wixSource -arch x64 @wixExtensionArgs -d PublishDir=$publishDir -d MsiVersion=$msiVersion -o $msiPath *>&1 |
        ForEach-Object { Write-ToolOutput "$_" }
    $wixExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($wixExitCode -ne 0) {
        throw "WiX build 返回错误代码 $wixExitCode。"
    }
    if (-not (Test-Path $msiPath)) {
        throw "WiX 没有生成预期的 MSI：$msiPath"
    }
    $buildSucceeded = $true

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
    Write-Host "耗时：$([int]((Get-Date) - $runStarted).TotalSeconds) 秒" -ForegroundColor DarkGray
    Add-Log ''
    Add-Log "===== $script:endMarker：成功（$msiName，$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')）====="
    Remove-Item $stateFile -Force -ErrorAction SilentlyContinue
    Wait-ForExit '按 Enter 键退出'
}
catch {
    $errorRecord = $_
    $details = $errorRecord | Format-List * -Force | Out-String
    Stop-Build $stage $errorRecord.Exception.Message $details
}
finally {
}
