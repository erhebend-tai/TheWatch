@echo off
setlocal EnableDelayedExpansion
REM ═══════════════════════════════════════════════════════════════════════════════
REM TheWatch — TeamCity Build Agent Setup (Windows)
REM ═══════════════════════════════════════════════════════════════════════════════
REM
REM Installs a TeamCity build agent on this machine and connects it to your
REM TeamCity server. The agent auto-detects .NET SDK, Java, and other tools.
REM
REM Prerequisites:
REM   - Java 17+ (for the TC agent JVM)
REM   - .NET 10 SDK
REM   - TeamCity server running and accessible
REM
REM Usage:
REM   .teamcity\setup-agent.cmd https://your-teamcity-server:8111
REM ═══════════════════════════════════════════════════════════════════════════════

if "%~1"=="" (
    echo Usage: setup-agent.cmd ^<TEAMCITY_SERVER_URL^>
    echo Example: setup-agent.cmd https://teamcity.example.com:8111
    exit /b 1
)

set "TC_SERVER=%~1"
set "AGENT_DIR=C:\TeamCity\buildAgent"
set "AGENT_NAME=%COMPUTERNAME%-thewatch"

echo [AGENT] TeamCity Build Agent Setup
echo [AGENT] Server: %TC_SERVER%
echo [AGENT] Agent dir: %AGENT_DIR%
echo [AGENT] Agent name: %AGENT_NAME%

REM ── Check Java ──────────────────────────────────────────────────────────────
java -version >nul 2>&1
if errorlevel 1 (
    echo [AGENT] Java not found. Installing via winget...
    winget install --id Microsoft.OpenJDK.17 --accept-package-agreements --accept-source-agreements
    if errorlevel 1 (
        echo [AGENT] Failed to install Java. Install Java 17+ manually.
        exit /b 1
    )
)

REM ── Download agent zip from TC server ───────────────────────────────────────
echo [AGENT] Downloading agent from %TC_SERVER%...
if not exist "%AGENT_DIR%" mkdir "%AGENT_DIR%"
curl -sL -o "%AGENT_DIR%\buildAgent.zip" "%TC_SERVER%/update/buildAgentFull.zip"
if errorlevel 1 (
    echo [AGENT] Failed to download agent. Check server URL and connectivity.
    exit /b 1
)

cd /d "%AGENT_DIR%"
echo [AGENT] Extracting...
tar -xf buildAgent.zip
del buildAgent.zip

REM ── Configure agent ─────────────────────────────────────────────────────────
echo [AGENT] Configuring agent...
copy /y conf\buildAgent.dist.properties conf\buildAgent.properties >nul

REM Set server URL
powershell -Command "(Get-Content conf\buildAgent.properties) -replace 'serverUrl=.*', 'serverUrl=%TC_SERVER%' | Set-Content conf\buildAgent.properties"

REM Set agent name
powershell -Command "Add-Content conf\buildAgent.properties 'name=%AGENT_NAME%'"

REM Add custom properties for TheWatch
powershell -Command "Add-Content conf\buildAgent.properties 'teamcity.agent.thewatch=true'"

REM ── Install as Windows service ──────────────────────────────────────────────
echo [AGENT] Installing as Windows service...
cd /d "%AGENT_DIR%\bin"
call service.install.bat
call service.start.bat

echo.
echo ══════════════════════════════════════════════════════════════
echo   TeamCity Build Agent Installed
echo ══════════════════════════════════════════════════════════════
echo   Name    : %AGENT_NAME%
echo   Server  : %TC_SERVER%
echo   Dir     : %AGENT_DIR%
echo   Service : TeamCity Build Agent (auto-start)
echo.
echo   The agent will appear in TeamCity ^> Agents ^> Unauthorized.
echo   Authorize it in the TeamCity UI to start accepting builds.
echo ══════════════════════════════════════════════════════════════
