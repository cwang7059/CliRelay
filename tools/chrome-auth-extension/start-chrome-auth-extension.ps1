$ErrorActionPreference = "Stop"

$script = Join-Path $PSScriptRoot "start-clirelay-oauth-auto.js"
node $script
