param(
    [string]$Path = "GameData/PersistentSRBSmoke/PluginData/Settings.cfg"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Path)) {
    throw "Settings file not found: $Path"
}

$lines = Get-Content $Path
$codeLines = @()
foreach ($line in $lines) {
    $code = ($line -split '//', 2)[0].Trim()
    if ($code.Length -gt 0) {
        $codeLines += $code
    }
}

if ($codeLines.Count -lt 3) {
    throw "Settings.cfg is unexpectedly empty"
}

if ($codeLines[0] -ne "PERSISTENT_SRB_SMOKE") {
    throw "Expected top-level PERSISTENT_SRB_SMOKE node, got '$($codeLines[0])'"
}

$depth = 0
$seenOpeningBrace = $false
foreach ($line in $codeLines) {
    foreach ($ch in $line.ToCharArray()) {
        if ($ch -eq '{') {
            $depth++
            $seenOpeningBrace = $true
        }
        elseif ($ch -eq '}') {
            $depth--
            if ($depth -lt 0) {
                throw "Settings.cfg contains a closing brace without a matching opening brace"
            }
        }
    }

    if ($line -eq 'PERSISTENT_SRB_SMOKE' -or $line -eq '{' -or $line -eq '}') {
        continue
    }

    if ($line -notmatch '=') {
        throw "Invalid config line (expected key = value): $line"
    }

    $parts = $line -split '=', 2
    if ([string]::IsNullOrWhiteSpace($parts[0]) -or [string]::IsNullOrWhiteSpace($parts[1])) {
        throw "Invalid key/value pair: $line"
    }
}

if (-not $seenOpeningBrace -or $depth -ne 0) {
    throw "Settings.cfg has unbalanced braces (final depth=$depth)"
}

$requiredVolumetricKeys = @(
    'volumetricLightingEnabled',
    'volumetricScatteringForward',
    'volumetricScatteringBackward',
    'volumetricMultipleScattering',
    'volumetricSoftDepthFactor',
    'volumetricSunIntensity',
    'volumetricAmbientIntensity',
    'volumetricBeerPowderFactor'
)

$raw = Get-Content $Path -Raw
foreach ($key in $requiredVolumetricKeys) {
    if ($raw -notmatch "(?m)^\s*$([regex]::Escape($key))\s*=") {
        throw "Missing required volumetric setting: $key"
    }
}

Write-Host "Settings.cfg validation passed ($($requiredVolumetricKeys.Count) volumetric keys present)."
