/**
 * ============================================================================
 * WRITE-AHEAD LOG (WAL)
 * ============================================================================
 * File:        interceptors.js
 * Module:      TheWatch Google Home Integration - Middleware / Interceptors
 * Created:     2026-03-24
 * Author:      TheWatch Platform Team
 * ----------------------------------------------------------------------------
 * PURPOSE:
 *   Express and Conversation middleware for authentication verification,
 *   structured logging (Winston), locale detection, request correlation ID
 *   injection, and timing metrics.
 *
 * MIDDLEWARE CHAIN (Express):
 *   1. correlationId  - Assigns X-Request-Id to every request
 *   2. requestLogger  - Logs method, path, status, duration
 *   3. rateLimiter    - Prevents abuse (configurable window/max)
 *   4. helmetSecurity - Standard security headers via helmet
 *
 * CONVERSATION MIDDLEWARE:
 *   1. verifyAccountLinking - Checks OAuth token exists on conv.user
 *   2. detectLocale         - Reads conv.user.locale, sets conv.scene.slots
 *   3. logConversation      - Logs intent, userId, session for auditing
 *
 * DEPENDENCIES:
 *   - winston, helmet, express-rate-limit, uuid, jsonwebtoken
 *
 * EXAMPLE USAGE:
 *   const { applyExpressMiddleware, verifyAccountLinking } = require('./interceptors');
 *   applyExpressMiddleware(expressApp);
 *
 * CHANGES:
 *   2026-03-24  Initial creation with all middleware
 * ============================================================================
 */

'use strict';

const winston = require('winston');
const helmet = require('helmet');
const rateLimit = require('express-rate-limit');
const { v4: uuidv4 } = require('uuid');
const config = require('./config');

// ---------------------------------------------------------------------------
// Winston Logger Factory
// ---------------------------------------------------------------------------
/**
 * Create a child logger with a module label.
 * @param {string} moduleName
 * @returns {winston.Logger}
 *
 * Example:
 *   const logger = createLogger('index');
 *   logger.info('Server started', { port: 3000 });
 */
function createLogger(moduleName) {
  return winston.createLogger({
    level: config.logging.level,
    format: winston.format.combine(
      winston.format.timestamp({ format: 'YYYY-MM-DD HH:mm:ss.SSS' }),
      winston.format.errors({ stack: true }),
      config.logging.format === 'json'
        ? winston.format.json()
        : winston.format.printf(({ timestamp, level, message, ...meta }) =>
            `${timestamp} [${level.toUpperCase()}] [${moduleName}] ${message} ${Object.keys(meta).length ? JSON.stringify(meta) : ''}`
          )
    ),
    defaultMeta: { module: moduleName, service: 'thewatch-google-home' },
    transports: [
      new winston.transports.Console(),
    ],
  });
}

const logger = createLogger('interceptors');

// ---------------------------------------------------------------------------
// Express Middleware
// ---------------------------------------------------------------------------

/**
 * Correlation ID middleware - assigns a unique ID to every request.
 */
function correlationId(req, res, next) {
  req.correlationId = req.headers['x-request-id'] || uuidv4();
  res.setHeader('X-Request-Id', req.correlationId);
  next();
}

/**
 * Request logging middleware - logs method, path, status code, and duration.
 */
function requestLogger(req, res, next) {
  const start = Date.now();
  res.on('finish', () => {
    const duration = Date.now() - start;
    logger.info('HTTP Request', {
      method: req.method,
      path: req.path,
      status: res.statusCode,
      durationMs: duration,
      correlationId: req.correlationId,
      userAgent: req.headers['user-agent'],
    });
  });
  next();
}

/**
 * Rate limiter - configurable via config.rateLimit.
 */
const rateLimiter = rateLimit({
  windowMs: config.rateLimit.windowMs,
  max: config.rateLimit.max,
  standardHeaders: true,
  legacyHeaders: false,
  message: { error: 'Too many requests. Please try again later.' },
  keyGenerator: (req) => {
    // Use conversation session ID if available, otherwise IP
    try {
      const body = req.body;
      return body?.session?.id || req.ip;
    } catch {
      return req.ip;
    }
  },
});

/**
 * Apply all Express middleware in the correct order.
 * @param {import('express').Application} app
 */
function applyExpressMiddleware(app) {
  app.use(helmet());
  app.use(correlationId);
  app.use(requestLogger);
  app.use(rateLimiter);

  logger.info('Express middleware applied', {
    rateLimitWindowMs: config.rateLimit.windowMs,
    rateLimitMax: config.rateLimit.max,
  });
}

// ---------------------------------------------------------------------------
// Conversation-level Middleware (for @assistant/conversation handlers)
// ---------------------------------------------------------------------------

/**
 * Verify that the user has completed account linking.
 * If no access token is present, prompt them to link.
 * @param {import('@assistant/conversation').ConversationV3} conv
 * @returns {boolean} true if account is linked
 */
function verifyAccountLinking(conv) {
  const token = conv.user?.params?.bearerToken
    || conv.headers?.authorization?.replace('Bearer ', '')
    || conv.request?.user?.accessToken;

  if (!token) {
    logger.warn('Account not linked', {
      sessionId: conv.session?.id,
    });
    return false;
  }

  // Store token for downstream API calls
  conv._authToken = token;
  return true;
}

/**
 * Extract the user's locale from the conversation.
 * @param {import('@assistant/conversation').ConversationV3} conv
 * @returns {string} BCP 47 locale string
 */
function detectLocale(conv) {
  const locale = conv.user?.locale || 'en-US';
  conv._locale = locale;
  logger.debug('Locale detected', { locale, sessionId: conv.session?.id });
  return locale;
}

/**
 * Extract a stable user ID from the conversation for API calls.
 * @param {import('@assistant/conversation').ConversationV3} conv
 * @returns {string|null}
 */
function extractUserId(conv) {
  // Prefer account-linked user ID, then session-based
  const userId = conv.user?.params?.userId
    || conv.user?.params?.accountUserId
    || conv.session?.id;
  conv._userId = userId;
  return userId;
}

/**
 * Log conversation-level data for auditing.
 * @param {import('@assistant/conversation').ConversationV3} conv
 * @param {string} intentName
 */
function logConversation(conv, intentName) {
  logger.info('Conversation intent', {
    intent: intentName,
    userId: conv._userId,
    sessionId: conv.session?.id,
    locale: conv._locale,
    hasToken: !!conv._authToken,
  });
}

module.exports = {
  createLogger,
  applyExpressMiddleware,
  verifyAccountLinking,
  detectLocale,
  extractUserId,
  logConversation,
  correlationId,
  requestLogger,
  rateLimiter,
};
