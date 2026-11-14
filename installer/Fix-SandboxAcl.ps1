<#
    LumeKit 工作区「去沙箱限制」脚本。

    背景：DSH / Codex 沙箱会在工作区目录树里留下这些访问限制
      · Everyone:(I)(CI)(DENY)(DC)          —— 拒绝删除子项
      · S-1-4-xxx:(I)(OI)(CI)(W,D,DC)       —— 沙箱专用 SID，只有写/删/建权限，没有读权限
      · Mandatory Label\Low / Medium (NW)  —— 完整性标签造成的 no-write-up
    带这些限制时，WiX 的原生打包工具 wixnative.exe 在「初始化 cab」阶段会被拒绝访问，
    installer\output\build.log 里就是 Error 0x80070005。

    本脚本做三件事：
      1) 固定工作区根目录的继承权限，并移除已确认的 CodexSandboxUsers 规则
      2) icacls /reset /T      —— 把子目录权限重置为工作区根目录继承的默认值
      3) icacls /setintegritylevel (OI)(CI)M /T —— 把完整性标签统一成 Medium

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

function Remove-SandboxRulesFromRoot {
    param([string]$Root)

    # 上级目录可能继续继承 CodexSandboxUsers。用 icacls 断开继承并保留现有
    # 权限，再只移除已确认的沙箱主体，避免修改整个 ProjectwithAI2026 目录。
    $acl = Get-Acl -LiteralPath $Root
    $sandboxIdentities = @($acl.Access | Where-Object {
        $identity = "$($_.IdentityReference)"
        ($identity -match 'CodexSandbox') -or ($identity -match '^S-1-4-')
    } | ForEach-Object {
        "$($_.IdentityReference)"
    } | Sort-Object -Unique)

    $inheritOutput = & icacls $Root /inheritance:d /C /Q 2>&1
    $inheritExitCode = $LASTEXITCODE
    if ($inheritExitCode -ne 0) {
        throw "icacls /inheritance:d 失败（退出码 $inheritExitCode）。"
    }

    # 不使用 Set-Acl：它会尝试重写完整性标签，并要求 SeSecurityPrivilege。
    foreach ($identity in $sandboxIdentities) {
        $icaclsIdentity = $identity
        if ($identity -match '^S-1-') {
            $icaclsIdentity = "*$identity"
        }
        $removeOutput = & icacls $Root /remove:g $icaclsIdentity /C /Q 2>&1
        $removeExitCode = $LASTEXITCODE
        if ($removeExitCode -ne 0) {
            throw "icacls /remove:g 失败（退出码 $removeExitCode）：$identity"
        }
    }

    return $sandboxIdentities.Count
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
Write-Host '  1) 固定工作区根目录权限并移除已确认的沙箱主体'
Write-Host '  2) 重置子目录权限为工作区根目录继承的默认值'
Write-Host '  3) 把整棵工作区的完整性标签统一为 Medium'
Write-Host ''

$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'

# 先处理根目录，避免 /reset 后又从上级目录继承回 CodexSandboxUsers。
$removedRuleCount = Remove-SandboxRulesFromRoot -Root $Path
Write-Host "  已移除根目录沙箱主体规则：$removedRuleCount 条"

# 输出先收进变量：失败时 icacls 会为每个文件打一行「拒绝访问」，直接连到控制台会刷屏。
$childrenPath = Join-Path $Path '*'
$resetOutput = & icacls $childrenPath /reset /T /C /Q 2>&1
$resetExitCode = $LASTEXITCODE

    # /reset 可能把根目录已有的显式规则保留下来；最后再清一次根目录，
    # 确保复查结果与实际 ACL 一致。
    foreach ($identity in $sandboxIdentities) {
        $icaclsIdentity = $identity
        if ($identity -match '^S-1-') {
            $icaclsIdentity = "*$identity"
        }
        $removeOutput = & icacls $Path /remove:g $icaclsIdentity /C /Q 2>&1
        $removeExitCode = $LASTEXITCODE
        if ($removeExitCode -ne 0) {
            throw "icacls /remove:g 失败（退出码 $removeExitCode）：$identity"
        }
    }

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
