param(
    [string]$Config = "config.local.yaml",
    [int]$Port = 8317,
    [string]$ManagementKey = $env:CLIRELAY_MANAGEMENT_KEY,
    [switch]$Restart,
    [switch]$Foreground,
    [switch]$SkipCodexConfig
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $Root $Config
$OutLog = Join-Path $Root ".clirelay-dev.out.log"
$ErrLog = Join-Path $Root ".clirelay-dev.err.log"
$BinPath = Join-Path $Root "bin\clirelay-server.exe"

function Get-ListeningProcess {
    param([int]$LocalPort)

    $connections = Get-NetTCPConnection -LocalPort $LocalPort -State Listen -ErrorAction SilentlyContinue
    if (-not $connections) {
        return $null
    }

    $pidValue = ($connections | Select-Object -First 1).OwningProcess
    if (-not $pidValue) {
        return $null
    }

    Get-Process -Id $pidValue -ErrorAction SilentlyContinue
}

function Get-GoExecutable {
    $go = Get-Command go -ErrorAction SilentlyContinue
    if ($go) {
        return $go.Source
    }

    $candidateRoots = @()
    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $candidateRoots += Join-Path $env:USERPROFILE ".local"
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidateRoots += $env:LOCALAPPDATA
    }

    foreach ($candidateRoot in $candidateRoots) {
        if (-not (Test-Path -LiteralPath $candidateRoot)) {
            continue
        }

        $candidate = Get-ChildItem -LiteralPath $candidateRoot -Directory -Filter "go*" -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName "go\bin\go.exe" } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Sort-Object -Descending |
            Select-Object -First 1
        if ($candidate) {
            return $candidate
        }
    }

    return $null
}

function Find-TomlSectionEnd {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [int]$StartIndex
    )

    for ($i = $StartIndex + 1; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match '^\s*\[') {
            return $i
        }
    }

    return $Lines.Count
}

function Ensure-TopLevelTomlSetting {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [string]$Key,
        [string]$SettingLine
    )

    $topEnd = $Lines.Count
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match '^\s*\[') {
            $topEnd = $i
            break
        }
    }

    $keyPattern = '^\s*' + [regex]::Escape($Key) + '\s*='
    for ($i = 0; $i -lt $topEnd; $i++) {
        if ($Lines[$i] -match $keyPattern) {
            if ($Lines[$i] -ne $SettingLine) {
                $Lines[$i] = $SettingLine
            }
            return
        }
    }

    $insertAt = 0
    for ($i = 0; $i -lt $topEnd; $i++) {
        if ($Lines[$i] -match '^\s*model\s*=') {
            $insertAt = $i + 1
            break
        }
    }

    $Lines.Insert($insertAt, $SettingLine)
}

function Find-TomlSection {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [string]$SectionPattern
    )

    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match $SectionPattern) {
            return $i
        }
    }

    return -1
}

function Test-TomlSectionHasKey {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [int]$SectionStart,
        [string]$Key
    )

    $sectionEnd = Find-TomlSectionEnd -Lines $Lines -StartIndex $SectionStart
    $keyPattern = '^\s*' + [regex]::Escape($Key) + '\s*='

    for ($i = $SectionStart + 1; $i -lt $sectionEnd; $i++) {
        if ($Lines[$i] -match $keyPattern) {
            return $true
        }
    }

    return $false
}

function Ensure-TomlSectionSetting {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [int]$SectionStart,
        [string]$Key,
        [string]$SettingLine
    )

    $sectionEnd = Find-TomlSectionEnd -Lines $Lines -StartIndex $SectionStart
    $keyPattern = '^\s*' + [regex]::Escape($Key) + '\s*='

    for ($i = $SectionStart + 1; $i -lt $sectionEnd; $i++) {
        if ($Lines[$i] -match $keyPattern) {
            if ($Lines[$i] -ne $SettingLine) {
                $Lines[$i] = $SettingLine
            }
            return
        }
    }

    $Lines.Insert($sectionEnd, $SettingLine)
}

function Get-TomlSectionStringValue {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [int]$SectionStart,
        [string]$Key
    )

    if ($SectionStart -lt 0) {
        return ""
    }

    $sectionEnd = Find-TomlSectionEnd -Lines $Lines -StartIndex $SectionStart
    $keyPattern = '^\s*' + [regex]::Escape($Key) + '\s*=\s*["'']([^"'']*)["'']'
    for ($i = $SectionStart + 1; $i -lt $sectionEnd; $i++) {
        if ($Lines[$i] -match $keyPattern) {
            return $Matches[1]
        }
    }

    return ""
}

function Find-SqliteExecutable {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:SQLITE3)) {
        $candidates += $env:SQLITE3
    }

    $candidates += Join-Path $Root "sqlite3.exe"
    $parent = Split-Path -Parent $Root
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        $candidates += Join-Path $parent "tools\platform-tools\sqlite3.exe"
    }

    $sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if ($sqlite) {
        $candidates += $sqlite.Source
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Format-MaskedKey {
    param([string]$Key)

    if ([string]::IsNullOrWhiteSpace($Key)) {
        return ""
    }
    if ($Key.Length -gt 8) {
        return $Key.Substring(0, 4) + "..." + $Key.Substring($Key.Length - 4)
    }
    return "***"
}

function Ensure-CliRelayClientAPIKey {
    param([string]$ApiKey)

    if ([string]::IsNullOrWhiteSpace($ApiKey)) {
        return
    }

    $dbPath = Join-Path $Root "data\usage.db"
    if (-not (Test-Path -LiteralPath $dbPath)) {
        Write-Warning "CliRelay usage database does not exist yet; cannot pre-enable Codex API key."
        return
    }

    $sqlite = Find-SqliteExecutable
    if (-not $sqlite) {
        Write-Warning "sqlite3 was not found; cannot verify the Codex API key in CliRelay's api_keys table."
        return
    }

    $escapedKey = $ApiKey.Replace("'", "''")
    $now = (Get-Date).ToUniversalTime().ToString("o")
    $sql = @"
INSERT INTO api_keys
  (key, name, disabled, daily_limit, total_quota, spending_limit,
   concurrency_limit, rpm_limit, tpm_limit, allowed_models, allowed_channels,
   allowed_channel_groups, system_prompt, created_at, updated_at)
VALUES
  ('$escapedKey', 'VS Code Codex via CliRelay', 0, 0, 0, 0, 0, 0, 0, '[]', '[]', '[]', '', '$now', '$now')
ON CONFLICT(key) DO UPDATE SET
  disabled = 0,
  name = CASE
    WHEN trim(coalesce(name, '')) = '' THEN excluded.name
    ELSE name
  END,
  updated_at = excluded.updated_at;
"@

    & $sqlite $dbPath $sql
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to upsert CliRelay Codex API key into $dbPath"
    }

    Write-Host "CliRelay API key enabled for Codex: $(Format-MaskedKey -Key $ApiKey)"
}

function Ensure-CodexCliRelayConfig {
    param(
        [int]$LocalPort,
        [switch]$Skip
    )

    if ($Skip -or $env:CLIRELAY_SKIP_CODEX_CONFIG -eq "1") {
        Write-Host "Skipping Codex config switch because it was disabled."
        return ""
    }

    $codexHome = $env:CODEX_HOME
    if ([string]::IsNullOrWhiteSpace($codexHome)) {
        if ([string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
            Write-Warning "Cannot locate Codex config: USERPROFILE is not set."
            return ""
        }

        $codexHome = Join-Path $env:USERPROFILE ".codex"
    }

    $codexConfig = Join-Path $codexHome "config.toml"
    $codexConfigDir = Split-Path -Parent $codexConfig
    if (-not (Test-Path -LiteralPath $codexConfigDir)) {
        New-Item -ItemType Directory -Path $codexConfigDir -Force | Out-Null
    }

    $content = ""
    if (Test-Path -LiteralPath $codexConfig) {
        $content = [System.IO.File]::ReadAllText($codexConfig)
    }

    $newline = "`n"
    if ($content -match "`r`n") {
        $newline = "`r`n"
    }

    $lines = New-Object 'System.Collections.Generic.List[string]'
    if ($content.Length -gt 0) {
        foreach ($line in ($content -split "`r?`n", -1)) {
            $lines.Add($line)
        }
    }

    Ensure-TopLevelTomlSetting `
        -Lines $lines `
        -Key "model_provider" `
        -SettingLine 'model_provider = "clirelay"'

    $baseUrl = "http://localhost:$LocalPort/v1"
    $clirelaySectionPattern = '^\s*\[model_providers\.("clirelay"|''clirelay''|clirelay)\]\s*(#.*)?$'
    $sectionStart = Find-TomlSection -Lines $lines -SectionPattern $clirelaySectionPattern
    $codexBearerToken = ""

    if ($sectionStart -lt 0) {
        if ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -ne "") {
            $lines.Add("")
        }

        $lines.Add("[model_providers.clirelay]")
        $lines.Add('name = "CliRelay"')
        $lines.Add("base_url = `"$baseUrl`"")
        $lines.Add('wire_api = "responses"')
        $lines.Add("supports_websockets = false")
        $lines.Add('experimental_bearer_token = "local-dev-key"')
        $codexBearerToken = "local-dev-key"
    } else {
        Ensure-TomlSectionSetting -Lines $lines -SectionStart $sectionStart -Key "name" -SettingLine 'name = "CliRelay"'
        Ensure-TomlSectionSetting -Lines $lines -SectionStart $sectionStart -Key "base_url" -SettingLine "base_url = `"$baseUrl`""
        Ensure-TomlSectionSetting -Lines $lines -SectionStart $sectionStart -Key "wire_api" -SettingLine 'wire_api = "responses"'
        Ensure-TomlSectionSetting -Lines $lines -SectionStart $sectionStart -Key "supports_websockets" -SettingLine "supports_websockets = false"

        $hasAuth = (Test-TomlSectionHasKey -Lines $lines -SectionStart $sectionStart -Key "experimental_bearer_token") -or
            (Test-TomlSectionHasKey -Lines $lines -SectionStart $sectionStart -Key "env_key") -or
            (Test-TomlSectionHasKey -Lines $lines -SectionStart $sectionStart -Key "requires_openai_auth")
        if (-not $hasAuth) {
            $sectionEnd = Find-TomlSectionEnd -Lines $lines -StartIndex $sectionStart
            $lines.Insert($sectionEnd, 'experimental_bearer_token = "local-dev-key"')
            $codexBearerToken = "local-dev-key"
        } else {
            $codexBearerToken = Get-TomlSectionStringValue -Lines $lines -SectionStart $sectionStart -Key "experimental_bearer_token"
        }
    }

    $newContent = [string]::Join($newline, $lines)
    if ($newContent -eq $content) {
        Write-Host "Codex config already points at CliRelay: $codexConfig"
        return $codexBearerToken
    }

    if (Test-Path -LiteralPath $codexConfig) {
        $backupPath = "$codexConfig.$(Get-Date -Format 'yyyyMMdd-HHmmss').bak"
        Copy-Item -LiteralPath $codexConfig -Destination $backupPath
        Write-Host "Backed up Codex config: $backupPath"
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($codexConfig, $newContent, $utf8NoBom)
    Write-Host "Codex config now uses CliRelay: $codexConfig"
    return $codexBearerToken
}

if (-not (Test-Path -LiteralPath $ConfigPath)) {
    throw "Config file not found: $ConfigPath"
}

$codexClientAPIKey = Ensure-CodexCliRelayConfig -LocalPort $Port -Skip:$SkipCodexConfig

Set-Location -LiteralPath $Root

$listener = Get-ListeningProcess -LocalPort $Port
if ($listener) {
    if (-not $Restart) {
        Write-Host "CliRelay appears to be running on port $Port. PID=$($listener.Id), Process=$($listener.ProcessName)"
        Write-Host "Use -Restart to stop it and start again."
        exit 0
    }

    Write-Host "Stopping existing process on port $Port. PID=$($listener.Id), Process=$($listener.ProcessName)"
    Stop-Process -Id $listener.Id -Force
    Start-Sleep -Seconds 1
}

if (-not $SkipCodexConfig) {
    Ensure-CliRelayClientAPIKey -ApiKey $codexClientAPIKey
}

if (Test-Path -LiteralPath $BinPath) {
    $FilePath = $BinPath
    $ArgumentList = @("-config", $Config)
} else {
    $go = Get-GoExecutable
    if (-not $go) {
        throw "Go was not found in PATH and $BinPath does not exist. Install Go or build the server binary first."
    }

    $FilePath = $go
    $ArgumentList = @("run", ".\cmd\server", "-config", $Config)
}

if (-not [string]::IsNullOrWhiteSpace($ManagementKey)) {
    $ArgumentList += @("-password", $ManagementKey)
}

if ($Foreground) {
    Write-Host "Starting CliRelay in foreground with config $Config..."
    & $FilePath @ArgumentList
    exit $LASTEXITCODE
}

$process = Start-Process `
    -FilePath $FilePath `
    -ArgumentList $ArgumentList `
    -WorkingDirectory $Root `
    -RedirectStandardOutput $OutLog `
    -RedirectStandardError $ErrLog `
    -WindowStyle Hidden `
    -PassThru

Write-Host "CliRelay started. PID=$($process.Id), Port=$Port"
Write-Host "Config: $ConfigPath"
if (-not [string]::IsNullOrWhiteSpace($ManagementKey)) {
    Write-Host "Local management key: $ManagementKey"
}
Write-Host "Logs:   $OutLog"
Write-Host "Errors: $ErrLog"
