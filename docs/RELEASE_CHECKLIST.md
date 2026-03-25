# TheWatch v1.0.0 Release Checklist

Step-by-step checklist for the production release. Every item must be completed
and verified by a human before proceeding to the next step.

**Do not skip steps. This is a life-safety application.**

---

## Pre-Release Verification

- [ ] Review all open issues in the GitHub issue tracker
  - Confirm no Critical or Blocker issues remain open
  - Verify issues #46, #47, #48, #49 are resolved
  - Document any known issues that ship with v1.0.0

- [ ] Run full test suite including performance tests
  ```bash
  dotnet test -c Release --logger "trx" -- RunConfiguration.TreatNoTestsAsError=false
  ```
  - Confirm all unit tests pass
  - Confirm all integration tests pass
  - Run load test against staging: SOS endpoint must sustain < 500ms p99 at 100 concurrent requests

- [ ] Verify version is correct in `eng/Versions.props`
  - VersionPrefix should be `1.0.0`
  - Build a release package locally to confirm version string:
    ```bash
    dotnet build -c Release -p:OfficialBuild=true -p:DotNetFinalVersionKind=release
    ```

## Infrastructure

- [ ] Verify Azure infrastructure via `infra/deploy.sh --environment production`
  - Or deploy Bicep directly:
    ```bash
    az deployment group create \
      --resource-group Watch-Init \
      --template-file infra/main.bicep \
      --parameters infra/main.parameters.json
    ```
  - Confirm all resources provisioned successfully
  - Verify connection strings in Key Vault or GitHub Secrets

- [ ] Deploy monitoring alerts
  ```bash
  az deployment group create \
    --resource-group Watch-Init \
    --template-file infra/monitoring.bicep \
    --parameters alertEmailAddress=<actual-ops-email>
  ```
  - Verify all 7 alert rules are active in Azure Monitor
  - Confirm action group email address is correct (not placeholder)

- [ ] Deploy operations dashboard
  ```bash
  az portal dashboard create \
    --resource-group Watch-Init \
    --name thewatch-ops-dashboard \
    --input-path infra/dashboards.json
  ```
  - Verify dashboard loads in Azure Portal
  - Confirm all 5 panels render data from Log Analytics

- [ ] Verify production CORS configuration
  - Confirm `Cors:AllowedOrigins` in production appsettings includes only real domains
  - Verify SignalR WebSocket connections work from allowed origins

## Mobile App Submission

- [ ] Submit Android to Google Play Store via `android-release.yml` workflow
  - Verify store listing metadata in `TheWatch-Android/store-listing/en-US/`
  - Replace privacy policy URL placeholder with actual URL
  - Upload screenshots (phone, 7-inch tablet, 10-inch tablet)
  - Upload feature graphic (1024x500)
  - Set content rating questionnaire responses
  - Target API level 35+ (Android 15)
  - Submit for review (expect 1-7 days)

- [ ] Submit iOS to Apple App Store via `ios-release.yml` workflow
  - Verify store listing metadata in `TheWatch-iOS/store-listing/`
  - Replace privacy and support URL placeholders with actual URLs
  - Upload screenshots for all required device sizes (6.7", 6.1", 5.5")
  - Complete App Privacy questionnaire (location always, microphone, camera)
  - Set age rating: 12+ (infrequent/mild references to safety topics)
  - Submit for review (expect 1-3 days)

## Release Execution

- [ ] Tag the release
  ```bash
  git tag -a v1.0.0 -m "v1.0.0 — First production release of TheWatch emergency response platform"
  git push origin v1.0.0
  ```

- [ ] Trigger the release workflow
  ```bash
  gh workflow run release.yml -f version=1.0.0
  ```
  - Or use GitHub Actions UI: Actions > Release > Run workflow > version: 1.0.0
  - Verify the workflow completes successfully
  - Confirm GitHub Release is created with changelog and NuGet package

- [ ] Deploy to production via deploy workflow
  ```bash
  gh workflow run deploy.yml -f environment=production -f deploy_infra=false -f deploy_containers=true
  ```
  - Approve the production environment gate when prompted
  - Wait for health check verification to pass

## Post-Release Verification

- [ ] Verify production monitoring alerts are active
  - Trigger a test alert by temporarily lowering a threshold, then restore
  - Confirm email notification is received by ops team
  - Verify Azure Portal dashboard shows live data

- [ ] Smoke test production API
  ```bash
  curl -s https://api.thewatch.app/health | jq .
  curl -s https://api.thewatch.app/alive
  ```
  - Verify `/health` returns Healthy status
  - Verify `/alive` returns 200

- [ ] Smoke test production SignalR
  - Open dashboard at https://dashboard.thewatch.app
  - Verify WebSocket connection establishes
  - Confirm real-time updates flow through

- [ ] Verify production Firebase Auth
  - Test sign-up flow on Android and iOS
  - Test sign-in flow
  - Test 2FA verification
  - Test password reset flow

## Announcement

- [ ] Announce release
  - Update project website
  - Post changelog summary to team communication channels
  - Notify beta testers that production is live
  - Update any external documentation or wikis

---

**Release completed by:** ___________________
**Date:** ___________________
**Notes:**


