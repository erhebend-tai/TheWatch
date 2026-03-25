@echo off
setlocal EnableDelayedExpansion
REM ═══════════════════════════════════════════════════════════════════════════════
REM TheWatch — Unified Build Script (Windows)
REM ═══════════════════════════════════════════════════════════════════════════════
REM
REM Shared by GitHub Actions, TeamCity, Azure DevOps, and local dev.
REM Ensures identical build behavior regardless of CI platform.
REM
REM Usage:
REM   build.cmd restore        Restore NuGet packages
REM   build.cmd build          Build solution (Release)
REM   build.cmd test           Run unit tests (excludes Integration)
REM   build.cmd test-integ     Run integration tests (requires Azure creds)
REM   build.cmd publish        Publish deployable artifacts
REM   build.cmd pack           Create NuGet packages for shared libraries
REM   build.cmd audit-verify   Verify audit trail Merkle chain integrity
REM   build.cmd all            restore + build + test + publish
REM   build.cmd ci             Full CI pipeline: restore + build + test + publish
REM ═══════════════════════════════════════════════════════════════════════════════

set "SOLUTION=TheWatch.slnx"
set "CONFIG=Release"
set "ARTIFACTS=%~dp0artifacts"
set "TEST_RESULTS=%~dp0test-results"

REM ── Version Parameters (set by CI or eng\common\CIBuild.cmd) ───────────────
REM OfficialBuild=true + BuildId=N → produces 1.0.0-ci.YYYYMMDD.N
REM Local builds produce 1.0.0-dev (no env vars needed)
if "%OFFICIAL_BUILD%"=="" (
    if defined GITHUB_ACTIONS set "OFFICIAL_BUILD=true"
)
if "%BUILD_ID%"=="" (
    if defined GITHUB_RUN_NUMBER set "BUILD_ID=%GITHUB_RUN_NUMBER%"
)
set "VERSION_PROPS="
if "%OFFICIAL_BUILD%"=="true" (
    set "VERSION_PROPS=-p:OfficialBuild=true"
    if not "%BUILD_ID%"=="" set "VERSION_PROPS=-p:OfficialBuild=true -p:BuildId=%BUILD_ID%"
)

if "%~1"=="" goto :usage
goto :%~1 2>nul || (echo Unknown target: %~1 & goto :usage)

:restore
echo [BUILD] Restoring NuGet packages...
dotnet restore "%SOLUTION%" --verbosity minimal
if errorlevel 1 exit /b 1
echo [BUILD] Restore complete.
goto :eof

:build
echo [BUILD] Building server-side projects (%CONFIG%)...
REM MAUI requires platform workloads not on all CI agents. Build server projects explicitly.
for %%P in (
    TheWatch.Shared TheWatch.Data
    TheWatch.Dashboard.Api TheWatch.Dashboard.Web
    TheWatch.Functions TheWatch.BuildServer TheWatch.DocGen
    TheWatch.WorkerServices TheWatch.Cli TheWatch.ApiService TheWatch.Web
    TheWatch.Adapters.Azure TheWatch.Adapters.Mock TheWatch.Adapters.AWS
    TheWatch.Adapters.Google TheWatch.Adapters.GitHub TheWatch.Adapters.Oracle
    TheWatch.Adapters.Cloudflare TheWatch.ServiceDefaults TheWatch.AppHost
    TheWatch.Shared.Tests TheWatch.Data.Tests TheWatch.Tests
    TheWatch.Dashboard.Api.Tests TheWatch.Functions.Tests
    TheWatch.Adapters.Mock.Tests TheWatch.Adapters.Azure.Tests
) do (
    if exist "%%P\%%P.csproj" (
        dotnet build "%%P\%%P.csproj" -c %CONFIG% --no-restore -p:TreatWarningsAsErrors=false %VERSION_PROPS%
    )
)
echo [BUILD] Build complete.
goto :eof

:test
echo [BUILD] Running unit tests...
if not exist "%TEST_RESULTS%" mkdir "%TEST_RESULTS%"
dotnet test "%SOLUTION%" -c %CONFIG% --no-build ^
    --filter "Category!=Integration" ^
    --logger "trx;LogFileName=unit-tests.trx" ^
    --results-directory "%TEST_RESULTS%" ^
    --collect:"XPlat Code Coverage" ^
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
if errorlevel 1 exit /b 1
echo [BUILD] Unit tests complete.
goto :eof

:test-integ
echo [BUILD] Running integration tests (requires Azure OpenAI creds)...
if not exist "%TEST_RESULTS%" mkdir "%TEST_RESULTS%"
dotnet test "%SOLUTION%" -c %CONFIG% --no-build ^
    --filter "Category=Integration" ^
    --logger "trx;LogFileName=integration-tests.trx" ^
    --results-directory "%TEST_RESULTS%"
if errorlevel 1 exit /b 1
echo [BUILD] Integration tests complete.
goto :eof

:publish
echo [BUILD] Publishing deployable artifacts...
if not exist "%ARTIFACTS%" mkdir "%ARTIFACTS%"

REM Dashboard.Api
dotnet publish TheWatch.Dashboard.Api/TheWatch.Dashboard.Api.csproj -c %CONFIG% --no-build -o "%ARTIFACTS%\Dashboard.Api"

REM Dashboard.Web
dotnet publish TheWatch.Dashboard.Web/TheWatch.Dashboard.Web.csproj -c %CONFIG% --no-build -o "%ARTIFACTS%\Dashboard.Web"

REM Functions
dotnet publish TheWatch.Functions/TheWatch.Functions.csproj -c %CONFIG% --no-build -o "%ARTIFACTS%\Functions"

REM WorkerServices
dotnet publish TheWatch.WorkerServices/TheWatch.WorkerServices.csproj -c %CONFIG% --no-build -o "%ARTIFACTS%\WorkerServices"

REM BuildServer
dotnet publish TheWatch.BuildServer/TheWatch.BuildServer.csproj -c %CONFIG% --no-build -o "%ARTIFACTS%\BuildServer"

REM DocGen
dotnet publish TheWatch.DocGen/TheWatch.DocGen.csproj -c %CONFIG% --no-build -o "%ARTIFACTS%\DocGen"

REM CLI
dotnet publish TheWatch.Cli/TheWatch.Cli.csproj -c %CONFIG% --no-build -o "%ARTIFACTS%\Cli"

echo [BUILD] Publish complete. Artifacts in: %ARTIFACTS%
goto :eof

:pack
echo [BUILD] Creating NuGet packages...
if not exist "%ARTIFACTS%\packages" mkdir "%ARTIFACTS%\packages"
dotnet pack TheWatch.Shared/TheWatch.Shared.csproj -c %CONFIG% --no-build -o "%ARTIFACTS%\packages"
dotnet pack TheWatch.Data/TheWatch.Data.csproj -c %CONFIG% --no-build -o "%ARTIFACTS%\packages"
echo [BUILD] Pack complete.
goto :eof

:audit-verify
echo [BUILD] Verifying audit trail integrity...
dotnet test TheWatch.Data.Tests/TheWatch.Data.Tests.csproj -c %CONFIG% --no-build ^
    --filter "FullyQualifiedName~AuditTrail" ^
    --logger "trx;LogFileName=audit-verify.trx" ^
    --results-directory "%TEST_RESULTS%"
if errorlevel 1 (
    echo [BUILD] AUDIT INTEGRITY CHECK FAILED
    exit /b 1
)
echo [BUILD] Audit integrity verified.
goto :eof

:all
call :restore
if errorlevel 1 exit /b 1
call :build
if errorlevel 1 exit /b 1
call :test
if errorlevel 1 exit /b 1
call :publish
goto :eof

:ci
call :restore
if errorlevel 1 exit /b 1
call :build
if errorlevel 1 exit /b 1
call :test
if errorlevel 1 exit /b 1
call :audit-verify
if errorlevel 1 exit /b 1
call :publish
goto :eof

:usage
echo.
echo Usage: build.cmd [target]
echo.
echo Targets:
echo   restore        Restore NuGet packages
echo   build          Build solution (Release)
echo   test           Run unit tests
echo   test-integ     Run integration tests (needs Azure creds)
echo   publish        Publish deployable artifacts
echo   pack           Create NuGet packages
echo   audit-verify   Verify audit Merkle chain integrity
echo   all            restore + build + test + publish
echo   ci             Full CI: restore + build + test + audit-verify + publish
exit /b 1
