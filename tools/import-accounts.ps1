# import-accounts-v2.ps1
# Convert and import accounts from export JSON into CliRelay auths directory
# Handles field mapping between export format and CliRelay's CodexTokenStorage format

param(
    [Parameter(Mandatory=$true)]
    [string]$InputFile,

    [string]$AuthsDir = "auths",

    [switch]$DryRun,

    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $InputFile)) {
    Write-Host "[ERROR] Input file not found: $InputFile" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $AuthsDir)) {
    Write-Host "[ERROR] Auths directory not found: $AuthsDir" -ForegroundColor Red
    exit 1
}

Write-Host "[INFO] Input file: $InputFile" -ForegroundColor Cyan
Write-Host "[INFO] Output dir: $AuthsDir" -ForegroundColor Cyan
if ($DryRun) {
    Write-Host "[INFO] DryRun mode - preview only" -ForegroundColor Yellow
}
if ($Force) {
    Write-Host "[INFO] Force mode - overwrite existing files" -ForegroundColor Yellow
}
Write-Host ""

$lines = Get-Content -Path $InputFile -Encoding UTF8

$imported = 0
$skipped = 0
$updated = 0
$errCount = 0
$total = 0

foreach ($rawLine in $lines) {
    $rawLine = $rawLine.Trim()
    if ([string]::IsNullOrWhiteSpace($rawLine)) { continue }
    $total++

    try {
        $src = $rawLine | ConvertFrom-Json

        $email = $src.email
        if ([string]::IsNullOrWhiteSpace($email)) {
            Write-Host "[WARN] Skipped: missing email field" -ForegroundColor Yellow
            $errCount++
            continue
        }

        $fileName = "codex-$email-free.json"
        $filePath = Join-Path $AuthsDir $fileName

        if ((Test-Path $filePath) -and (-not $Force)) {
            Write-Host "[SKIP] Already exists: $email (use -Force to overwrite)" -ForegroundColor DarkGray
            $skipped++
            continue
        }

        # --- Build the CliRelay-compatible auth object ---
        $auth = [ordered]@{}

        # Core required fields
        $auth["access_token"] = if ($src.access_token) { $src.access_token } else { "" }

        # account_id: map from chatgpt_account_id
        $accountId = ""
        if ($src.chatgpt_account_id) { $accountId = $src.chatgpt_account_id }
        elseif ($src.account_id) { $accountId = $src.account_id }
        $auth["account_id"] = $accountId

        $auth["account_plan_type"] = "free"
        $auth["account_structure"] = "personal"

        # Preserve extra fields from source
        if ($src.chatgpt_account_id) { $auth["chatgpt_account_id"] = $src.chatgpt_account_id }
        if ($src.chatgpt_user_id)    { $auth["chatgpt_user_id"] = $src.chatgpt_user_id }
        if ($src.client_id)          { $auth["client_id"] = $src.client_id }
        if ($src.created_at) {
            $refDate = (Get-Date "1970-01-01T00:00:00Z").AddSeconds($src.created_at)
            $auth["created_at"] = $refDate.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:sszzz")
        } else {
            $auth["created_at"] = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
        }
        if ($src.db_id)              { $auth["db_id"] = $src.db_id }

        # disabled must be explicitly false for CliRelay to load the account
        $auth["disabled"] = $false

        $auth["email"] = $email

        # expired: calculate from access_token JWT exp claim
        $expiredStr = ""
        if ($src.expired) {
            $expiredStr = $src.expired
        } elseif ($src.access_token -and $src.access_token.Contains(".")) {
            try {
                $parts = $src.access_token.Split(".")
                $payload = $parts[1]
                # Fix base64 padding
                $mod = $payload.Length % 4
                if ($mod -eq 2) { $payload += "==" }
                elseif ($mod -eq 3) { $payload += "=" }
                $payload = $payload.Replace("-", "+").Replace("_", "/")
                $jsonBytes = [System.Convert]::FromBase64String($payload)
                $jsonStr = [System.Text.Encoding]::UTF8.GetString($jsonBytes)
                $jwt = $jsonStr | ConvertFrom-Json
                if ($jwt.exp) {
                    $expDate = (Get-Date "1970-01-01T00:00:00Z").AddSeconds($jwt.exp)
                    $expiredStr = $expDate.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:sszzz")
                }
            } catch {
                # fallback: 10 days from now
                $expiredStr = (Get-Date).AddDays(10).ToString("yyyy-MM-ddTHH:mm:sszzz")
            }
        }
        if ([string]::IsNullOrWhiteSpace($expiredStr)) {
            $expiredStr = (Get-Date).AddDays(10).ToString("yyyy-MM-ddTHH:mm:sszzz")
        }
        $auth["expired"] = $expiredStr

        $auth["id_token"] = if ($src.id_token) { $src.id_token } else { "" }

        # last_refresh: use last_used timestamp, created_at, or now
        $lastRefreshStr = ""
        if ($src.last_refresh) {
            $lastRefreshStr = $src.last_refresh
        } elseif ($src.last_used) {
            $refDate = (Get-Date "1970-01-01T00:00:00Z").AddSeconds($src.last_used)
            $lastRefreshStr = $refDate.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:sszzz")
        } elseif ($src.created_at) {
            $refDate = (Get-Date "1970-01-01T00:00:00Z").AddSeconds($src.created_at)
            $lastRefreshStr = $refDate.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:sszzz")
        } else {
            $lastRefreshStr = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
        }
        $auth["last_refresh"] = $lastRefreshStr

        if ($src.last_used) {
            $refDate = (Get-Date "1970-01-01T00:00:00Z").AddSeconds($src.last_used)
            $auth["last_used"] = $refDate.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:sszzz")
        } else {
            $auth["last_used"] = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
        }
        if ($src.login_identity)     { $auth["login_identity"] = $src.login_identity }

        # Mailbox info (preserve if present)
        if ($src.mailbox)            { $auth["mailbox"] = $src.mailbox }
        if ($src.mailbox_connection) { $auth["mailbox_connection"] = $src.mailbox_connection }
        if ($src.mailbox_url)        { $auth["mailbox_url"] = $src.mailbox_url }

        if ($src.organization_id)    { $auth["organization_id"] = $src.organization_id }
        if ($src.password)           { $auth["password"] = $src.password }
        if ($src.phone)              { $auth["phone"] = $src.phone }

        $auth["plan_type"] = "free"

        if ($src.platform)           { $auth["platform"] = $src.platform }
        if ($src.project_id)         { $auth["project_id"] = $src.project_id }

        $auth["refresh_token"] = if ($src.refresh_token) { $src.refresh_token } else { "" }

        # session_token must be present (even if empty)
        $auth["session_token"] = if ($null -ne $src.session_token) { $src.session_token } else { "" }
        if ($src.source)             { $auth["source"] = $src.source }
        if ($src.status)             { $auth["status"] = $src.status }

        $auth["type"] = "codex"

        # user_id: map from chatgpt_user_id
        $userId = ""
        if ($src.chatgpt_user_id) { $userId = $src.chatgpt_user_id }
        elseif ($src.user_id) { $userId = $src.user_id }
        if ($userId) { $auth["user_id"] = $userId }

        if ($src.version)            { $auth["version"] = $src.version }
        if ($src.workspace_id)       { $auth["workspace_id"] = $src.workspace_id }
        if ($src.account_claims_email) { $auth["account_claims_email"] = $src.account_claims_email }

        # Convert to JSON
        $outputJson = $auth | ConvertTo-Json -Depth 10 -Compress

        $isUpdate = Test-Path $filePath

        if ($DryRun) {
            $action = if ($isUpdate) { "UPDATE" } else { "IMPORT" }
            Write-Host "[DRY][$action] $email -> $fileName (account_id=$accountId)" -ForegroundColor Green
        } else {
            [System.IO.File]::WriteAllText(
                $filePath,
                ($outputJson + "`n"),
                [System.Text.UTF8Encoding]::new($false)
            )
            if ($isUpdate) {
                Write-Host "[UPDATED] $email -> $fileName" -ForegroundColor Yellow
                $updated++
            } else {
                Write-Host "[OK] $email -> $fileName" -ForegroundColor Green
            }
        }
        $imported++

    } catch {
        Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
        $errCount++
    }
}

Write-Host ""
Write-Host ("=" * 60)
Write-Host "[SUMMARY]" -ForegroundColor Cyan
Write-Host "  New imports: $($imported - $updated)" -ForegroundColor Green
if ($Force) {
    Write-Host "  Updated: $updated" -ForegroundColor Yellow
}
Write-Host "  Skipped: $skipped" -ForegroundColor DarkGray
Write-Host "  Errors: $errCount" -ForegroundColor Red
Write-Host "  Total: $total" -ForegroundColor Cyan
