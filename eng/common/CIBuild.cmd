@echo off
REM ═══════════════════════════════════════════════════════════════════════════════
REM TheWatch — CI Build Entry Point (Windows)
REM ═══════════════════════════════════════════════════════════════════════════════
REM Arcade convention: eng/common/CIBuild.cmd is the standard CI entry point.
REM Delegates to the root build.cmd with version parameters.
REM
REM Example:
REM   eng\common\CIBuild.cmd                           (default ci target)
REM   eng\common\CIBuild.cmd -buildId 42               (explicit build ID)
REM   eng\common\CIBuild.cmd -release                  (stable release build)
REM ═══════════════════════════════════════════════════════════════════════════════

setlocal EnableDelayedExpansion
set "REPO_ROOT=%~dp0..\.."

REM Default to GitHub Actions run number if available
if "%BUILD_ID%"=="" (
    if defined GITHUB_RUN_NUMBER set "BUILD_ID=%GITHUB_RUN_NUMBER%"
)
if "%BUILD_ID%"=="" set "BUILD_ID=0"

REM Parse arguments
set "EXTRA_ARGS="
:parse
if "%~1"=="" goto :run
if /i "%~1"=="-buildId" (
    set "BUILD_ID=%~2"
    shift & shift
    goto :parse
)
if /i "%~1"=="-release" (
    set "EXTRA_ARGS=%EXTRA_ARGS% -p:DotNetFinalVersionKind=release"
    shift
    goto :parse
)
shift
goto :parse

:run
echo [CI] OfficialBuild=true, BuildId=%BUILD_ID%
call "%REPO_ROOT%\build.cmd" ci
