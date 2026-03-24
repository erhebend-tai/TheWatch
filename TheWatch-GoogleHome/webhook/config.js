/**
 * ============================================================================
 * WRITE-AHEAD LOG (WAL)
 * ============================================================================
 * File:        config.js
 * Module:      TheWatch Google Home Integration - Configuration
 * Created:     2026-03-24
 * Author:      TheWatch Platform Team
 * ----------------------------------------------------------------------------
 * PURPOSE:
 *   Centralized configuration for the Google Home webhook fulfillment service.
 *   All API endpoints, timeouts, retry policies, feature flags, and environment
 *   variable mappings are consolidated here.
 *
 * DEPENDENCIES:
 *   - dotenv (loads .env file in non-production environments)
 *
 * EXAMPLE USAGE:
 *   const config = require('./config');
 *   console.log(config.api.baseUrl); // "https://api.thewatch.app"
 *   console.log(config.api.timeout); // 8000
 *
 * CHANGES:
 *   2026-03-24  Initial creation with full config surface
 * ============================================================================
 */

'use strict';

require('dotenv').config();

const config = {
  // ---------------------------------------------------------------------------
  // TheWatch Dashboard API (Aspire backend)
  // ---------------------------------------------------------------------------
  api: {
    baseUrl: process.env.THEWATCH_API_BASE_URL || 'https://api.thewatch.app',
    timeout: parseInt(process.env.THEWATCH_API_TIMEOUT, 10) || 8000,
    retries: parseInt(process.env.THEWATCH_API_RETRIES, 10) || 3,
    retryDelay: parseInt(process.env.THEWATCH_API_RETRY_DELAY, 10) || 1000,
    retryBackoffMultiplier: parseFloat(process.env.THEWATCH_API_RETRY_BACKOFF) || 2.0,
    endpoints: {
      alert:       '/api/iot/alert',
      checkIn:     '/api/iot/checkin',
      status:      '/api/iot/status',    // + /{userId}
      cancel:      '/api/iot/cancel',
      responders:  '/api/iot/responders', // + /{userId}
      volunteers:  '/api/iot/volunteers', // + /{userId}
      contacts:    '/api/iot/contacts',   // + /{userId}
      evacuation:  '/api/iot/evacuation', // + /{locationId}
      shelters:    '/api/iot/shelters',   // + /{locationId}
      medical:     '/api/iot/medical',    // + /{userId}
      phrase:      '/api/iot/phrase',     // POST set emergency phrase
      duress:      '/api/iot/duress',     // POST silent duress signal
    },
  },

  // ---------------------------------------------------------------------------
  // IoT source identifier for all requests originating from Google Home
  // ---------------------------------------------------------------------------
  iot: {
    source: 'GOOGLE_HOME',
    deviceFamily: 'SMART_SPEAKER',
  },

  // ---------------------------------------------------------------------------
  // OAuth2 / Account Linking
  // ---------------------------------------------------------------------------
  auth: {
    tokenVerifyUrl: process.env.THEWATCH_TOKEN_VERIFY_URL || 'https://auth.thewatch.app/oauth2/introspect',
    clientId: process.env.THEWATCH_OAUTH_CLIENT_ID || '',
    clientSecret: process.env.THEWATCH_OAUTH_CLIENT_SECRET || '',
    jwtSecret: process.env.THEWATCH_JWT_SECRET || '',
  },

  // ---------------------------------------------------------------------------
  // Server settings
  // ---------------------------------------------------------------------------
  server: {
    port: parseInt(process.env.PORT, 10) || 3000,
    env: process.env.NODE_ENV || 'development',
  },

  // ---------------------------------------------------------------------------
  // Logging
  // ---------------------------------------------------------------------------
  logging: {
    level: process.env.LOG_LEVEL || 'info',
    format: process.env.LOG_FORMAT || 'json',
  },

  // ---------------------------------------------------------------------------
  // Rate limiting
  // ---------------------------------------------------------------------------
  rateLimit: {
    windowMs: parseInt(process.env.RATE_LIMIT_WINDOW_MS, 10) || 60000,
    max: parseInt(process.env.RATE_LIMIT_MAX, 10) || 60,
  },

  // ---------------------------------------------------------------------------
  // Feature flags
  // ---------------------------------------------------------------------------
  features: {
    silentDuressEnabled: process.env.FEATURE_SILENT_DURESS === 'true',
    evacuationInfoEnabled: process.env.FEATURE_EVACUATION_INFO !== 'false',
    shelterInfoEnabled: process.env.FEATURE_SHELTER_INFO !== 'false',
    medicalInfoEnabled: process.env.FEATURE_MEDICAL_INFO !== 'false',
  },
};

module.exports = config;
