/**
 * ============================================================================
 * WRITE-AHEAD LOG (WAL)
 * ============================================================================
 * File:        api-client.js
 * Module:      TheWatch Google Home Integration - API Client
 * Created:     2026-03-24
 * Author:      TheWatch Platform Team
 * ----------------------------------------------------------------------------
 * PURPOSE:
 *   Axios-based HTTP client for communicating with the TheWatch Dashboard API
 *   (.NET Aspire backend). Implements automatic retries with exponential
 *   backoff, request/response interceptors for auth token injection, error
 *   classification (transient vs permanent), structured logging, and
 *   circuit-breaker awareness.
 *
 * PORT/ADAPTER PATTERN:
 *   This is the ADAPTER that fulfills the "IoT Gateway Port" contract
 *   defined in TheWatch-Aspire. Every outgoing call carries the
 *   iot.source = "GOOGLE_HOME" header.
 *
 * DEPENDENCIES:
 *   - axios
 *   - config.js (local)
 *   - winston (for structured logging)
 *
 * EXAMPLE USAGE:
 *   const apiClient = require('./api-client');
 *   const result = await apiClient.triggerAlert(userId, { lat, lng, message });
 *   const status = await apiClient.getStatus(userId);
 *
 * ERROR CLASSIFICATION:
 *   - TRANSIENT: 408, 429, 500, 502, 503, 504 => retried automatically
 *   - PERMANENT: 400, 401, 403, 404, 409       => thrown immediately
 *   - NETWORK:   ECONNABORTED, ETIMEDOUT        => retried automatically
 *
 * CHANGES:
 *   2026-03-24  Initial creation with full retry + error classification
 * ============================================================================
 */

'use strict';

const axios = require('axios');
const config = require('./config');
const { createLogger } = require('./interceptors');

const logger = createLogger('api-client');

// ---------------------------------------------------------------------------
// Transient HTTP status codes that warrant automatic retry
// ---------------------------------------------------------------------------
const TRANSIENT_STATUS_CODES = new Set([408, 429, 500, 502, 503, 504]);
const TRANSIENT_ERROR_CODES = new Set(['ECONNABORTED', 'ETIMEDOUT', 'ECONNRESET', 'ENOTFOUND', 'EAI_AGAIN']);

/**
 * Classify whether an Axios error is transient (retryable) or permanent.
 * @param {Error} error - Axios error object
 * @returns {{ transient: boolean, code: string, message: string }}
 */
function classifyError(error) {
  if (!error.response) {
    // Network-level failure
    const code = error.code || 'UNKNOWN_NETWORK';
    return {
      transient: TRANSIENT_ERROR_CODES.has(code),
      code,
      message: error.message,
    };
  }

  const status = error.response.status;
  return {
    transient: TRANSIENT_STATUS_CODES.has(status),
    code: `HTTP_${status}`,
    message: error.response.data?.message || error.message,
  };
}

/**
 * Sleep for a given number of milliseconds.
 * @param {number} ms
 * @returns {Promise<void>}
 */
function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// ---------------------------------------------------------------------------
// Create Axios instance
// ---------------------------------------------------------------------------
const instance = axios.create({
  baseURL: config.api.baseUrl,
  timeout: config.api.timeout,
  headers: {
    'Content-Type': 'application/json',
    'X-IoT-Source': config.iot.source,
    'X-IoT-Device-Family': config.iot.deviceFamily,
  },
});

// Request interceptor: inject Bearer token when available
instance.interceptors.request.use((reqConfig) => {
  if (reqConfig._authToken) {
    reqConfig.headers.Authorization = `Bearer ${reqConfig._authToken}`;
  }
  reqConfig.headers['X-Request-Id'] = reqConfig._requestId || require('uuid').v4();
  reqConfig.metadata = { startTime: Date.now() };
  logger.debug('Outgoing request', {
    method: reqConfig.method,
    url: reqConfig.url,
    requestId: reqConfig.headers['X-Request-Id'],
  });
  return reqConfig;
});

// Response interceptor: log timing
instance.interceptors.response.use(
  (response) => {
    const duration = Date.now() - (response.config.metadata?.startTime || Date.now());
    logger.info('API response', {
      method: response.config.method,
      url: response.config.url,
      status: response.status,
      durationMs: duration,
    });
    return response;
  },
  (error) => {
    const duration = Date.now() - (error.config?.metadata?.startTime || Date.now());
    const classified = classifyError(error);
    logger.warn('API error', {
      method: error.config?.method,
      url: error.config?.url,
      ...classified,
      durationMs: duration,
    });
    error._classified = classified;
    return Promise.reject(error);
  }
);

// ---------------------------------------------------------------------------
// Retry wrapper
// ---------------------------------------------------------------------------
/**
 * Execute an Axios request with automatic retries for transient failures.
 * @param {Function} requestFn - A function returning an Axios promise
 * @param {number} [maxRetries] - Override default retry count
 * @returns {Promise<import('axios').AxiosResponse>}
 */
async function withRetry(requestFn, maxRetries = config.api.retries) {
  let lastError;
  for (let attempt = 0; attempt <= maxRetries; attempt++) {
    try {
      return await requestFn();
    } catch (error) {
      lastError = error;
      const classified = error._classified || classifyError(error);
      if (!classified.transient || attempt === maxRetries) {
        throw error;
      }
      const delay = config.api.retryDelay * Math.pow(config.api.retryBackoffMultiplier, attempt);
      logger.info(`Retry ${attempt + 1}/${maxRetries} after ${delay}ms`, {
        code: classified.code,
      });
      await sleep(delay);
    }
  }
  throw lastError;
}

// ---------------------------------------------------------------------------
// Public API methods — one per Dashboard API endpoint
// ---------------------------------------------------------------------------

const apiClient = {
  /**
   * Trigger an SOS alert from Google Home.
   * POST /api/iot/alert
   * @param {string} authToken - OAuth bearer token
   * @param {string} userId
   * @param {{ latitude?: number, longitude?: number, message?: string, severity?: string }} payload
   */
  async triggerAlert(authToken, userId, payload = {}) {
    return withRetry(() =>
      instance.post(config.api.endpoints.alert, {
        userId,
        source: config.iot.source,
        deviceFamily: config.iot.deviceFamily,
        timestamp: new Date().toISOString(),
        severity: payload.severity || 'CRITICAL',
        latitude: payload.latitude || null,
        longitude: payload.longitude || null,
        message: payload.message || 'SOS triggered via Google Home',
      }, { _authToken: authToken })
    );
  },

  /**
   * Request a check-in for the user.
   * POST /api/iot/checkin
   */
  async requestCheckIn(authToken, userId, payload = {}) {
    return withRetry(() =>
      instance.post(config.api.endpoints.checkIn, {
        userId,
        source: config.iot.source,
        deviceFamily: config.iot.deviceFamily,
        timestamp: new Date().toISOString(),
        type: payload.type || 'SCHEDULED',
        message: payload.message || 'Check-in requested via Google Home',
      }, { _authToken: authToken })
    );
  },

  /**
   * Get current safety status for the user.
   * GET /api/iot/status/{userId}
   */
  async getStatus(authToken, userId) {
    return withRetry(() =>
      instance.get(`${config.api.endpoints.status}/${userId}`, {
        _authToken: authToken,
      })
    );
  },

  /**
   * Cancel an active SOS alert.
   * POST /api/iot/cancel
   */
  async cancelAlert(authToken, userId, payload = {}) {
    return withRetry(() =>
      instance.post(config.api.endpoints.cancel, {
        userId,
        source: config.iot.source,
        reason: payload.reason || 'Cancelled via Google Home',
        timestamp: new Date().toISOString(),
      }, { _authToken: authToken })
    );
  },

  /**
   * Set or update the user's emergency phrase.
   * POST /api/iot/phrase
   */
  async setEmergencyPhrase(authToken, userId, phrase) {
    return withRetry(() =>
      instance.post(config.api.endpoints.phrase, {
        userId,
        source: config.iot.source,
        emergencyPhrase: phrase,
        timestamp: new Date().toISOString(),
      }, { _authToken: authToken })
    );
  },

  /**
   * Get nearby responders for the user.
   * GET /api/iot/responders/{userId}
   */
  async getNearbyResponders(authToken, userId) {
    return withRetry(() =>
      instance.get(`${config.api.endpoints.responders}/${userId}`, {
        _authToken: authToken,
      })
    );
  },

  /**
   * Get volunteer status for the user.
   * GET /api/iot/volunteers/{userId}
   */
  async getVolunteerStatus(authToken, userId) {
    return withRetry(() =>
      instance.get(`${config.api.endpoints.volunteers}/${userId}`, {
        _authToken: authToken,
      })
    );
  },

  /**
   * Get emergency contacts for the user.
   * GET /api/iot/contacts/{userId}
   */
  async getEmergencyContacts(authToken, userId) {
    return withRetry(() =>
      instance.get(`${config.api.endpoints.contacts}/${userId}`, {
        _authToken: authToken,
      })
    );
  },

  /**
   * Send a silent duress signal (no audible confirmation).
   * POST /api/iot/duress
   */
  async sendSilentDuress(authToken, userId, payload = {}) {
    return withRetry(() =>
      instance.post(config.api.endpoints.duress, {
        userId,
        source: config.iot.source,
        deviceFamily: config.iot.deviceFamily,
        timestamp: new Date().toISOString(),
        silent: true,
        latitude: payload.latitude || null,
        longitude: payload.longitude || null,
      }, { _authToken: authToken })
    );
  },

  /**
   * Get evacuation information for a location.
   * GET /api/iot/evacuation/{locationId}
   */
  async getEvacuationInfo(authToken, locationId) {
    return withRetry(() =>
      instance.get(`${config.api.endpoints.evacuation}/${locationId}`, {
        _authToken: authToken,
      })
    );
  },

  /**
   * Get nearby shelter information.
   * GET /api/iot/shelters/{locationId}
   */
  async getShelterInfo(authToken, locationId) {
    return withRetry(() =>
      instance.get(`${config.api.endpoints.shelters}/${locationId}`, {
        _authToken: authToken,
      })
    );
  },

  /**
   * Get medical profile for the user.
   * GET /api/iot/medical/{userId}
   */
  async getMedicalInfo(authToken, userId) {
    return withRetry(() =>
      instance.get(`${config.api.endpoints.medical}/${userId}`, {
        _authToken: authToken,
      })
    );
  },
};

module.exports = apiClient;
