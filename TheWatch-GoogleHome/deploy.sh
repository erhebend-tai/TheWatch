#!/usr/bin/env bash

# ============================================================================
# WRITE-AHEAD LOG (WAL)
# ============================================================================
# File:        deploy.sh
# Module:      TheWatch Google Home Integration - Deployment Script
# Created:     2026-03-24
# Author:      TheWatch Platform Team
# ----------------------------------------------------------------------------
# PURPOSE:
#   Automated deployment script for the TheWatch Google Home Action.
#   Handles:
#   1. Webhook deployment to Google Cloud Functions (or Cloud Run)
#   2. Actions SDK push to Google Actions Console
#   3. Actions SDK deploy to preview/production
#   4. Environment validation
#   5. Post-deploy verification
#
# PREREQUISITES:
#   - gcloud CLI installed and authenticated
#   - gactions CLI installed (https://developers.google.com/assistant/conversational/build/gactions)
#   - Node.js >= 18
#   - GCP project configured with Actions API enabled
#
# USAGE:
#   ./deploy.sh                    # Deploy to preview (default)
#   ./deploy.sh --production       # Deploy to production
#   ./deploy.sh --webhook-only     # Only deploy the webhook
#   ./deploy.sh --actions-only     # Only push/deploy the Actions config
#   ./deploy.sh --dry-run          # Validate without deploying
#
# ENVIRONMENT VARIABLES (required):
#   THEWATCH_GCP_PROJECT       - GCP project ID
#   THEWATCH_GCP_REGION        - GCP region (default: us-central1)
#   THEWATCH_WEBHOOK_URL       - Webhook URL (auto-set after Cloud Function deploy)
#   THEWATCH_OAUTH_CLIENT_ID   - OAuth2 client ID
#   THEWATCH_OAUTH_CLIENT_SECRET - OAuth2 client secret
#
# CHANGES:
#   2026-03-24  Initial creation
# ============================================================================

set -euo pipefail
IFS=$'\n\t'

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WEBHOOK_DIR="${SCRIPT_DIR}/webhook"
SDK_DIR="${SCRIPT_DIR}/sdk"

GCP_PROJECT="${THEWATCH_GCP_PROJECT:-}"
GCP_REGION="${THEWATCH_GCP_REGION:-us-central1}"
FUNCTION_NAME="thewatch-google-home-fulfillment"
RUNTIME="nodejs20"
MEMORY="512MB"
TIMEOUT="30s"
MIN_INSTANCES=1
MAX_INSTANCES=10

DEPLOY_TARGET="preview"
DEPLOY_WEBHOOK=true
DEPLOY_ACTIONS=true
DRY_RUN=false

# ---------------------------------------------------------------------------
# Color output
# ---------------------------------------------------------------------------
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log_info()  { echo -e "${BLUE}[INFO]${NC}  $*"; }
log_ok()    { echo -e "${GREEN}[OK]${NC}    $*"; }
log_warn()  { echo -e "${YELLOW}[WARN]${NC}  $*"; }
log_error() { echo -e "${RED}[ERROR]${NC} $*"; }

# ---------------------------------------------------------------------------
# Parse arguments
# ---------------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    --production)    DEPLOY_TARGET="production"; shift ;;
    --webhook-only)  DEPLOY_ACTIONS=false; shift ;;
    --actions-only)  DEPLOY_WEBHOOK=false; shift ;;
    --dry-run)       DRY_RUN=true; shift ;;
    --help)
      echo "Usage: $0 [--production] [--webhook-only] [--actions-only] [--dry-run]"
      exit 0
      ;;
    *)
      log_error "Unknown argument: $1"
      exit 1
      ;;
  esac
done

# ---------------------------------------------------------------------------
# Validation
# ---------------------------------------------------------------------------
log_info "Validating environment..."

check_command() {
  if ! command -v "$1" &>/dev/null; then
    log_error "Required command not found: $1"
    exit 1
  fi
  log_ok "$1 found"
}

check_command "gcloud"
check_command "node"
check_command "npm"

# gactions CLI is optional if --webhook-only
if [[ "$DEPLOY_ACTIONS" == true ]]; then
  check_command "gactions"
fi

# Validate Node.js version
NODE_VERSION=$(node --version | cut -d'v' -f2 | cut -d'.' -f1)
if [[ "$NODE_VERSION" -lt 18 ]]; then
  log_error "Node.js >= 18 required, found v${NODE_VERSION}"
  exit 1
fi
log_ok "Node.js v${NODE_VERSION}"

# Validate GCP project
if [[ -z "$GCP_PROJECT" ]]; then
  GCP_PROJECT=$(gcloud config get-value project 2>/dev/null || true)
  if [[ -z "$GCP_PROJECT" ]]; then
    log_error "THEWATCH_GCP_PROJECT not set and no default gcloud project configured"
    exit 1
  fi
  log_warn "Using default gcloud project: ${GCP_PROJECT}"
fi
log_ok "GCP Project: ${GCP_PROJECT}"

# Validate OAuth credentials
if [[ -z "${THEWATCH_OAUTH_CLIENT_ID:-}" ]]; then
  log_warn "THEWATCH_OAUTH_CLIENT_ID not set - account linking will not work"
fi

if [[ "$DRY_RUN" == true ]]; then
  log_info "=== DRY RUN MODE - No actual deployments will be made ==="
fi

# ---------------------------------------------------------------------------
# Step 1: Install dependencies
# ---------------------------------------------------------------------------
log_info "Installing webhook dependencies..."

cd "$WEBHOOK_DIR"

if [[ "$DRY_RUN" == false ]]; then
  npm ci --production 2>&1 | tail -1
  log_ok "Dependencies installed"
else
  log_info "[DRY RUN] Would run: npm ci --production"
fi

# ---------------------------------------------------------------------------
# Step 2: Run tests
# ---------------------------------------------------------------------------
log_info "Running tests..."

if [[ "$DRY_RUN" == false ]]; then
  cd "$SCRIPT_DIR"
  npm --prefix webhook install 2>&1 | tail -1
  npx --prefix webhook mocha tests/**/*.js --timeout 10000 || {
    log_error "Tests failed! Aborting deployment."
    exit 1
  }
  log_ok "All tests passed"
else
  log_info "[DRY RUN] Would run: npx mocha tests/**/*.js"
fi

# ---------------------------------------------------------------------------
# Step 3: Deploy webhook to Cloud Functions
# ---------------------------------------------------------------------------
if [[ "$DEPLOY_WEBHOOK" == true ]]; then
  log_info "Deploying webhook to Cloud Functions..."

  DEPLOY_CMD="gcloud functions deploy ${FUNCTION_NAME} \
    --project=${GCP_PROJECT} \
    --region=${GCP_REGION} \
    --runtime=${RUNTIME} \
    --trigger-http \
    --allow-unauthenticated \
    --entry-point=app \
    --source=${WEBHOOK_DIR} \
    --memory=${MEMORY} \
    --timeout=${TIMEOUT} \
    --min-instances=${MIN_INSTANCES} \
    --max-instances=${MAX_INSTANCES} \
    --set-env-vars=NODE_ENV=production"

  # Add environment variables if set
  ENV_VARS="NODE_ENV=production"
  [[ -n "${THEWATCH_API_BASE_URL:-}" ]] && ENV_VARS="${ENV_VARS},THEWATCH_API_BASE_URL=${THEWATCH_API_BASE_URL}"
  [[ -n "${THEWATCH_OAUTH_CLIENT_ID:-}" ]] && ENV_VARS="${ENV_VARS},THEWATCH_OAUTH_CLIENT_ID=${THEWATCH_OAUTH_CLIENT_ID}"
  [[ -n "${THEWATCH_JWT_SECRET:-}" ]] && ENV_VARS="${ENV_VARS},THEWATCH_JWT_SECRET=${THEWATCH_JWT_SECRET}"
  [[ -n "${LOG_LEVEL:-}" ]] && ENV_VARS="${ENV_VARS},LOG_LEVEL=${LOG_LEVEL}"

  DEPLOY_CMD="gcloud functions deploy ${FUNCTION_NAME} \
    --project=${GCP_PROJECT} \
    --region=${GCP_REGION} \
    --runtime=${RUNTIME} \
    --trigger-http \
    --allow-unauthenticated \
    --entry-point=app \
    --source=${WEBHOOK_DIR} \
    --memory=${MEMORY} \
    --timeout=${TIMEOUT} \
    --min-instances=${MIN_INSTANCES} \
    --max-instances=${MAX_INSTANCES} \
    --set-env-vars=${ENV_VARS}"

  if [[ "$DRY_RUN" == false ]]; then
    eval "$DEPLOY_CMD"

    # Extract the webhook URL
    WEBHOOK_URL=$(gcloud functions describe "${FUNCTION_NAME}" \
      --project="${GCP_PROJECT}" \
      --region="${GCP_REGION}" \
      --format='value(httpsTrigger.url)' 2>/dev/null)

    log_ok "Webhook deployed: ${WEBHOOK_URL}"
    export THEWATCH_WEBHOOK_URL="${WEBHOOK_URL}"
  else
    log_info "[DRY RUN] Would run:"
    echo "  $DEPLOY_CMD"
  fi
fi

# ---------------------------------------------------------------------------
# Step 4: Push Actions SDK configuration
# ---------------------------------------------------------------------------
if [[ "$DEPLOY_ACTIONS" == true ]]; then
  log_info "Pushing Actions SDK configuration..."

  cd "$SDK_DIR"

  if [[ "$DRY_RUN" == false ]]; then
    # Push the action package
    gactions push --project "${GCP_PROJECT}"
    log_ok "Actions SDK pushed"

    # Deploy to target (preview or production)
    if [[ "$DEPLOY_TARGET" == "production" ]]; then
      log_warn "Deploying to PRODUCTION..."
      gactions deploy --project "${GCP_PROJECT}"
      log_ok "Actions deployed to production"
    else
      gactions deploy preview --project "${GCP_PROJECT}"
      log_ok "Actions deployed to preview"
    fi
  else
    log_info "[DRY RUN] Would run:"
    echo "  gactions push --project ${GCP_PROJECT}"
    echo "  gactions deploy ${DEPLOY_TARGET} --project ${GCP_PROJECT}"
  fi
fi

# ---------------------------------------------------------------------------
# Step 5: Post-deploy verification
# ---------------------------------------------------------------------------
log_info "Running post-deploy verification..."

if [[ "$DRY_RUN" == false && "$DEPLOY_WEBHOOK" == true ]]; then
  # Health check
  HEALTH_RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" "${WEBHOOK_URL}/health" 2>/dev/null || echo "000")

  if [[ "$HEALTH_RESPONSE" == "200" ]]; then
    log_ok "Health check passed (HTTP 200)"
  else
    log_warn "Health check returned HTTP ${HEALTH_RESPONSE} - may need time to warm up"
  fi
fi

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo ""
echo "============================================"
echo "  TheWatch Google Home Deployment Summary"
echo "============================================"
echo "  Project:    ${GCP_PROJECT}"
echo "  Region:     ${GCP_REGION}"
echo "  Target:     ${DEPLOY_TARGET}"
echo "  Webhook:    ${DEPLOY_WEBHOOK}"
echo "  Actions:    ${DEPLOY_ACTIONS}"
echo "  Dry Run:    ${DRY_RUN}"
[[ -n "${WEBHOOK_URL:-}" ]] && echo "  URL:        ${WEBHOOK_URL}"
echo "============================================"
echo ""

if [[ "$DEPLOY_TARGET" == "preview" ]]; then
  log_info "Test with: gactions test --project ${GCP_PROJECT}"
  log_info "Or use the Actions Console simulator at:"
  log_info "  https://console.actions.google.com/project/${GCP_PROJECT}/simulator"
fi

log_ok "Deployment complete!"
