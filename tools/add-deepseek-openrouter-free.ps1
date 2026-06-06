[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ApiKey = $env:OPENROUTER_API_KEY,
    [string]$ProviderName = "deepseek-openrouter-free",
    [string]$BaseUrl = "https://openrouter.ai/api/v1",
    [string]$Prefix = "deepseek",
    [string]$Alias = "deepseek-free",
    [switch]$DisabledTemplate,
    [switch]$Restart
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$DatabasePath = Join-Path $Root "data\usage.db"

function Find-SqliteExecutable {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:SQLITE3)) {
        $candidates += $env:SQLITE3
    }
    $candidates += Join-Path $Root "sqlite3.exe"
    $candidates += Join-Path (Split-Path -Parent $Root) "tools\platform-tools\sqlite3.exe"

    $sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if ($sqlite) {
        $candidates += $sqlite.Source
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    throw "sqlite3 was not found. Set SQLITE3 or install sqlite3."
}

function Escape-SqlLiteral {
    param([string]$Value)
    return $Value -replace "'", "''"
}

function Read-OpenAICompatibilityPayload {
    param(
        [string]$Sqlite,
        [string]$DbPath
    )

    $payload = & $Sqlite $DbPath "SELECT payload FROM runtime_settings WHERE setting_key='openai-compatibility';"
    if ($LASTEXITCODE -ne 0) {
        throw "failed to read runtime_settings.openai-compatibility"
    }
    if ([string]::IsNullOrWhiteSpace($payload)) {
        return @()
    }

    $parsed = $payload | ConvertFrom-Json
    if ($null -eq $parsed) {
        return @()
    }
    if ($parsed -is [array]) {
        return @($parsed)
    }
    return @($parsed)
}

if (-not (Test-Path -LiteralPath $DatabasePath)) {
    throw "usage database not found: $DatabasePath"
}

if ([string]::IsNullOrWhiteSpace($ApiKey) -and -not $DisabledTemplate) {
    throw "Set -ApiKey or OPENROUTER_API_KEY. Use -DisabledTemplate only if you want to create an inactive template."
}

$sqlitePath = Find-SqliteExecutable
$providers = Read-OpenAICompatibilityPayload -Sqlite $sqlitePath -DbPath $DatabasePath

$keyEntry = [ordered]@{}
if ($DisabledTemplate) {
    $keyEntry["api-key"] = "OPENROUTER_API_KEY_HERE"
    $keyEntry["disabled"] = $true
} else {
    $keyEntry["api-key"] = $ApiKey.Trim()
}

$deepseekProvider = [ordered]@{
    name = $ProviderName.Trim()
    "base-url" = $BaseUrl.Trim()
    "api-key-entries" = @($keyEntry)
    models = @(
        [ordered]@{
            name = "deepseek/deepseek-v4-flash:free"
            alias = $Alias.Trim()
        },
        [ordered]@{
            name = "deepseek/deepseek-v4-flash:free"
            alias = "deepseek-v4-flash-free"
        }
    )
}
if (-not [string]::IsNullOrWhiteSpace($Prefix)) {
    $deepseekProvider["prefix"] = $Prefix.Trim()
}

$next = New-Object System.Collections.Generic.List[object]
foreach ($provider in $providers) {
    $name = ""
    if ($provider.PSObject.Properties["name"]) {
        $name = [string]$provider.PSObject.Properties["name"].Value
    }
    if (-not [string]::Equals($name.Trim(), $ProviderName.Trim(), [System.StringComparison]::OrdinalIgnoreCase)) {
        $next.Add($provider)
    }
}
$next.Add($deepseekProvider)

$json = ConvertTo-Json -InputObject @($next.ToArray()) -Depth 16 -Compress
$escapedJson = Escape-SqlLiteral $json
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$sql = @"
INSERT INTO runtime_settings(setting_key, payload, updated_at)
VALUES('openai-compatibility', '$escapedJson', '$now')
ON CONFLICT(setting_key) DO UPDATE SET payload = excluded.payload, updated_at = excluded.updated_at;
"@

if ($PSCmdlet.ShouldProcess($DatabasePath, "upsert $ProviderName OpenAI-compatible provider")) {
    & $sqlitePath $DatabasePath $sql
    if ($LASTEXITCODE -ne 0) {
        throw "failed to update runtime_settings.openai-compatibility"
    }

    if ($DisabledTemplate) {
        Write-Host "DeepSeek OpenRouter Free template added disabled. Replace OPENROUTER_API_KEY_HERE and enable it in AI Providers."
    } else {
        Write-Host "DeepSeek OpenRouter Free provider added: $ProviderName / $Alias"
    }

    if ($Restart) {
        & (Join-Path $Root "start-clirelay.ps1") -Restart
    } else {
        Write-Host "Restart CliRelay to apply the runtime setting, or run this script again with -Restart."
    }
}
