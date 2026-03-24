/**
 * =============================================================================
 * WRITE-AHEAD LOG (WAL) - api-client.js
 * =============================================================================
 * PURPOSE:
 *   Axios-based HTTP client adapter for calling TheWatch Dashboard API from
 *   the Alexa Lambda function. Handles authentication, retries with exponential
 *   backoff, error classification, circuit breaking, and structured logging.
 *
 * ARCHITECTURE:
 *   - Hexagonal adapter: implements outbound API port for the Lambda handlers
 *   - Wraps axios with interceptors for auth headers, request IDs, and logging
 *   - Uses axios-retry for transient failure recovery
 *   - Classifies errors into: TRANSIENT, AUTH, CLIENT, SERVER, NETWORK, UNKNOWN
 *   - Circuit breaker prevents cascading failures when API is down
 *
 * EXAMPLE USAGE:
 *   const apiClient = require('./api-client');
 *
 *   // Trigger SOS alert
 *   const result = await apiClient.triggerSOS({
 *     alexaUserId: 'amzn1.ask.account.xxx',
 *     source: 'ALEXA',
 *     latitude: 40.7128,
 *     longitude: -74.0060,
 *   });
 *
 *   // Check user status
 *   const status = await apiClient.getStatus('amzn1.ask.account.xxx');
 *
 *   // Cancel active alert
 *   await apiClient.cancelSOS({ alexaUserId: 'amzn1.ask.account.xxx' });
 *
 * ERROR RESPONSE SHAPE:
 *   { success: false, errorType: 'TRANSIENT'|'AUTH'|..., message: '...', statusCode: 503 }
 * =============================================================================
 */

'use strict';

const axios = require('axios');
const axiosRetry = require('axios-retry').default || require('axios-retry');
const { v4: uuidv4 } = require('uuid');
const config = require('./config');

// ---------------------------------------------------------------------------
// Circuit Breaker (simple in-memory)
// ---------------------------------------------------------------------------
const circuitState = {
  failures: 0,
  lastFailureTime: 0,
  isOpen: false,
};

function checkCircuit() {
  if (!circuitState.isOpen) return true;
  const elapsed = Date.now() - circuitState.lastFailureTime;
  if (elapsed >= config.circuitBreaker.resetTimeoutMs) {
    circuitState.isOpen = false;
    circuitState.failures = 0;
    console.log('[api-client] Circuit breaker HALF-OPEN, allowing request');
    return true;
  }
  return false;
}

function recordSuccess() {
  circuitState.failures = 0;
  circuitState.isOpen = false;
}

function recordFailure() {
  circuitState.failures += 1;
  circuitState.lastFailureTime = Date.now();
  if (circuitState.failures >= config.circuitBreaker.failureThreshold) {
    circuitState.isOpen = true;
    console.warn(`[api-client] Circuit breaker OPEN after ${circuitState.failures} failures`);
  }
}

// ---------------------------------------------------------------------------
// Error Classification
// ---------------------------------------------------------------------------
const ErrorType = Object.freeze({
  TRANSIENT: 'TRANSIENT',
  AUTH: 'AUTH',
  CLIENT: 'CLIENT',
  SERVER: 'SERVER',
  NETWORK: 'NETWORK',
  CIRCUIT_OPEN: 'CIRCUIT_OPEN',
  UNKNOWN: 'UNKNOWN',
});

function classifyError(error) {
  if (!error.response) {
    return { errorType: ErrorType.NETWORK, message: error.message || 'Network error', statusCode: 0 };
  }
  const status = error.response.status;
  if (status === 401 || status === 403) {
    return { errorType: ErrorType.AUTH, message: 'Authentication failed', statusCode: status };
  }
  if (status >= 400 && status < 500) {
    return { errorType: ErrorType.CLIENT, message: error.response.data?.message || 'Client error', statusCode: status };
  }
  if (config.retry.retryableStatusCodes.includes(status)) {
    return { errorType: ErrorType.TRANSIENT, message: 'Transient server error', statusCode: status };
  }
  if (status >= 500) {
    return { errorType: ErrorType.SERVER, message: 'Server error', statusCode: status };
  }
  return { errorType: ErrorType.UNKNOWN, message: 'Unknown error', statusCode: status };
}

// ---------------------------------------------------------------------------
// Axios instance
// ---------------------------------------------------------------------------
const client = axios.create({
  baseURL: config.api.baseUrl,
  timeout: config.api.timeoutMs,
  headers: {
    'Content-Type': 'application/json',
    'Accept': 'application/json',
    'X-IoT-Source': config.iotSource,
  },
});

// Auth interceptor
client.interceptors.request.use((reqConfig) => {
  const requestId = uuidv4();
  reqConfig.headers['X-Request-ID'] = requestId;
  reqConfig.headers['X-Correlation-ID'] = requestId;
  if (config.api.key) {
    reqConfig.headers['Authorization'] = `Bearer ${config.api.key}`;
  }
  console.log(`[api-client] >> ${reqConfig.method?.toUpperCase()} ${reqConfig.url} [${requestId}]`);
  return reqConfig;
});

// Response logging interceptor
client.interceptors.response.use(
  (response) => {
    console.log(`[api-client] << ${response.status} ${response.config.url}`);
    recordSuccess();
    return response;
  },
  (error) => {
    const status = error.response?.status || 'NETWORK';
    console.error(`[api-client] << ${status} ${error.config?.url} - ${error.message}`);
    recordFailure();
    return Promise.reject(error);
  }
);

// Retry configuration
axiosRetry(client, {
  retries: config.retry.maxRetries,
  retryDelay: (retryCount) => {
    const delay = config.retry.initialDelayMs * Math.pow(config.retry.backoffFactor, retryCount - 1);
    console.log(`[api-client] Retry #${retryCount}, waiting ${delay}ms`);
    return delay;
  },
  retryCondition: (error) => {
    if (!error.response) return true; // network errors
    return config.retry.retryableStatusCodes.includes(error.response.status);
  },
});

// ---------------------------------------------------------------------------
// API Methods
// ---------------------------------------------------------------------------

/**
 * Wraps an API call with circuit breaker check and error classification.
 */
async function safeCall(fn) {
  if (!checkCircuit()) {
    return {
      success: false,
      errorType: ErrorType.CIRCUIT_OPEN,
      message: 'Service temporarily unavailable. Please try again in a moment.',
      statusCode: 503,
    };
  }
  try {
    const response = await fn();
    return { success: true, data: response.data };
  } catch (error) {
    const classified = classifyError(error);
    return { success: false, ...classified };
  }
}

/**
 * Trigger an SOS alert.
 * @param {Object} params
 * @param {string} params.alexaUserId - Alexa account user ID
 * @param {string} params.source      - IoT source (default: ALEXA)
 * @param {number} [params.latitude]  - Optional GPS latitude
 * @param {number} [params.longitude] - Optional GPS longitude
 * @param {string} [params.scope]     - Alert scope (e.g., 'SilentDuress')
 */
async function triggerSOS(params) {
  return safeCall(() =>
    client.post(config.api.endpoints.alert, {
      userId: params.alexaUserId,
      source: params.source || config.iotSource,
      latitude: params.latitude || null,
      longitude: params.longitude || null,
      scope: params.scope || 'Standard',
      timestamp: new Date().toISOString(),
      deviceType: 'ALEXA',
    })
  );
}

/**
 * Submit a check-in confirmation.
 */
async function checkIn(params) {
  return safeCall(() =>
    client.post(config.api.endpoints.checkin, {
      userId: params.alexaUserId,
      source: config.iotSource,
      status: params.status || 'OK',
      timestamp: new Date().toISOString(),
    })
  );
}

/**
 * Get current status for a user.
 */
async function getStatus(alexaUserId) {
  return safeCall(() =>
    client.get(`${config.api.endpoints.status}/${encodeURIComponent(alexaUserId)}`)
  );
}

/**
 * Cancel an active SOS alert.
 */
async function cancelSOS(params) {
  return safeCall(() =>
    client.post(config.api.endpoints.cancel, {
      userId: params.alexaUserId,
      source: config.iotSource,
      reason: params.reason || 'UserCancelled',
      timestamp: new Date().toISOString(),
    })
  );
}

/**
 * Set/update emergency phrase for voice detection.
 */
async function setEmergencyPhrase(params) {
  return safeCall(() =>
    client.post(config.api.endpoints.phrase, {
      userId: params.alexaUserId,
      phrase: params.phrase,
      source: config.iotSource,
      timestamp: new Date().toISOString(),
    })
  );
}

/**
 * Get nearby volunteer responders.
 */
async function getNearbyResponders(alexaUserId) {
  return safeCall(() =>
    client.get(`${config.api.endpoints.responders}?userId=${encodeURIComponent(alexaUserId)}&source=${config.iotSource}`)
  );
}

/**
 * Get or toggle volunteer status.
 */
async function getVolunteerStatus(alexaUserId) {
  return safeCall(() =>
    client.get(`${config.api.endpoints.volunteer}?userId=${encodeURIComponent(alexaUserId)}`)
  );
}

/**
 * Get emergency contacts list.
 */
async function getEmergencyContacts(alexaUserId) {
  return safeCall(() =>
    client.get(`${config.api.endpoints.contacts}?userId=${encodeURIComponent(alexaUserId)}`)
  );
}

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------
module.exports = {
  triggerSOS,
  checkIn,
  getStatus,
  cancelSOS,
  setEmergencyPhrase,
  getNearbyResponders,
  getVolunteerStatus,
  getEmergencyContacts,
  ErrorType,
  // Exposed for testing
  _client: client,
  _circuitState: circuitState,
};
