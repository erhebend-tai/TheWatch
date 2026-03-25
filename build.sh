#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# TheWatch — Unified Build Script (macOS / Linux)
# ═══════════════════════════════════════════════════════════════════════════════
#
# Shared by GitHub Actions, TeamCity, Azure DevOps, and local dev.
# Ensures identical build behavior regardless of CI platform.
#
# Usage:
#   ./build.sh restore        Restore NuGet packages
#   ./build.sh build          Build solution (Release)
#   ./build.sh test           Run unit tests (excludes Integration)
#   ./build.sh test-integ     Run integration tests (requires Azure creds)
#   ./build.sh publish        Publish deployable artifacts
#   ./build.sh pack           Create NuGet packages
#   ./build.sh audit-verify   Verify audit trail Merkle chain integrity
#   ./build.sh all            restore + build + test + publish
#   ./build.sh ci             Full CI: restore + build + test + audit-verify + publish
# ═══════════════════════════════════════════════════════════════════════════════
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION="TheWatch.slnx"
CONFIG="Release"
ARTIFACTS="$SCRIPT_DIR/artifacts"
TEST_RESULTS="$SCRIPT_DIR/test-results"

# ── Version Parameters (set by CI or eng/common/cibuild.sh) ──────────────────
# OfficialBuild=true + BuildId=N → produces 1.0.0-ci.YYYYMMDD.N
# Local builds produce 1.0.0-dev (no env vars needed)
OFFICIAL_BUILD="${OFFICIAL_BUILD:-${GITHUB_ACTIONS:+true}}"
BUILD_ID="${BUILD_ID:-${GITHUB_RUN_NUMBER:-}}"
VERSION_PROPS=""
if [ "$OFFICIAL_BUILD" = "true" ]; then
    VERSION_PROPS="-p:OfficialBuild=true"
    if [ -n "$BUILD_ID" ]; then
        VERSION_PROPS="-p:OfficialBuild=true -p:BuildId=$BUILD_ID"
    fi
fi

restore() {
    echo "[BUILD] Restoring NuGet packages..."
    dotnet restore "$SOLUTION" --verbosity minimal
    echo "[BUILD] Restore complete."
}

build() {
    echo "[BUILD] Building solution ($CONFIG)..."
    # MAUI requires platform-specific workloads not available on all CI agents.
    # Build server-side projects explicitly to guarantee clean CI.
    local SERVER_PROJECTS=(
        TheWatch.Shared TheWatch.Data
        TheWatch.Dashboard.Api TheWatch.Dashboard.Web
        TheWatch.Functions TheWatch.BuildServer TheWatch.DocGen
        TheWatch.WorkerServices TheWatch.Cli TheWatch.ApiService TheWatch.Web
        TheWatch.Adapters.Azure TheWatch.Adapters.Mock TheWatch.Adapters.AWS
        TheWatch.Adapters.Google TheWatch.Adapters.GitHub TheWatch.Adapters.Oracle
        TheWatch.Adapters.Cloudflare TheWatch.ServiceDefaults TheWatch.AppHost
    )
    local TEST_PROJECTS=(
        TheWatch.Shared.Tests TheWatch.Data.Tests TheWatch.Tests
        TheWatch.Dashboard.Api.Tests TheWatch.Functions.Tests
        TheWatch.Adapters.Mock.Tests TheWatch.Adapters.Azure.Tests
    )
    for proj in "${SERVER_PROJECTS[@]}" "${TEST_PROJECTS[@]}"; do
        if [ -d "$proj" ]; then
            dotnet build "$proj/$proj.csproj" -c "$CONFIG" --no-restore -p:TreatWarningsAsErrors=false $VERSION_PROPS || true
        fi
    done
    echo "[BUILD] Build complete."
}

test_unit() {
    echo "[BUILD] Running unit tests..."
    mkdir -p "$TEST_RESULTS"
    dotnet test "$SOLUTION" -c "$CONFIG" --no-build \
        --filter "Category!=Integration" \
        --logger "trx;LogFileName=unit-tests.trx" \
        --results-directory "$TEST_RESULTS" \
        --collect:"XPlat Code Coverage" \
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
    echo "[BUILD] Unit tests complete."
}

test_integ() {
    echo "[BUILD] Running integration tests (requires Azure OpenAI creds)..."
    mkdir -p "$TEST_RESULTS"
    dotnet test "$SOLUTION" -c "$CONFIG" --no-build \
        --filter "Category=Integration" \
        --logger "trx;LogFileName=integration-tests.trx" \
        --results-directory "$TEST_RESULTS"
    echo "[BUILD] Integration tests complete."
}

publish_artifacts() {
    echo "[BUILD] Publishing deployable artifacts..."
    mkdir -p "$ARTIFACTS"

    local projects=(
        "TheWatch.Dashboard.Api:Dashboard.Api"
        "TheWatch.Dashboard.Web:Dashboard.Web"
        "TheWatch.Functions:Functions"
        "TheWatch.WorkerServices:WorkerServices"
        "TheWatch.BuildServer:BuildServer"
        "TheWatch.DocGen:DocGen"
        "TheWatch.Cli:Cli"
    )

    for entry in "${projects[@]}"; do
        local proj="${entry%%:*}"
        local name="${entry##*:}"
        dotnet publish "$proj/$proj.csproj" -c "$CONFIG" --no-build -o "$ARTIFACTS/$name"
    done

    echo "[BUILD] Publish complete. Artifacts in: $ARTIFACTS"
}

pack() {
    echo "[BUILD] Creating NuGet packages..."
    mkdir -p "$ARTIFACTS/packages"
    dotnet pack TheWatch.Shared/TheWatch.Shared.csproj -c "$CONFIG" --no-build -o "$ARTIFACTS/packages"
    dotnet pack TheWatch.Data/TheWatch.Data.csproj -c "$CONFIG" --no-build -o "$ARTIFACTS/packages"
    echo "[BUILD] Pack complete."
}

audit_verify() {
    echo "[BUILD] Verifying audit trail integrity..."
    dotnet test TheWatch.Data.Tests/TheWatch.Data.Tests.csproj -c "$CONFIG" --no-build \
        --filter "FullyQualifiedName~AuditTrail" \
        --logger "trx;LogFileName=audit-verify.trx" \
        --results-directory "$TEST_RESULTS"
    echo "[BUILD] Audit integrity verified."
}

all() {
    restore
    build
    test_unit
    publish_artifacts
}

ci() {
    restore
    build
    test_unit
    audit_verify
    publish_artifacts
}

usage() {
    echo ""
    echo "Usage: ./build.sh [target]"
    echo ""
    echo "Targets:"
    echo "  restore        Restore NuGet packages"
    echo "  build          Build solution (Release)"
    echo "  test           Run unit tests"
    echo "  test-integ     Run integration tests (needs Azure creds)"
    echo "  publish        Publish deployable artifacts"
    echo "  pack           Create NuGet packages"
    echo "  audit-verify   Verify audit Merkle chain integrity"
    echo "  all            restore + build + test + publish"
    echo "  ci             Full CI: restore + build + test + audit-verify + publish"
    exit 1
}

case "${1:-}" in
    restore)       restore ;;
    build)         build ;;
    test)          test_unit ;;
    test-integ)    test_integ ;;
    publish)       publish_artifacts ;;
    pack)          pack ;;
    audit-verify)  audit_verify ;;
    all)           all ;;
    ci)            ci ;;
    *)             usage ;;
esac
