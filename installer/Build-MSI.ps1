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

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
[System.IO.File]::WriteAllText($logFile, '', $utf8NoBom)
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
