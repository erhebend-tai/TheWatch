#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# TheWatch — CI Build Entry Point (Linux/macOS)
# ═══════════════════════════════════════════════════════════════════════════════
# Arcade convention: eng/common/cibuild.sh is the standard CI entry point.
# Delegates to the root build.sh with version parameters.
#
# Example:
#   ./eng/common/cibuild.sh                           (default ci target)
#   ./eng/common/cibuild.sh --build-id 42             (explicit build ID)
#   ./eng/common/cibuild.sh --release                 (stable release build)
# ═══════════════════════════════════════════════════════════════════════════════
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# Default to GitHub Actions run number if available
BUILD_ID="${BUILD_ID:-${GITHUB_RUN_NUMBER:-0}}"
EXTRA_ARGS=""

# Parse arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --build-id)
            BUILD_ID="$2"
            shift 2
            ;;
        --release)
            EXTRA_ARGS="$EXTRA_ARGS -p:DotNetFinalVersionKind=release"
            shift
            ;;
        *)
            shift
            ;;
    esac
done

echo "[CI] OfficialBuild=true, BuildId=$BUILD_ID"
export BUILD_ID
"$REPO_ROOT/build.sh" ci
