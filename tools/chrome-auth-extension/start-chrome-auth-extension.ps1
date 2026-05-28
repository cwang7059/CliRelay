$ErrorActionPreference = "Stop"

$extensionPath = $PSScriptRoot
$profilePath = Join-Path $extensionPath ".chrome-profile"

if (-not (Test-Path -LiteralPath $profilePath)) {
  New-Item -ItemType Directory -Path $profilePath | Out-Null
}

$firstRunMarker = Join-Path $profilePath "First Run"
if (-not (Test-Path -LiteralPath $firstRunMarker)) {
  New-Item -ItemType File -Path $firstRunMarker | Out-Null
}

$chromeCandidates = @(
  "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
  "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
  "$env:LocalAppData\Google\Chrome\Application\chrome.exe"
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

if (-not $chromeCandidates -or $chromeCandidates.Count -eq 0) {
  throw "chrome.exe was not found. Install Chrome or load this folder manually in chrome://extensions/."
}

$chrome = @($chromeCandidates)[0]
$arguments = @(
  "--user-data-dir=$profilePath",
  "--no-first-run",
  "--no-default-browser-check",
  "--disable-sync",
  "--disable-features=ChromeWhatsNewUI,SigninInterception,SignInProfileCreation",
  "--load-extension=$extensionPath",
  "--disable-extensions-except=$extensionPath",
  "chrome://extensions/"
)

Start-Process -FilePath $chrome -ArgumentList $arguments
