/**
 * =============================================================================
 * WRITE-AHEAD LOG (WAL) - config.js
 * =============================================================================
 * PURPOSE:
 *   Centralized configuration for TheWatch Alexa Skill Lambda function.
 *   All external endpoints, timeouts, retry policies, feature flags, and
 *   environment-specific overrides live here.
 *
 * ARCHITECTURE:
 *   - Reads from process.env (Lambda environment variables)
 *   - Falls back to sensible defaults for local development
 *   - Exports a frozen config object (immutable at runtime)
 *   - Follows hexagonal pattern: this is infra config consumed by adapters
 *
 * EXAMPLE USAGE:
 *   const config = require('./config');
 *   console.log(config.api.baseUrl);     // "https://api.thewatch.app"
 *   console.log(config.retry.maxRetries); // 3
 *   console.log(config.sos.confirmationRequired); // true
 *
 * ENVIRONMENT VARIABLES:
 *   THEWATCH_API_BASE_URL      - Dashboard API base URL
 *   THEWATCH_API_KEY            - API key for authentication
 *   THEWATCH_API_TIMEOUT_MS     - Request timeout in milliseconds
 *   THEWATCH_RETRY_MAX          - Max retry attempts
 *   THEWATCH_RETRY_DELAY_MS     - Initial retry delay in ms
 *   THEWATCH_LOG_LEVEL          - Logging level (debug|info|warn|error)
 *   THEWATCH_ENV                - Environment (development|staging|production)
 *   THEWATCH_SOS_CONFIRM        - Whether SOS requires voice confirmation ("true"/"false")
 *   THEWATCH_SILENT_DURESS_CODE - Numeric code for silent duress verification
 * =============================================================================
 */

'use strict';

const config = Object.freeze({

  // ---------------------------------------------------------------------------
  // Environment
  // ---------------------------------------------------------------------------
  env: process.env.THEWATCH_ENV || 'development',
  logLevel: process.env.THEWATCH_LOG_LEVEL || 'info',

  // ---------------------------------------------------------------------------
  // TheWatch Dashboard API
  // ---------------------------------------------------------------------------
  api: Object.freeze({
    baseUrl: process.env.THEWATCH_API_BASE_URL || 'https://api.thewatch.app',
    key: process.env.THEWATCH_API_KEY || '',
    timeoutMs: parseInt(process.env.THEWATCH_API_TIMEOUT_MS, 10) || 8000,
    endpoints: Object.freeze({
      alert:    '/api/iot/alert',
      checkin:  '/api/iot/checkin',
      status:   '/api/iot/status',   // GET /api/iot/status/{userId}
      cancel:   '/api/iot/cancel',
      phrase:   '/api/iot/phrase',    // POST set emergency phrase
      responders: '/api/iot/responders', // GET nearby responders
      volunteer:  '/api/iot/volunteer',  // GET/POST volunteer status
      contacts:   '/api/iot/contacts',   // GET emergency contacts
    }),
  }),

  // ---------------------------------------------------------------------------
  // Retry policy (exponential backoff)
  // ---------------------------------------------------------------------------
  retry: Object.freeze({
    maxRetries: parseInt(process.env.THEWATCH_RETRY_MAX, 10) || 3,
    initialDelayMs: parseInt(process.env.THEWATCH_RETRY_DELAY_MS, 10) || 500,
    backoffFactor: 2,
    retryableStatusCodes: [408, 429, 500, 502, 503, 504],
  }),

  // ---------------------------------------------------------------------------
  // SOS configuration
  // ---------------------------------------------------------------------------
  sos: Object.freeze({
    confirmationRequired: (process.env.THEWATCH_SOS_CONFIRM || 'true') === 'true',
    confirmationTimeoutSec: 10,
    source: 'ALEXA',
    silentDuressCode: process.env.THEWATCH_SILENT_DURESS_CODE || '9999',
  }),

  // ---------------------------------------------------------------------------
  // IoT source identifier
  // ---------------------------------------------------------------------------
  iotSource: 'ALEXA',

  // ---------------------------------------------------------------------------
  // Supported locales
  // ---------------------------------------------------------------------------
  supportedLocales: Object.freeze(['en-US', 'en-GB', 'es-US', 'es-ES', 'es-MX']),

  // ---------------------------------------------------------------------------
  // Session attribute keys
  // ---------------------------------------------------------------------------
  sessionKeys: Object.freeze({
    SOS_PENDING: 'sosPendingConfirmation',
    LAST_INTENT: 'lastIntent',
    USER_ID_MAPPED: 'userIdMapped',
    LOCALE: 'locale',
  }),

  // ---------------------------------------------------------------------------
  // Circuit breaker (for api-client)
  // ---------------------------------------------------------------------------
  circuitBreaker: Object.freeze({
    failureThreshold: 5,
    resetTimeoutMs: 30000,
  }),
});

module.exports = config;
