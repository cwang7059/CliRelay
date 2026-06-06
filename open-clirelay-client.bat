@echo off
setlocal

cd /d "%~dp0"

set "PORT=8317"
set "BACKEND_BASE=http://127.0.0.1:%PORT%"
set "MANAGE_URL=%BACKEND_BASE%/manage"
set "LOCAL_ELECTRON_ROOT=%~dp0..\codeProxy-electron-api-key-reveal"
set "ELECTRON_ROOT=%~dp0..\codeProxy"
if exist "%LOCAL_ELECTRON_ROOT%\scripts\electron-preview.mjs" (
    set "ELECTRON_ROOT=%LOCAL_ELECTRON_ROOT%"
)
set "ELECTRON_EXE=%ELECTRON_ROOT%\release\electron\win-unpacked\Code Proxy Admin.exe"
set "ELECTRON_BAT=%ELECTRON_ROOT%\start-electron-admin.bat"
set "ELECTRON_CMD=%ELECTRON_ROOT%\start-electron-admin.cmd"
set "ELECTRON_PREVIEW=%ELECTRON_ROOT%\scripts\electron-preview.mjs"

call :StartBackend
if errorlevel 1 exit /b 1

call :CloseOldClientWindows

set "CODE_PROXY_API_BASE=%BACKEND_BASE%"
set "CODE_PROXY_ADMIN_URL="
set "ELECTRON_RENDERER_URL="

if /I "%ELECTRON_ROOT%"=="%LOCAL_ELECTRON_ROOT%" if exist "%USERPROFILE%\.bun\bin\bun.exe" if exist "%ELECTRON_PREVIEW%" (
    echo Building and opening latest Electron client from local feature worktree...
    "%USERPROFILE%\.bun\bin\bun.exe" run build
    if errorlevel 1 exit /b 1
    "%USERPROFILE%\.bun\bin\bun.exe" scripts\electron-preview.mjs
    if errorlevel 1 exit /b 1
    exit /b 0
)

if exist "%ELECTRON_BAT%" (
    echo Building and opening latest Electron client...
    call "%ELECTRON_BAT%" -Rebuild -BackendBase "%BACKEND_BASE%"
    if errorlevel 1 exit /b 1
    exit /b 0
)

if exist "%ELECTRON_CMD%" (
    echo Building and opening latest Electron client via codeProxy launcher...
    call "%ELECTRON_CMD%" -Rebuild -BackendBase "%BACKEND_BASE%"
    if errorlevel 1 exit /b 1
    exit /b 0
)

if exist "%ELECTRON_EXE%" (
    echo Opening packaged Electron client...
    start "" /D "%ELECTRON_ROOT%\release\electron\win-unpacked" "%ELECTRON_EXE%"
    exit /b 0
)

echo.
echo Electron client was not found at:
echo %ELECTRON_ROOT%
echo Opening web client instead:
echo %MANAGE_URL%
start "" "%MANAGE_URL%"
exit /b 0

:StartBackend
echo Starting CliRelay backend if needed...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-clirelay.ps1"
if errorlevel 1 (
    echo.
    echo Failed to start CliRelay backend.
    echo Check start-clirelay.ps1 output above.
    pause
    exit /b 1
)
powershell -NoProfile -Command "Start-Sleep -Seconds 1" >nul
exit /b 0

:CloseOldClientWindows
echo Closing old Electron client windows...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$roots = @('%ELECTRON_ROOT%', '%LOCAL_ELECTRON_ROOT%') | ForEach-Object { try { (Resolve-Path -LiteralPath $_ -ErrorAction Stop).ProviderPath.TrimEnd('\') } catch { $null } } | Where-Object { $_ }; Get-CimInstance Win32_Process | Where-Object { @('Code Proxy Admin.exe', 'electron.exe') -contains $_.Name } | Where-Object { $exe = [string]$_.ExecutablePath; $cmd = [string]$_.CommandLine; $matched = $false; foreach ($root in $roots) { if (($exe -and $exe.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) -or ($cmd -and $cmd.IndexOf($root, [StringComparison]::OrdinalIgnoreCase) -ge 0)) { $matched = $true; break } }; $matched } | ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch {} }"
powershell -NoProfile -Command "Start-Sleep -Milliseconds 500" >nul
exit /b 0

