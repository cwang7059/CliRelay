# Check what's listening on port 8317 and compare auth file formats
Write-Host "=== Checking port 8317 ===" -ForegroundColor Cyan
try {
    $conn = Get-NetTCPConnection -LocalPort 8317 -ErrorAction Stop
    $proc = Get-Process -Id $conn.OwningProcess[0]
    Write-Host "PID: $($proc.Id), Name: $($proc.ProcessName), Start: $($proc.StartTime)"
} catch {
    Write-Host "Port 8317 not in use or error: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Comparing auth file formats ===" -ForegroundColor Cyan

# A working file (visible in the UI)
$workingFile = "auths\codex-AmandaSullivan9463@outlook.com-free.json"
# Our converted file 
$convertedFile = "auths\codex-DanielRoss1693@outlook.com-free.json"

if (Test-Path $workingFile) {
    Write-Host ""
    Write-Host "[WORKING FILE] $workingFile" -ForegroundColor Green
    $working = Get-Content $workingFile -Raw | ConvertFrom-Json
    $workingKeys = ($working | Get-Member -MemberType NoteProperty).Name | Sort-Object
    Write-Host "Keys: $($workingKeys -join ', ')"
    Write-Host "account_id: $($working.account_id)"
    Write-Host "plan_type: $($working.plan_type)"
    Write-Host "type: $($working.type)"
    Write-Host "expired: $($working.expired)"
    Write-Host "last_refresh: $($working.last_refresh)"
    Write-Host "user_id: $($working.user_id)"
    Write-Host "disabled: $($working.disabled)"
} else {
    Write-Host "[WARN] Working file not found: $workingFile" -ForegroundColor Yellow
}

if (Test-Path $convertedFile) {
    Write-Host ""
    Write-Host "[CONVERTED FILE] $convertedFile" -ForegroundColor Yellow
    $converted = Get-Content $convertedFile -Raw | ConvertFrom-Json
    $convertedKeys = ($converted | Get-Member -MemberType NoteProperty).Name | Sort-Object
    Write-Host "Keys: $($convertedKeys -join ', ')"
    Write-Host "account_id: $($converted.account_id)"
    Write-Host "plan_type: $($converted.plan_type)"
    Write-Host "type: $($converted.type)"
    Write-Host "expired: $($converted.expired)"
    Write-Host "last_refresh: $($converted.last_refresh)"
    Write-Host "user_id: $($converted.user_id)"
    Write-Host "disabled: $($converted.disabled)"
}

# Show diff: keys in working but not in converted
if ((Test-Path $workingFile) -and (Test-Path $convertedFile)) {
    Write-Host ""
    Write-Host "=== Key Differences ===" -ForegroundColor Cyan
    $missingInConverted = $workingKeys | Where-Object { $_ -notin $convertedKeys }
    $extraInConverted = $convertedKeys | Where-Object { $_ -notin $workingKeys }
    if ($missingInConverted) {
        Write-Host "Keys in WORKING but MISSING in converted:" -ForegroundColor Red
        $missingInConverted | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    }
    if ($extraInConverted) {
        Write-Host "Keys in CONVERTED but not in working:" -ForegroundColor DarkGray
        $extraInConverted | ForEach-Object { Write-Host "  + $_" -ForegroundColor DarkGray }
    }
    if (-not $missingInConverted -and -not $extraInConverted) {
        Write-Host "Keys are identical" -ForegroundColor Green
    }
}
