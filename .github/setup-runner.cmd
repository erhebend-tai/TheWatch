@echo off
REM ═══════════════════════════════════════════════════════════════════════════════
REM TheWatch — GitHub Actions Self-Hosted Runner Setup (Windows)
REM ═══════════════════════════════════════════════════════════════════════════════
REM
REM Installs and configures a GitHub Actions self-hosted runner on this machine.
REM The runner will pick up jobs tagged with 'runs-on: self-hosted' or 'windows-self'.
REM
REM Prerequisites:
REM   - .NET 10 SDK installed
REM   - GitHub PAT with 'repo' scope (or fine-grained with 'Actions' permission)
REM
REM Usage:
REM   .github\setup-runner.cmd YOUR_GITHUB_TOKEN
REM ═══════════════════════════════════════════════════════════════════════════════

if "%~1"=="" (
    echo Usage: setup-runner.cmd ^<GITHUB_PAT^>
    echo.
    echo Get a token at: https://github.com/settings/tokens
    echo Required scope: repo (or fine-grained: Actions read/write)
    exit /b 1
)

set "RUNNER_DIR=C:\actions-runner"
set "REPO=erhebend-tai/TheWatch"
set "TOKEN=%~1"

echo [RUNNER] Setting up GitHub Actions self-hosted runner...
echo [RUNNER] Repository: %REPO%
echo [RUNNER] Install dir: %RUNNER_DIR%

REM ── Get latest runner version ───────────────────────────────────────────────
echo [RUNNER] Downloading latest runner...
if not exist "%RUNNER_DIR%" mkdir "%RUNNER_DIR%"
cd /d "%RUNNER_DIR%"

REM Download runner package (latest win-x64)
curl -sL -o actions-runner-win-x64.zip https://github.com/actions/runner/releases/download/v2.321.0/actions-runner-win-x64-2.321.0.zip
if errorlevel 1 (
    echo [RUNNER] Failed to download runner. Check your internet connection.
    exit /b 1
)

REM Extract
echo [RUNNER] Extracting...
tar -xf actions-runner-win-x64.zip
del actions-runner-win-x64.zip

REM ── Get registration token from GitHub API ──────────────────────────────────
echo [RUNNER] Getting registration token...
for /f "usebackq delims=" %%A in (`curl -sX POST -H "Authorization: token %TOKEN%" -H "Accept: application/vnd.github+json" https://api.github.com/repos/%REPO%/actions/runners/registration-token ^| findstr /C:"token"`) do (
    for /f "tokens=2 delims=:, " %%B in ("%%A") do set "REG_TOKEN=%%~B"
)

if "!REG_TOKEN!"=="" (
    echo [RUNNER] Failed to get registration token. Check your PAT permissions.
    exit /b 1
)

REM ── Configure runner ────────────────────────────────────────────────────────
echo [RUNNER] Configuring runner...
config.cmd --url https://github.com/%REPO% --token %REG_TOKEN% --name "%COMPUTERNAME%-thewatch" --labels "windows-self,thewatch,dotnet10" --work "_work" --runasservice

echo.
echo ══════════════════════════════════════════════════════════════
echo   GitHub Actions Runner Installed
echo ══════════════════════════════════════════════════════════════
echo   Name   : %COMPUTERNAME%-thewatch
echo   Labels : windows-self, thewatch, dotnet10
echo   Dir    : %RUNNER_DIR%
echo   Service: Installed as Windows service (auto-start)
echo.
echo   To use in workflows:
echo     runs-on: [self-hosted, windows-self, thewatch]
echo ══════════════════════════════════════════════════════════════
