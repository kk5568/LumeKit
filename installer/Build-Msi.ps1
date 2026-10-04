$ErrorActionPreference = 'Stop'

$installerRoot = $PSScriptRoot
$projectRoot = Split-Path -Parent $installerRoot
$projectFile = Join-Path $projectRoot 'BluetoothAudioReceiver\BluetoothAudioReceiver.csproj'
$propsFile = Join-Path $projectRoot 'Directory.Build.props'
$publishProfile = Join-Path $projectRoot 'BluetoothAudioReceiver\Properties\PublishProfiles\win-x64.pubxml'
$wixExe = Join-Path $projectRoot '.wix\wix.exe'
$wixSource = Join-Path $installerRoot 'Product.wxs'
$outputRoot = Join-Path $installerRoot 'output'
$wixTempRoot = Join-Path $installerRoot 'temp'
$publishDir = Join-Path $projectRoot 'BluetoothAudioReceiver\bin\Release\net10.0-windows10.0.22621.0\win-x64\publish'
$logFile = Join-Path $outputRoot 'build.log'

function Stop-Build([string]$stage, [string]$reason, [string]$details) {
    if ([string]::IsNullOrWhiteSpace($reason)) {
        $reason = 'PowerShell 捕获到未提供简短描述的错误。'
    }
    if (-not [string]::IsNullOrWhiteSpace($details)) {
        Add-Content -Path $logFile -Value "`r`n===== PowerShell 错误详情 =====`r`n$details"
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
Set-Content -Path $logFile -Value ''
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
    if (-not (Test-Path $wixExe)) {
        throw "找不到仓库内 WiX 工具：$wixExe"
    }

    [xml]$props = Get-Content $propsFile
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

    $propsText = Get-Content $propsFile -Raw
    $updatedPropsText = $propsText `
        -replace '<LumeKitVersion>[^<]+</LumeKitVersion>', "<LumeKitVersion>$appVersion</LumeKitVersion>" `
        -replace '<LumeKitPackageVersion>[^<]+</LumeKitPackageVersion>', "<LumeKitPackageVersion>$packageVersion</LumeKitPackageVersion>"
    if ($updatedPropsText -eq $propsText) {
        throw '没有找到可更新的版本节点。'
    }
    Set-Content -Path $propsFile -Value $updatedPropsText -Encoding utf8

    $msiVersion = "$($today.Year % 100).$($today.Month).$($today.Day)"
    $msiName = "LumeKit-$appVersion-x64.msi"
    $msiPath = Join-Path $outputRoot $msiName

    $stage = '检查环境'
    Write-Host "$stage..." -ForegroundColor Cyan
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '找不到 dotnet。'
    }

    $stage = '清理旧输出'
    Write-Host "$stage..." -ForegroundColor Cyan
    Get-ChildItem $outputRoot -File -Filter '*.msi' -ErrorAction SilentlyContinue | Remove-Item -Force
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    $stage = '程序发布'
    Write-Host "$stage..." -ForegroundColor Cyan
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & dotnet publish $projectFile -p:PublishProfile=$publishProfile -p:UseSharedCompilation=false --nologo *>&1 |
        Tee-Object -FilePath $logFile -Append
    $publishExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($publishExitCode -ne 0) {
        throw "dotnet publish 返回错误代码 $publishExitCode。"
    }
    if (-not (Test-Path (Join-Path $publishDir 'BluetoothAudioReceiver.exe'))) {
        throw "发布目录中找不到 BluetoothAudioReceiver.exe：$publishDir"
    }

    $stage = 'WiX 生成 MSI'
    Write-Host "$stage..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $wixTempRoot -Force | Out-Null
    $env:TEMP = $wixTempRoot
    $env:TMP = $wixTempRoot
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $wixExe build $wixSource -arch x64 -d PublishDir=$publishDir -d MsiVersion=$msiVersion -o $msiPath *>&1 |
        Tee-Object -FilePath $logFile -Append
    $wixExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($wixExitCode -ne 0) {
        throw "WiX build 返回错误代码 $wixExitCode。"
    }
    if (-not (Test-Path $msiPath)) {
        throw "WiX 没有生成预期的 MSI：$msiPath"
    }

    $msiInfo = Get-Item $msiPath
    Write-Host ''
    Write-Host '================================' -ForegroundColor Green
    Write-Host ' LumeKit MSI 打包完成' -ForegroundColor Green
    Write-Host '================================' -ForegroundColor Green
    Write-Host ''
    Write-Host "版本：$appVersion"
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
