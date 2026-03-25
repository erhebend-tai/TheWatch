@echo off
setlocal EnableDelayedExpansion
REM ═══════════════════════════════════════════════════════════════════════════════
REM TheWatch Swarm Infrastructure — Deploy ^& Wire Credentials (Windows)
REM ═══════════════════════════════════════════════════════════════════════════════
REM
REM Deploys the Bicep template, retrieves all keys/connection strings,
REM writes .env, and populates dotnet user-secrets for Dashboard.Api and Web.
REM
REM Usage:
REM   infra\deploy.cmd                                      uses defaults
REM   infra\deploy.cmd -g MyResourceGroup                   custom resource group
REM   infra\deploy.cmd -g MyRG -n myprefix                  custom RG + name prefix
REM   infra\deploy.cmd --dry-run                            validate only
REM   infra\deploy.cmd --environment staging                deploy to staging
REM   infra\deploy.cmd --environment production             deploy to production
REM   infra\deploy.cmd --containers                         build & push containers to ACR
REM
REM Prerequisites:
REM   - az cli logged in (az login)
REM   - dotnet SDK installed
REM   - jq on PATH (winget install jqlang.jq)
REM ═══════════════════════════════════════════════════════════════════════════════

set "SCRIPT_DIR=%~dp0"
set "PROJECT_ROOT=%SCRIPT_DIR%.."

REM ── Defaults ────────────────────────────────────────────────────────────────
set "RESOURCE_GROUP=Watch-Init"
set "BASE_NAME=thewatch"
set "SQL_ADMIN_USER=watchadmin"
set "SQL_ADMIN_PASS="
set "RABBIT_USER=thewatch"
set "RABBIT_PASS="
set "DRY_RUN=0"
set "DEPLOY_CONTAINERS=0"
set "ENVIRONMENT=dev"
set "PARAMS_FILE=%SCRIPT_DIR%main.parameters.dev.json"
set "ACR_LOGIN_SERVER="

REM ── Parse Arguments ─────────────────────────────────────────────────────────
:parse_args
if "%~1"=="" goto :done_args
if /i "%~1"=="-g"               ( set "RESOURCE_GROUP=%~2" & shift & shift & goto :parse_args )
if /i "%~1"=="--resource-group" ( set "RESOURCE_GROUP=%~2" & shift & shift & goto :parse_args )
if /i "%~1"=="-n"               ( set "BASE_NAME=%~2"      & shift & shift & goto :parse_args )
if /i "%~1"=="--name"           ( set "BASE_NAME=%~2"      & shift & shift & goto :parse_args )
if /i "%~1"=="--sql-password"   ( set "SQL_ADMIN_PASS=%~2" & shift & shift & goto :parse_args )
if /i "%~1"=="--rabbit-password"( set "RABBIT_PASS=%~2"    & shift & shift & goto :parse_args )
if /i "%~1"=="--params"         ( set "PARAMS_FILE=%~2"    & shift & shift & goto :parse_args )
if /i "%~1"=="--environment"    ( set "ENVIRONMENT=%~2"    & shift & shift & goto :parse_args )
if /i "%~1"=="-e"               ( set "ENVIRONMENT=%~2"    & shift & shift & goto :parse_args )
if /i "%~1"=="--containers"    ( set "DEPLOY_CONTAINERS=1" & shift          & goto :parse_args )
if /i "%~1"=="--acr"           ( set "ACR_LOGIN_SERVER=%~2"& shift & shift & goto :parse_args )
if /i "%~1"=="--dry-run"        ( set "DRY_RUN=1"          & shift          & goto :parse_args )
if /i "%~1"=="-h"               ( goto :show_help )
if /i "%~1"=="--help"           ( goto :show_help )
echo Unknown argument: %~1
exit /b 1

:show_help
echo Usage: %~nx0 [-g resource-group] [-n base-name] [--environment staging^|production] [--containers] [--acr server] [--sql-password pw] [--rabbit-password pw] [--dry-run]
exit /b 0

:done_args

REM ── Resolve environment-specific parameters file ──────────────────────────
if not "!ENVIRONMENT!"=="dev" (
    if exist "%SCRIPT_DIR%main.parameters.!ENVIRONMENT!.json" (
        set "PARAMS_FILE=%SCRIPT_DIR%main.parameters.!ENVIRONMENT!.json"
        echo Using environment-specific parameters: !PARAMS_FILE!
    )
)

REM ── Resolve ACR login server ──────────────────────────────────────────────
if "!ACR_LOGIN_SERVER!"=="" set "ACR_LOGIN_SERVER=!BASE_NAME!.azurecr.io"

REM ── Prompt for secrets if not provided ──────────────────────────────────────
if "!SQL_ADMIN_PASS!"=="" (
    set /p "SQL_ADMIN_PASS=SQL admin password: "
)
if "!RABBIT_PASS!"=="" (
    set /p "RABBIT_PASS=RabbitMQ password: "
)

echo.
echo ══════════════════════════════════════════════════════════════
echo   TheWatch Swarm Infrastructure Deployment (Windows)
echo ══════════════════════════════════════════════════════════════
echo   Resource Group : !RESOURCE_GROUP!
echo   Base Name      : !BASE_NAME!
echo   Template       : %SCRIPT_DIR%main.bicep
echo   Parameters     : !PARAMS_FILE!
echo   Dry Run        : !DRY_RUN!
echo ══════════════════════════════════════════════════════════════
echo.

REM ── Ensure resource group exists ────────────────────────────────────────────
az group show --name "!RESOURCE_GROUP!" >nul 2>&1
if errorlevel 1 (
    echo Creating resource group !RESOURCE_GROUP! in eastus2...
    az group create --name "!RESOURCE_GROUP!" --location eastus2 --output none
)

REM ── Deploy or Validate ──────────────────────────────────────────────────────
set "DEPLOY_NAME=!BASE_NAME!-swarm-%date:~10,4%%date:~4,2%%date:~7,2%"

if "!DRY_RUN!"=="1" (
    echo Validating template ^(dry run^)...
    az deployment group validate ^
        --resource-group "!RESOURCE_GROUP!" ^
        --template-file "%SCRIPT_DIR%main.bicep" ^
        --parameters "!PARAMS_FILE!" ^
        --parameters "sqlAdminPassword=!SQL_ADMIN_PASS!" ^
        --parameters "rabbitPassword=!RABBIT_PASS!" ^
        --parameters "baseName=!BASE_NAME!" ^
        --output json
    if errorlevel 1 ( echo Validation FAILED. & exit /b 1 )
    echo Validation passed.
    exit /b 0
)

echo Deploying infrastructure ^(this may take 15-25 minutes^)...

set "DEPLOY_OUTPUT_FILE=%TEMP%\thewatch-deploy-output.json"

az deployment group create ^
    --name "!DEPLOY_NAME!" ^
    --resource-group "!RESOURCE_GROUP!" ^
    --template-file "%SCRIPT_DIR%main.bicep" ^
    --parameters "!PARAMS_FILE!" ^
    --parameters "sqlAdminPassword=!SQL_ADMIN_PASS!" ^
    --parameters "rabbitPassword=!RABBIT_PASS!" ^
    --parameters "baseName=!BASE_NAME!" ^
    --output json > "!DEPLOY_OUTPUT_FILE!"

if errorlevel 1 ( echo Deployment FAILED. & exit /b 1 )

echo Deployment complete. Extracting outputs...

REM ── Extract Bicep Outputs ───────────────────────────────────────────────────
for /f "usebackq delims=" %%A in (`jq -r ".properties.outputs.signalrConnectionString.value // empty" "!DEPLOY_OUTPUT_FILE!"`) do set "SIGNALR_CONN=%%A"
for /f "usebackq delims=" %%A in (`jq -r ".properties.outputs.signalrHostName.value // empty" "!DEPLOY_OUTPUT_FILE!"`) do set "SIGNALR_HOST=%%A"
for /f "usebackq delims=" %%A in (`jq -r ".properties.outputs.redisHostName.value // empty" "!DEPLOY_OUTPUT_FILE!"`) do set "REDIS_HOST=%%A"
for /f "usebackq delims=" %%A in (`jq -r ".properties.outputs.redisSslPort.value // empty" "!DEPLOY_OUTPUT_FILE!"`) do set "REDIS_PORT=%%A"
for /f "usebackq delims=" %%A in (`jq -r ".properties.outputs.rabbitMqFqdn.value // empty" "!DEPLOY_OUTPUT_FILE!"`) do set "RABBIT_FQDN=%%A"
for /f "usebackq delims=" %%A in (`jq -r ".properties.outputs.sqlServerFqdn.value // empty" "!DEPLOY_OUTPUT_FILE!"`) do set "SQL_FQDN=%%A"
for /f "usebackq delims=" %%A in (`jq -r ".properties.outputs.sqlConnectionStringTemplate.value // empty" "!DEPLOY_OUTPUT_FILE!"`) do set "SQL_CONN_TEMPLATE=%%A"
for /f "usebackq delims=" %%A in (`jq -r ".properties.outputs.openaiEndpoint.value // empty" "!DEPLOY_OUTPUT_FILE!"`) do set "OPENAI_ENDPOINT=%%A"
for /f "usebackq delims=" %%A in (`jq -r ".properties.outputs.openaiAccountName.value // empty" "!DEPLOY_OUTPUT_FILE!"`) do set "OPENAI_ACCOUNT=%%A"

REM ── Retrieve secrets that require list-keys ─────────────────────────────────
echo Retrieving Redis access key...
for /f "usebackq delims=" %%A in (`az redis list-keys --name "!BASE_NAME!-redis" --resource-group "!RESOURCE_GROUP!" --query "primaryKey" -o tsv 2^>nul`) do set "REDIS_KEY=%%A"
if "!REDIS_KEY!"=="" set "REDIS_KEY=PENDING"

set "REDIS_CONN=!REDIS_HOST!:!REDIS_PORT!,password=!REDIS_KEY!,ssl=True,abortConnect=False"

echo Retrieving OpenAI API key...
for /f "usebackq delims=" %%A in (`az cognitiveservices account keys list --name "!OPENAI_ACCOUNT!" --resource-group "!RESOURCE_GROUP!" --query "key1" -o tsv 2^>nul`) do set "OPENAI_KEY=%%A"
if "!OPENAI_KEY!"=="" (
    for /f "usebackq delims=" %%A in (`az cognitiveservices account keys list --name "!OPENAI_ACCOUNT!" --resource-group "Project" --query "key1" -o tsv 2^>nul`) do set "OPENAI_KEY=%%A"
)
if "!OPENAI_KEY!"=="" set "OPENAI_KEY=RETRIEVE_MANUALLY"

set "SQL_CONN=!SQL_CONN_TEMPLATE!Password=!SQL_ADMIN_PASS!;"

REM ── Retrieve subscription info ──────────────────────────────────────────────
for /f "usebackq delims=" %%A in (`az account show --query "id" -o tsv`) do set "SUB_ID=%%A"
for /f "usebackq delims=" %%A in (`az account show --query "tenantId" -o tsv`) do set "TENANT_ID=%%A"

REM ── Write .env ──────────────────────────────────────────────────────────────
set "ENV_FILE=%PROJECT_ROOT%\.env"
echo Writing !ENV_FILE!...

(
echo # ═══════════════════════════════════════════════════════════════════
echo # TheWatch Swarm Infrastructure — Azure Resource Credentials
echo # Generated: %date% %time% by infra\deploy.cmd
echo # ═══════════════════════════════════════════════════════════════════
echo.
echo # ── Azure Subscription ────────────────────────────────────────────
echo AZURE_SUBSCRIPTION_ID=!SUB_ID!
echo AZURE_TENANT_ID=!TENANT_ID!
echo AZURE_RESOURCE_GROUP=!RESOURCE_GROUP!
echo.
echo # ── Azure OpenAI ──────────────────────────────────────────────────
echo AZURE_OPENAI_ENDPOINT=!OPENAI_ENDPOINT!
echo AZURE_OPENAI_API_KEY=!OPENAI_KEY!
echo AZURE_OPENAI_DEPLOYMENT_GPT41=gpt-4.1
echo AZURE_OPENAI_DEPLOYMENT_GPT4O=gpt-4o
echo AZURE_OPENAI_DEPLOYMENT_GPT4O_MINI=gpt-4o-mini
echo AZURE_OPENAI_DEPLOYMENT_EMBEDDING=text-embedding-3-large
echo AZURE_OPENAI_API_VERSION=2024-12-01-preview
echo.
echo # ── Azure SignalR Service ─────────────────────────────────────────
echo AZURE_SIGNALR_CONNECTION_STRING=!SIGNALR_CONN!
echo AZURE_SIGNALR_HOSTNAME=!SIGNALR_HOST!
echo.
echo # ── RabbitMQ on Container Apps ────────────────────────────────────
echo RABBITMQ_HOST=!RABBIT_FQDN!
echo RABBITMQ_PORT=5672
echo RABBITMQ_USER=!RABBIT_USER!
echo RABBITMQ_PASSWORD=!RABBIT_PASS!
echo RABBITMQ_VHOST=/
echo RABBITMQ_EXCHANGE=swarm-tasks
echo RABBITMQ_RESULTS_QUEUE=swarm-results
echo.
echo # ── Azure Cache for Redis ─────────────────────────────────────────
echo REDIS_HOST=!REDIS_HOST!
echo REDIS_PORT=!REDIS_PORT!
echo REDIS_PASSWORD=!REDIS_KEY!
echo REDIS_SSL=true
echo REDIS_CONNECTION_STRING=!REDIS_CONN!
echo.
echo # ── Azure SQL Server ──────────────────────────────────────────────
echo SQL_SERVER=!SQL_FQDN!
echo SQL_DATABASE=hangfire
echo SQL_USER=!SQL_ADMIN_USER!
echo SQL_PASSWORD=!SQL_ADMIN_PASS!
echo SQL_CONNECTION_STRING=!SQL_CONN!
echo.
echo # ── Aspire / Local Dev Overrides ──────────────────────────────────
echo ASPIRE_DASHBOARD_URL=https://localhost:17037
echo DOTNET_ENVIRONMENT=Development
) > "!ENV_FILE!"

echo .env written.

REM ── Dotnet User-Secrets ─────────────────────────────────────────────────────
echo Wiring dotnet user-secrets...

REM -- Dashboard.Api --
echo   Wiring secrets for Dashboard.Api...
pushd "%PROJECT_ROOT%\TheWatch.Dashboard.Api"
dotnet user-secrets init 2>nul
dotnet user-secrets set "Azure:OpenAI:Endpoint" "!OPENAI_ENDPOINT!" 2>nul
dotnet user-secrets set "Azure:OpenAI:ApiKey" "!OPENAI_KEY!" 2>nul
dotnet user-secrets set "Azure:SignalR:ConnectionString" "!SIGNALR_CONN!" 2>nul
dotnet user-secrets set "ConnectionStrings:Redis" "!REDIS_CONN!" 2>nul
dotnet user-secrets set "ConnectionStrings:Hangfire" "!SQL_CONN!" 2>nul
dotnet user-secrets set "RabbitMQ:Host" "!RABBIT_FQDN!" 2>nul
dotnet user-secrets set "RabbitMQ:User" "!RABBIT_USER!" 2>nul
dotnet user-secrets set "RabbitMQ:Password" "!RABBIT_PASS!" 2>nul
popd

REM -- Dashboard.Web --
echo   Wiring secrets for Dashboard.Web...
pushd "%PROJECT_ROOT%\TheWatch.Dashboard.Web"
dotnet user-secrets init 2>nul
dotnet user-secrets set "Azure:OpenAI:Endpoint" "!OPENAI_ENDPOINT!" 2>nul
dotnet user-secrets set "Azure:OpenAI:ApiKey" "!OPENAI_KEY!" 2>nul
dotnet user-secrets set "Azure:SignalR:ConnectionString" "!SIGNALR_CONN!" 2>nul
dotnet user-secrets set "ConnectionStrings:Redis" "!REDIS_CONN!" 2>nul
popd

REM ── Container Build ^& Push (optional) ──────────────────────────────────────
if "!DEPLOY_CONTAINERS!"=="1" (
    echo.
    echo Building and pushing container images to ACR: !ACR_LOGIN_SERVER!

    az acr login --name "!BASE_NAME!" 2>nul
    if errorlevel 1 (
        echo Warning: ACR login via az acr failed. Trying docker login...
        for /f "usebackq delims=" %%A in (`az acr credential show --name "!BASE_NAME!" --query "passwords[0].value" -o tsv 2^>nul`) do set "ACR_PWD=%%A"
        if not "!ACR_PWD!"=="" (
            echo !ACR_PWD! | docker login "!ACR_LOGIN_SERVER!" --username "!BASE_NAME!" --password-stdin
        ) else (
            echo ERROR: Cannot authenticate to ACR. Skipping container deployment.
            set "DEPLOY_CONTAINERS=0"
        )
    )
)

if "!DEPLOY_CONTAINERS!"=="1" (
    for /f "usebackq delims=" %%A in (`git rev-parse --short HEAD 2^>nul`) do set "GIT_SHORT=%%A"
    if "!GIT_SHORT!"=="" set "GIT_SHORT=local"
    set "IMAGE_TAG=%date:~10,4%%date:~4,2%%date:~7,2%-!GIT_SHORT!"

    echo Image tag: !IMAGE_TAG!

    REM -- Build Dashboard.Api --
    echo Building Dashboard.Api...
    docker build -f "%PROJECT_ROOT%\TheWatch.Dashboard.Api\Dockerfile" ^
        -t "!ACR_LOGIN_SERVER!/thewatch/dashboard-api:!IMAGE_TAG!" ^
        -t "!ACR_LOGIN_SERVER!/thewatch/dashboard-api:!ENVIRONMENT!" ^
        -t "!ACR_LOGIN_SERVER!/thewatch/dashboard-api:latest" ^
        "%PROJECT_ROOT%"
    docker push "!ACR_LOGIN_SERVER!/thewatch/dashboard-api" --all-tags

    REM -- Build Functions --
    echo Building Functions...
    docker build -f "%PROJECT_ROOT%\TheWatch.Functions\Dockerfile" ^
        -t "!ACR_LOGIN_SERVER!/thewatch/functions:!IMAGE_TAG!" ^
        -t "!ACR_LOGIN_SERVER!/thewatch/functions:!ENVIRONMENT!" ^
        -t "!ACR_LOGIN_SERVER!/thewatch/functions:latest" ^
        "%PROJECT_ROOT%"
    docker push "!ACR_LOGIN_SERVER!/thewatch/functions" --all-tags

    REM -- Build WorkerServices --
    echo Building WorkerServices...
    docker build -f "%PROJECT_ROOT%\TheWatch.WorkerServices\Dockerfile" ^
        -t "!ACR_LOGIN_SERVER!/thewatch/worker:!IMAGE_TAG!" ^
        -t "!ACR_LOGIN_SERVER!/thewatch/worker:!ENVIRONMENT!" ^
        -t "!ACR_LOGIN_SERVER!/thewatch/worker:latest" ^
        "%PROJECT_ROOT%"
    docker push "!ACR_LOGIN_SERVER!/thewatch/worker" --all-tags

    echo All container images pushed to !ACR_LOGIN_SERVER!
)

REM ── Cleanup temp file ───────────────────────────────────────────────────────
del "!DEPLOY_OUTPUT_FILE!" 2>nul

REM ── Summary ─────────────────────────────────────────────────────────────────
echo.
echo ══════════════════════════════════════════════════════════════
echo   Deployment Complete
echo ══════════════════════════════════════════════════════════════
echo   SignalR     : !SIGNALR_HOST!
echo   Redis       : !REDIS_HOST!:!REDIS_PORT!
echo   RabbitMQ    : !RABBIT_FQDN!
echo   SQL Server  : !SQL_FQDN!
echo   OpenAI      : !OPENAI_ENDPOINT!
echo   .env        : !ENV_FILE!
echo   Secrets     : Dashboard.Api, Dashboard.Web
echo   Environment : !ENVIRONMENT!
if "!DEPLOY_CONTAINERS!"=="1" (
echo   ACR         : !ACR_LOGIN_SERVER!
echo   Containers  : dashboard-api, functions, worker
)
echo ══════════════════════════════════════════════════════════════

endlocal
exit /b 0
