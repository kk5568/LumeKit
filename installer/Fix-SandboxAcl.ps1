<#
    LumeKit 工作区「去沙箱限制」脚本。

    背景：DSH / Codex 沙箱会在工作区目录树里留下这些访问限制
      · Everyone:(I)(CI)(DENY)(DC)          —— 拒绝删除子项
      · S-1-4-xxx:(I)(OI)(CI)(W,D,DC)       —— 沙箱专用 SID，只有写/删/建权限，没有读权限
      · Mandatory Label\Low / Medium (NW)  —— 完整性标签造成的 no-write-up
    带这些限制时，WiX 的原生打包工具 wixnative.exe 在「初始化 cab」阶段会被拒绝访问，
    installer\output\build.log 里就是 Error 0x80070005。

    本脚本做两件事：
      1) icacls /reset /T      —— 把整棵工作区的权限重置为从上级目录继承的默认值
      2) icacls /setintegritylevel (OI)(CI)M /T —— 把完整性标签统一成 Medium

    用法（在你自己的 PowerShell 窗口里跑，不要在沙箱终端里跑）：
      powershell -ExecutionPolicy Bypass -File installer\Fix-SandboxAcl.ps1
    如果提示「拒绝访问」，就用「以管理员身份运行」的 PowerShell 再跑一次。
#>
[CmdletBinding()]
param(
    # 要处理的工作区根目录，默认是本脚本上一级目录（仓库根）。
    [string]$Path,
    # 只重置权限，不动完整性标签。
    [switch]$SkipIntegrityLevel
)

$ErrorActionPreference = 'Stop'

# 注意：Windows PowerShell 5.1 里 $PSScriptRoot 在 param() 默认值求值时是空的，
# 所以默认路径必须在脚本正文里算。
if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = Split-Path -Parent $PSScriptRoot
}

function Get-AclRestrictions {
    param([string]$Root)
    # 原生命令的 stderr 在 $ErrorActionPreference='Stop' 下会当成异常抛出，这里临时降级。
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $lines = & icacls $Root 2>&1
    $ErrorActionPreference = $previous
    return @($lines | Where-Object {
        $text = "$_"
        ($text -match 'S-1-4-') -or ($text -match '\(DENY\)') -or ($text -match 'Mandatory Label\\Low') -or ($text -match 'CodexSandbox')
    })
}

if (-not (Test-Path -LiteralPath $Path)) {
    throw "工作区路径不存在：$Path"
}
$Path = (Resolve-Path -LiteralPath $Path).Path

Write-Host "工作区：$Path" -ForegroundColor Cyan
$restrictions = Get-AclRestrictions -Root $Path

if ($restrictions.Count -eq 0) {
    Write-Host '没有检测到沙箱访问限制，无需处理。' -ForegroundColor Green
    exit 0
}

Write-Host '检测到以下沙箱访问限制：' -ForegroundColor Yellow
$restrictions | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkYellow }
Write-Host ''
Write-Host '即将执行：' -ForegroundColor Cyan
Write-Host '  1) 重置整棵工作区的权限为「继承默认值」'
Write-Host '  2) 把整棵工作区的完整性标签统一为 Medium'
Write-Host ''

$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'

# 输出先收进变量：失败时 icacls 会为每个文件打一行「拒绝访问」，直接连到控制台会刷屏。
$resetOutput = & icacls $Path /reset /T /C /Q 2>&1
$resetExitCode = $LASTEXITCODE
$integrityExitCode = 0
$integrityOutput = @()
if (-not $SkipIntegrityLevel) {
    $integrityOutput = & icacls $Path /setintegritylevel '(OI)(CI)M' /T /C /Q 2>&1
    $integrityExitCode = $LASTEXITCODE
}

$ErrorActionPreference = $previous

$resetOutput | Select-Object -Last 2 | ForEach-Object { Write-Host "  $_" }
$integrityOutput | Select-Object -Last 2 | ForEach-Object { Write-Host "  $_" }

if ($resetExitCode -ne 0) {
    throw "icacls /reset 失败（退出码 $resetExitCode）。请改用「以管理员身份运行」的 PowerShell 重跑本脚本。"
}
if ($integrityExitCode -ne 0) {
    throw "icacls /setintegritylevel 失败（退出码 $integrityExitCode）。请改用「以管理员身份运行」的 PowerShell 重跑本脚本。"
}

$remaining = Get-AclRestrictions -Root $Path
if ($remaining.Count -eq 0) {
    Write-Host '完成：沙箱访问限制已清除。' -ForegroundColor Green
    exit 0
}

Write-Host '警告：仍然存在以下限制：' -ForegroundColor Yellow
$remaining | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkYellow }
exit 2
