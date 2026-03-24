/**
 * =============================================================================
 * WRITE-AHEAD LOG (WAL) - interceptors.js
 * =============================================================================
 * PURPOSE:
 *   Request and response interceptors for TheWatch Alexa Skill. Interceptors
 *   run before/after every intent handler and provide cross-cutting concerns:
 *   logging, authentication verification, localization injection, session
 *   management, and response enrichment.
 *
 * ARCHITECTURE:
 *   - ASK SDK interceptor pattern (RequestInterceptor / ResponseInterceptor)
 *   - Runs in order: LogRequest -> LocalizationInterceptor -> AuthInterceptor
 *   - Response: LogResponse -> SessionPersistenceInterceptor
 *   - Localization interceptor injects `t()` function into requestAttributes
 *   - Auth interceptor verifies Alexa userId is present
 *
 * EXAMPLE USAGE:
 *   // In index.js skill builder:
 *   const interceptors = require('./interceptors');
 *
 *   Alexa.SkillBuilders.custom()
 *     .addRequestInterceptors(
 *       interceptors.LogRequestInterceptor,
 *       interceptors.LocalizationInterceptor,
 *       interceptors.AuthVerificationInterceptor
 *     )
 *     .addResponseInterceptors(
 *       interceptors.LogResponseInterceptor,
 *       interceptors.SessionPersistenceInterceptor
 *     )
 *
 *   // In a handler, use the injected t() function:
 *   handle(handlerInput) {
 *     const t = handlerInput.attributesManager.getRequestAttributes().t;
 *     const speech = t('WELCOME');
 *   }
 * =============================================================================
 */

'use strict';

const { resolve } = require('./strings');
const config = require('./config');

// ---------------------------------------------------------------------------
// Request Interceptors
// ---------------------------------------------------------------------------

/**
 * Logs every incoming request with intent name, locale, userId (truncated),
 * session ID, and timestamp.
 */
const LogRequestInterceptor = {
  process(handlerInput) {
    const request = handlerInput.requestEnvelope.request;
    const session = handlerInput.requestEnvelope.session;
    const userId = handlerInput.requestEnvelope.context?.System?.user?.userId || 'unknown';
    const truncatedUserId = userId.length > 20 ? `${userId.substring(0, 10)}...${userId.slice(-6)}` : userId;

    console.log(JSON.stringify({
      wal: 'REQUEST_IN',
      timestamp: new Date().toISOString(),
      requestType: request.type,
      intentName: request.intent?.name || 'N/A',
      locale: request.locale,
      userId: truncatedUserId,
      sessionId: session?.sessionId || 'N/A',
      isNewSession: session?.new || false,
      dialogState: request.dialogState || 'N/A',
    }));
  },
};

/**
 * Injects localization function t(key, vars) into request attributes.
 * Detects locale from the request and binds it for all downstream handlers.
 */
const LocalizationInterceptor = {
  process(handlerInput) {
    const locale = handlerInput.requestEnvelope.request.locale || 'en-US';
    const attributes = handlerInput.attributesManager.getRequestAttributes();

    // Inject translation function
    attributes.t = function (key, vars = {}) {
      return resolve(locale, key, vars);
    };

    // Store locale for downstream use
    attributes.locale = locale;

    // Determine language for SSML lang tag
    attributes.lang = locale.startsWith('es') ? 'es' : 'en';
  },
};

/**
 * Verifies that the Alexa userId is present in the request envelope.
 * If not, logs a warning. Does NOT block the request (graceful degradation).
 */
const AuthVerificationInterceptor = {
  process(handlerInput) {
    const userId = handlerInput.requestEnvelope.context?.System?.user?.userId;
    const accessToken = handlerInput.requestEnvelope.context?.System?.user?.accessToken;
    const attributes = handlerInput.attributesManager.getRequestAttributes();

    if (!userId) {
      console.warn('[interceptor:auth] No Alexa userId found in request envelope');
      attributes.authValid = false;
      attributes.alexaUserId = null;
    } else {
      attributes.authValid = true;
      attributes.alexaUserId = userId;
    }

    // If account linking is set up, store the access token
    if (accessToken) {
      attributes.accessToken = accessToken;
      attributes.isLinked = true;
    } else {
      attributes.isLinked = false;
    }
  },
};

/**
 * Tracks timing of request processing for performance monitoring.
 */
const TimingInterceptor = {
  process(handlerInput) {
    const attributes = handlerInput.attributesManager.getRequestAttributes();
    attributes._startTime = Date.now();
  },
};

// ---------------------------------------------------------------------------
// Response Interceptors
// ---------------------------------------------------------------------------

/**
 * Logs the outgoing response including speech output length, card presence,
 * and processing duration.
 */
const LogResponseInterceptor = {
  process(handlerInput, response) {
    const request = handlerInput.requestEnvelope.request;
    const attributes = handlerInput.attributesManager.getRequestAttributes();
    const duration = attributes._startTime ? Date.now() - attributes._startTime : -1;

    console.log(JSON.stringify({
      wal: 'RESPONSE_OUT',
      timestamp: new Date().toISOString(),
      requestType: request.type,
      intentName: request.intent?.name || 'N/A',
      speechLength: response?.outputSpeech?.ssml?.length || 0,
      hasCard: !!response?.card,
      shouldEndSession: response?.shouldEndSession,
      processingMs: duration,
    }));
  },
};

/**
 * Persists session attributes at the end of each response cycle.
 * Useful for maintaining SOS pending state, last intent, etc.
 */
const SessionPersistenceInterceptor = {
  process(handlerInput) {
    const sessionAttributes = handlerInput.attributesManager.getSessionAttributes();

    // Auto-track last intent for context-aware follow-ups
    const request = handlerInput.requestEnvelope.request;
    if (request.intent?.name) {
      sessionAttributes[config.sessionKeys.LAST_INTENT] = request.intent.name;
    }

    // Persist locale
    const locale = handlerInput.requestEnvelope.request.locale;
    if (locale) {
      sessionAttributes[config.sessionKeys.LOCALE] = locale;
    }

    handlerInput.attributesManager.setSessionAttributes(sessionAttributes);
  },
};

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------
module.exports = {
  // Request interceptors (order matters)
  LogRequestInterceptor,
  TimingInterceptor,
  LocalizationInterceptor,
  AuthVerificationInterceptor,
  // Response interceptors
  LogResponseInterceptor,
  SessionPersistenceInterceptor,
};
