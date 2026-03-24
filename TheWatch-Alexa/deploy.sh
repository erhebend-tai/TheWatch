#!/usr/bin/env bash
# =============================================================================
# WRITE-AHEAD LOG (WAL) - deploy.sh
# =============================================================================
# PURPOSE:
#   Deployment script for TheWatch Alexa Skill. Handles both the Lambda function
#   deployment (backend) and the skill package deployment (interaction model +
#   manifest) via ASK CLI and AWS CLI.
#
# ARCHITECTURE:
#   - Builds Lambda package (zip) from lambda/ directory
#   - Deploys Lambda function via AWS CLI
#   - Deploys skill package via ASK CLI (skill.json + interaction models)
#   - Supports staging and production environments
#   - Validates prerequisites before deploying
#
# EXAMPLE USAGE:
#   # Deploy everything (Lambda + skill package) to staging
#   ./deploy.sh --env staging
#
#   # Deploy only the Lambda function
#   ./deploy.sh --lambda-only
#
#   # Deploy only the skill package (interaction model changes)
#   ./deploy.sh --skill-only
#
#   # Production deploy with confirmation
#   ./deploy.sh --env production
#
#   # Dry run (validate only, no deploy)
#   ./deploy.sh --dry-run
# =============================================================================

set -euo pipefail

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAMBDA_DIR="${SCRIPT_DIR}/lambda"
SKILL_DIR="${SCRIPT_DIR}/skill-package"
DEPLOY_DIR="${SCRIPT_DIR}/deploy"
ENV_FILE="${SCRIPT_DIR}/.env"

# Defaults
ENVIRONMENT="staging"
LAMBDA_ONLY=false
SKILL_ONLY=false
DRY_RUN=false
SKIP_TESTS=false

# AWS/ASK defaults (override via .env or env vars)
AWS_REGION="${AWS_REGION:-us-east-1}"
LAMBDA_FUNCTION_NAME="${AWS_LAMBDA_FUNCTION_NAME:-thewatch-alexa-skill}"
SKILL_ID="${ALEXA_SKILL_ID:-}"

# ---------------------------------------------------------------------------
# Color output
# ---------------------------------------------------------------------------
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log_info()  { echo -e "${BLUE}[INFO]${NC}  $1"; }
log_ok()    { echo -e "${GREEN}[OK]${NC}    $1"; }
log_warn()  { echo -e "${YELLOW}[WARN]${NC}  $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# ---------------------------------------------------------------------------
# Parse arguments
# ---------------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case $1 in
    --env)
      ENVIRONMENT="$2"
      shift 2
      ;;
    --lambda-only)
      LAMBDA_ONLY=true
      shift
      ;;
    --skill-only)
      SKILL_ONLY=true
      shift
      ;;
    --dry-run)
      DRY_RUN=true
      shift
      ;;
    --skip-tests)
      SKIP_TESTS=true
      shift
      ;;
    --skill-id)
      SKILL_ID="$2"
      shift 2
      ;;
    -h|--help)
      echo "Usage: deploy.sh [OPTIONS]"
      echo ""
      echo "Options:"
      echo "  --env <env>       Environment: staging|production (default: staging)"
      echo "  --lambda-only     Deploy only the Lambda function"
      echo "  --skill-only      Deploy only the skill package"
      echo "  --dry-run         Validate without deploying"
      echo "  --skip-tests      Skip running tests before deploy"
      echo "  --skill-id <id>   Override Alexa Skill ID"
      echo "  -h, --help        Show this help"
      exit 0
      ;;
    *)
      log_error "Unknown option: $1"
      exit 1
      ;;
  esac
done

# ---------------------------------------------------------------------------
# Load .env if exists
# ---------------------------------------------------------------------------
if [[ -f "$ENV_FILE" ]]; then
  log_info "Loading environment from .env"
  set -a
  source "$ENV_FILE"
  set +a
fi

# Override function name for production
if [[ "$ENVIRONMENT" == "production" ]]; then
  LAMBDA_FUNCTION_NAME="${LAMBDA_FUNCTION_NAME}-prod"
fi

# ---------------------------------------------------------------------------
# Prerequisite checks
# ---------------------------------------------------------------------------
log_info "Checking prerequisites..."

check_command() {
  if ! command -v "$1" &> /dev/null; then
    log_error "$1 is required but not installed."
    return 1
  fi
  log_ok "$1 found"
}

PREREQ_OK=true
check_command "node" || PREREQ_OK=false
check_command "npm" || PREREQ_OK=false
check_command "aws" || PREREQ_OK=false
check_command "ask" || PREREQ_OK=false
check_command "zip" || PREREQ_OK=false

if [[ "$PREREQ_OK" != "true" ]]; then
  log_error "Missing prerequisites. Install them and retry."
  exit 1
fi

# Verify Node.js version >= 18
NODE_VERSION=$(node -v | sed 's/v//' | cut -d. -f1)
if [[ "$NODE_VERSION" -lt 18 ]]; then
  log_error "Node.js >= 18 required (found v${NODE_VERSION})"
  exit 1
fi
log_ok "Node.js v${NODE_VERSION}"

# Verify AWS credentials
if ! aws sts get-caller-identity &> /dev/null; then
  log_error "AWS credentials not configured. Run 'aws configure' first."
  exit 1
fi
log_ok "AWS credentials valid"

# ---------------------------------------------------------------------------
# Run tests
# ---------------------------------------------------------------------------
if [[ "$SKIP_TESTS" != "true" ]]; then
  log_info "Running tests..."
  cd "$LAMBDA_DIR"
  npm install --production=false 2>/dev/null
  cd "$SCRIPT_DIR"
  npm test || {
    log_error "Tests failed. Fix them before deploying."
    exit 1
  }
  log_ok "All tests passed"
else
  log_warn "Skipping tests (--skip-tests)"
fi

# ---------------------------------------------------------------------------
# Production confirmation gate
# ---------------------------------------------------------------------------
if [[ "$ENVIRONMENT" == "production" && "$DRY_RUN" != "true" ]]; then
  log_warn "You are deploying to PRODUCTION."
  read -p "Type 'deploy-production' to confirm: " CONFIRM
  if [[ "$CONFIRM" != "deploy-production" ]]; then
    log_error "Production deployment cancelled."
    exit 1
  fi
fi

# ---------------------------------------------------------------------------
# Build Lambda package
# ---------------------------------------------------------------------------
if [[ "$SKILL_ONLY" != "true" ]]; then
  log_info "Building Lambda package..."

  mkdir -p "$DEPLOY_DIR"

  cd "$LAMBDA_DIR"

  # Install production dependencies
  npm install --production 2>/dev/null
  log_ok "Dependencies installed"

  # Create zip
  LAMBDA_ZIP="${DEPLOY_DIR}/lambda-${ENVIRONMENT}-$(date +%Y%m%d-%H%M%S).zip"
  zip -r "$LAMBDA_ZIP" . \
    -x "*.git*" \
    -x "node_modules/.cache/*" \
    -x "*.test.*" \
    -x "*.spec.*" \
    -x "coverage/*" \
    -x ".nyc_output/*" \
    > /dev/null

  LAMBDA_SIZE=$(du -sh "$LAMBDA_ZIP" | cut -f1)
  log_ok "Lambda package built: $LAMBDA_ZIP ($LAMBDA_SIZE)"

  cd "$SCRIPT_DIR"

  # Deploy Lambda
  if [[ "$DRY_RUN" != "true" ]]; then
    log_info "Deploying Lambda function: $LAMBDA_FUNCTION_NAME ($ENVIRONMENT)..."

    aws lambda update-function-code \
      --function-name "$LAMBDA_FUNCTION_NAME" \
      --zip-file "fileb://$LAMBDA_ZIP" \
      --region "$AWS_REGION" \
      --publish \
      > /dev/null

    log_ok "Lambda function deployed"

    # Update environment variables
    log_info "Updating Lambda environment variables..."
    aws lambda update-function-configuration \
      --function-name "$LAMBDA_FUNCTION_NAME" \
      --environment "Variables={THEWATCH_ENV=$ENVIRONMENT,THEWATCH_API_BASE_URL=${THEWATCH_API_BASE_URL:-https://api.thewatch.app},THEWATCH_API_KEY=${THEWATCH_API_KEY:-},THEWATCH_API_TIMEOUT_MS=${THEWATCH_API_TIMEOUT_MS:-8000},THEWATCH_RETRY_MAX=${THEWATCH_RETRY_MAX:-3},THEWATCH_SOS_CONFIRM=${THEWATCH_SOS_CONFIRM:-true},THEWATCH_LOG_LEVEL=${THEWATCH_LOG_LEVEL:-info}}" \
      --region "$AWS_REGION" \
      > /dev/null

    log_ok "Lambda environment updated"
  else
    log_warn "[DRY RUN] Would deploy Lambda: $LAMBDA_FUNCTION_NAME"
  fi
fi

# ---------------------------------------------------------------------------
# Deploy Skill Package
# ---------------------------------------------------------------------------
if [[ "$LAMBDA_ONLY" != "true" ]]; then
  log_info "Deploying skill package..."

  if [[ -z "$SKILL_ID" ]]; then
    log_warn "No ALEXA_SKILL_ID set. Creating new skill..."

    if [[ "$DRY_RUN" != "true" ]]; then
      SKILL_ID=$(ask smapi create-skill-for-vendor \
        --manifest "file:${SKILL_DIR}/skill.json" \
        2>/dev/null | jq -r '.skillId')

      log_ok "Skill created: $SKILL_ID"
      log_info "Save this SKILL_ID in your .env file: ALEXA_SKILL_ID=$SKILL_ID"
    else
      log_warn "[DRY RUN] Would create new skill"
    fi
  else
    log_info "Updating existing skill: $SKILL_ID"

    if [[ "$DRY_RUN" != "true" ]]; then
      # Update skill manifest
      ask smapi update-skill-manifest \
        --skill-id "$SKILL_ID" \
        --manifest "file:${SKILL_DIR}/skill.json" \
        2>/dev/null

      log_ok "Skill manifest updated"

      # Update interaction models for each locale
      for LOCALE_FILE in "${SKILL_DIR}"/interactionModels/custom/*.json; do
        LOCALE=$(basename "$LOCALE_FILE" .json)
        log_info "Updating interaction model: $LOCALE"

        ask smapi set-interaction-model \
          --skill-id "$SKILL_ID" \
          --stage development \
          --locale "$LOCALE" \
          --interaction-model "file:${LOCALE_FILE}" \
          2>/dev/null

        log_ok "Interaction model updated: $LOCALE"
      done
    else
      log_warn "[DRY RUN] Would update skill: $SKILL_ID"
    fi
  fi
fi

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo ""
echo "=============================================="
echo -e "${GREEN}Deployment Summary${NC}"
echo "=============================================="
echo "Environment:     $ENVIRONMENT"
echo "Lambda Function: $LAMBDA_FUNCTION_NAME"
echo "Skill ID:        ${SKILL_ID:-N/A}"
echo "Region:          $AWS_REGION"
echo "Dry Run:         $DRY_RUN"
echo "Timestamp:       $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "=============================================="

if [[ "$DRY_RUN" == "true" ]]; then
  log_warn "This was a dry run. No changes were made."
else
  log_ok "Deployment complete!"
fi
