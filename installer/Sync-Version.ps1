param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $PSCommandPath
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $scriptDir '..')).Path
}

$projectDir = Join-Path $ProjectRoot 'BluetoothAudioReceiver'
$manifestPath = Join-Path $projectDir 'Package.appxmanifest'
$manifestDebugPath = Join-Path $projectDir 'Package_Debug.appxmanifest'
$wxsPath = Join-Path $scriptDir 'BluetoothAudioReceiver.wxs'

function Update-File {
    param(
        [string]$Path,
        [scriptblock]$Transformer
    )

    $content = Get-Content -LiteralPath $Path -Raw
    $updated = & $Transformer $content
    if ($updated -ne $content) {
        Set-Content -LiteralPath $Path -Value $updated -Encoding utf8
    }
}

Update-File -Path $manifestPath -Transformer {
    param($content)
    [regex]::Replace(
        $content,
        '(?m)(<Identity\b[^>]*\sVersion=")([^"]+)(")',
        ('${1}' + $Version + '${3}'),
        1)
}

Update-File -Path $manifestDebugPath -Transformer {
    param($content)
    [regex]::Replace(
        $content,
        '(?m)(<Identity\b[^>]*\sVersion=")([^"]+)(")',
        ('${1}' + $Version + '${3}'),
        1)
}

Update-File -Path $wxsPath -Transformer {
    param($content)
    [regex]::Replace(
        $content,
        '(?s)(<Package\b[^>]*\sVersion=")([^"]+)(")',
        ('${1}' + $Version + '${3}'),
        1)
}
