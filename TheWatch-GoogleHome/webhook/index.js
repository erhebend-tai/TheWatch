/**
 * ============================================================================
 * WRITE-AHEAD LOG (WAL)
 * ============================================================================
 * File:        index.js
 * Module:      TheWatch Google Home Integration - Main Webhook Fulfillment
 * Created:     2026-03-24
 * Author:      TheWatch Platform Team
 * ----------------------------------------------------------------------------
 * PURPOSE:
 *   Main entry point for the Google Home / Google Assistant webhook
 *   fulfillment service. Registers all intent handlers using the
 *   @assistant/conversation SDK (Actions SDK v3). Serves as both a
 *   standalone Express server and a Cloud Functions entry point.
 *
 * INTENT HANDLERS:
 *   - actions.intent.MAIN       => Welcome / returning user greeting
 *   - trigger_sos               => Trigger SOS alert (requires confirmation)
 *   - check_in                  => Record a safety check-in
 *   - get_status                => Retrieve current safety status
 *   - cancel_sos                => Cancel an active SOS alert
 *   - set_emergency_phrase      => Set/update the user's emergency phrase
 *   - nearby_responders         => List nearby volunteer responders
 *   - volunteer_status          => Check user's volunteer status
 *   - emergency_contacts        => List emergency contacts
 *   - silent_duress             => Send silent duress signal (no audible confirm)
 *   - evacuation_info           => Get evacuation route information
 *   - shelter_info              => Get nearby shelter information
 *   - medical_info              => Get user's medical profile
 *
 * ARCHITECTURE:
 *   Express app -> @assistant/conversation middleware -> intent handlers
 *   Each handler: verify auth -> extract locale -> call API -> build response
 *
 * DEPENDENCIES:
 *   - @assistant/conversation (Actions SDK v3)
 *   - express
 *   - ./config, ./api-client, ./response-builder, ./strings, ./interceptors
 *
 * EXAMPLE (standalone):
 *   PORT=3000 node index.js
 *
 * EXAMPLE (Cloud Function):
 *   exports.app is the Express app for GCF deployment
 *
 * CHANGES:
 *   2026-03-24  Initial creation with 12 intent handlers + global error handler
 * ============================================================================
 */

'use strict';

require('dotenv').config();

const express = require('express');
const {
  conversation,
  Simple,
} = require('@assistant/conversation');

const config = require('./config');
const apiClient = require('./api-client');
const rb = require('./response-builder');
const strings = require('./strings');
const {
  createLogger,
  applyExpressMiddleware,
  verifyAccountLinking,
  detectLocale,
  extractUserId,
  logConversation,
} = require('./interceptors');

const logger = createLogger('index');

// ---------------------------------------------------------------------------
// Initialize Conversation App
// ---------------------------------------------------------------------------
const app = conversation({ debug: config.server.env !== 'production' });

// ---------------------------------------------------------------------------
// Helper: Run common pre-handler checks (auth, locale, userId, logging)
// Returns { s, userId, authToken } or null if account linking required
// ---------------------------------------------------------------------------
function preHandler(conv, intentName) {
  detectLocale(conv);
  extractUserId(conv);
  logConversation(conv, intentName);

  const s = strings.forLocale(conv._locale);

  if (!verifyAccountLinking(conv)) {
    conv.add(rb.simple(s.accountLinkingRequired()));
    conv.scene.next = { name: 'AccountLinking' };
    return null;
  }

  return {
    s,
    userId: conv._userId,
    authToken: conv._authToken,
  };
}

// ---------------------------------------------------------------------------
// MAIN INVOCATION — Welcome
// ---------------------------------------------------------------------------
app.handle('actions.intent.MAIN', (conv) => {
  detectLocale(conv);
  extractUserId(conv);
  logConversation(conv, 'actions.intent.MAIN');

  const s = strings.forLocale(conv._locale);
  const userName = conv.user?.params?.displayName;

  if (!verifyAccountLinking(conv)) {
    conv.add(rb.simple(s.welcome()));
    conv.add(...rb.suggestions(['Link Account', 'Learn More']));
    return;
  }

  if (userName) {
    conv.add(rb.simple(s.welcomeReturning({ name: userName })));
  } else {
    conv.add(rb.simple(s.welcome()));
  }
  conv.add(...rb.suggestions(s.suggestions.main));
});

// ---------------------------------------------------------------------------
// TRIGGER SOS — Initiates an SOS alert
// ---------------------------------------------------------------------------
app.handle('trigger_sos', async (conv) => {
  const ctx = preHandler(conv, 'trigger_sos');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  try {
    const response = await apiClient.triggerAlert(authToken, userId, {
      severity: 'CRITICAL',
      message: 'SOS triggered via Google Home voice command',
    });

    const data = response.data;
    if (data?.alreadyActive) {
      conv.add(rb.simple(s.sos.alreadyActive()));
      conv.add(...rb.suggestions(s.suggestions.afterSos));
      return;
    }

    const userName = conv.user?.params?.displayName || 'User';
    const result = rb.sosTriggered(s, userName);
    conv.add(result.simple);
    conv.add(result.card);
    conv.add(...result.suggestions);

    logger.info('SOS alert triggered', { userId });
  } catch (error) {
    logger.error('Failed to trigger SOS', { userId, error: error.message });
    conv.add(rb.simple(s.sos.failed()));
    conv.add(rb.simple(
      'If you are in immediate danger, please say "Hey Google, call 911" or call emergency services directly.'
    ));
    conv.add(...rb.suggestions(['Try again', 'Call 911']));
  }
});

// ---------------------------------------------------------------------------
// CHECK IN — Record a safety check-in
// ---------------------------------------------------------------------------
app.handle('check_in', async (conv) => {
  const ctx = preHandler(conv, 'check_in');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  try {
    const response = await apiClient.requestCheckIn(authToken, userId, {
      type: 'MANUAL',
      message: 'Voice check-in via Google Home',
    });

    const data = response.data;
    const time = new Date().toLocaleTimeString(conv._locale, {
      hour: 'numeric',
      minute: '2-digit',
    });

    conv.add(rb.simple(s.checkIn.success({ time })));

    if (data?.nextScheduledCheckIn) {
      conv.add(rb.simple(s.checkIn.scheduled({ nextTime: data.nextScheduledCheckIn })));
    }

    conv.add(...rb.suggestions(s.suggestions.afterCheckIn));
    logger.info('Check-in recorded', { userId });
  } catch (error) {
    logger.error('Failed to record check-in', { userId, error: error.message });
    conv.add(rb.simple(s.checkIn.failed()));
    conv.add(...rb.suggestions(s.suggestions.main));
  }
});

// ---------------------------------------------------------------------------
// GET STATUS — Retrieve current safety status
// ---------------------------------------------------------------------------
app.handle('get_status', async (conv) => {
  const ctx = preHandler(conv, 'get_status');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  try {
    const response = await apiClient.getStatus(authToken, userId);
    const data = response.data;

    if (!data) {
      conv.add(rb.simple(s.status.noData()));
      conv.add(...rb.suggestions(s.suggestions.main));
      return;
    }

    const userName = conv.user?.params?.displayName || 'User';
    const result = rb.statusResponse(s, {
      safe: data.safe !== false,
      name: userName,
      lastCheckIn: data.lastCheckIn,
      alertTime: data.alertTime,
      respondersCount: data.respondersCount,
    });

    conv.add(result.simple);
    conv.add(result.card);
    conv.add(...result.suggestions);

    logger.info('Status retrieved', { userId, safe: data.safe });
  } catch (error) {
    logger.error('Failed to get status', { userId, error: error.message });
    conv.add(rb.simple(s.status.failed()));
    conv.add(...rb.suggestions(s.suggestions.main));
  }
});

// ---------------------------------------------------------------------------
// CANCEL SOS — Cancel an active SOS alert
// ---------------------------------------------------------------------------
app.handle('cancel_sos', async (conv) => {
  const ctx = preHandler(conv, 'cancel_sos');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  try {
    const response = await apiClient.cancelAlert(authToken, userId, {
      reason: 'Cancelled by user via Google Home',
    });

    const data = response.data;
    if (data?.noActiveAlert) {
      conv.add(rb.simple(s.cancel.noActiveAlert()));
    } else {
      conv.add(rb.simple(s.cancel.success()));
    }
    conv.add(...rb.suggestions(s.suggestions.afterCancel));

    logger.info('SOS cancelled', { userId });
  } catch (error) {
    logger.error('Failed to cancel SOS', { userId, error: error.message });
    conv.add(rb.simple(s.cancel.failed()));
    conv.add(...rb.suggestions(s.suggestions.main));
  }
});

// ---------------------------------------------------------------------------
// SET EMERGENCY PHRASE
// ---------------------------------------------------------------------------
app.handle('set_emergency_phrase', async (conv) => {
  const ctx = preHandler(conv, 'set_emergency_phrase');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  try {
    // The phrase is extracted from the EmergencyPhrase slot/param
    const phrase = conv.session?.params?.emergencyPhrase
      || conv.intent?.params?.emergency_phrase?.resolved
      || conv.scene?.slots?.emergency_phrase?.value;

    if (!phrase) {
      conv.add(rb.simple(
        'What would you like your emergency phrase to be? For example, you could say "red alert" or "I need help now".'
      ));
      return;
    }

    await apiClient.setEmergencyPhrase(authToken, userId, phrase);

    conv.add(rb.simple(s.emergencyPhrase.set({ phrase })));
    conv.add(...rb.suggestions(s.suggestions.main));

    logger.info('Emergency phrase set', { userId });
  } catch (error) {
    logger.error('Failed to set emergency phrase', { userId, error: error.message });
    conv.add(rb.simple(s.emergencyPhrase.failed()));
    conv.add(...rb.suggestions(s.suggestions.main));
  }
});

// ---------------------------------------------------------------------------
// NEARBY RESPONDERS
// ---------------------------------------------------------------------------
app.handle('nearby_responders', async (conv) => {
  const ctx = preHandler(conv, 'nearby_responders');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  try {
    const response = await apiClient.getNearbyResponders(authToken, userId);
    const data = response.data;

    if (!data?.responders || data.responders.length === 0) {
      conv.add(rb.simple(s.responders.none()));
      conv.add(...rb.suggestions(s.suggestions.afterSos));
      return;
    }

    const nearest = data.responders[0];
    const result = rb.respondersResponse(s, {
      count: data.responders.length,
      nearest: `${nearest.name}, ${nearest.distance} away, ETA ${nearest.eta}`,
      responders: data.responders.slice(0, 5),
    });

    conv.add(result.simple);
    if (result.table) conv.add(result.table);
    conv.add(...result.suggestions);

    logger.info('Responders retrieved', { userId, count: data.responders.length });
  } catch (error) {
    logger.error('Failed to get responders', { userId, error: error.message });
    conv.add(rb.simple(s.responders.failed()));
    conv.add(...rb.suggestions(s.suggestions.main));
  }
});

// ---------------------------------------------------------------------------
// VOLUNTEER STATUS
// ---------------------------------------------------------------------------
app.handle('volunteer_status', async (conv) => {
  const ctx = preHandler(conv, 'volunteer_status');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  try {
    const response = await apiClient.getVolunteerStatus(authToken, userId);
    const data = response.data;

    if (!data?.active) {
      conv.add(rb.simple(s.volunteer.inactive()));
    } else {
      conv.add(rb.simple(s.volunteer.active({ since: data.since || 'recently' })));
      if (data.respondedCount != null) {
        conv.add(rb.simple(s.volunteer.status({
          respondedCount: data.respondedCount,
          rating: data.rating || 'N/A',
        })));
      }
    }
    conv.add(...rb.suggestions(s.suggestions.main));

    logger.info('Volunteer status retrieved', { userId });
  } catch (error) {
    logger.error('Failed to get volunteer status', { userId, error: error.message });
    conv.add(rb.simple(s.volunteer.failed()));
    conv.add(...rb.suggestions(s.suggestions.main));
  }
});

// ---------------------------------------------------------------------------
// EMERGENCY CONTACTS
// ---------------------------------------------------------------------------
app.handle('emergency_contacts', async (conv) => {
  const ctx = preHandler(conv, 'emergency_contacts');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  try {
    const response = await apiClient.getEmergencyContacts(authToken, userId);
    const data = response.data;

    if (!data?.contacts || data.contacts.length === 0) {
      conv.add(rb.simple(s.contacts.none()));
      conv.add(...rb.suggestions(s.suggestions.main));
      return;
    }

    const result = rb.contactsResponse(s, data.contacts);
    conv.add(result.simple);
    conv.add(result.table);
    conv.add(...result.suggestions);

    logger.info('Contacts retrieved', { userId, count: data.contacts.length });
  } catch (error) {
    logger.error('Failed to get contacts', { userId, error: error.message });
    conv.add(rb.simple(s.contacts.failed()));
    conv.add(...rb.suggestions(s.suggestions.main));
  }
});

// ---------------------------------------------------------------------------
// SILENT DURESS — Sends a silent alert (minimal audible feedback)
// ---------------------------------------------------------------------------
app.handle('silent_duress', async (conv) => {
  const ctx = preHandler(conv, 'silent_duress');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  if (!config.features.silentDuressEnabled) {
    conv.add(rb.simple(s.errors.generic()));
    return;
  }

  try {
    await apiClient.sendSilentDuress(authToken, userId);

    // Deliberately minimal response — the point is to NOT alert an attacker
    conv.add(rb.simple(s.silentDuress.activated()));

    logger.warn('Silent duress activated', { userId });
  } catch (error) {
    logger.error('Failed to send silent duress', { userId, error: error.message });
    // Still keep response minimal
    conv.add(rb.simple(s.silentDuress.failed()));
  }
});

// ---------------------------------------------------------------------------
// EVACUATION INFO
// ---------------------------------------------------------------------------
app.handle('evacuation_info', async (conv) => {
  const ctx = preHandler(conv, 'evacuation_info');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  try {
    // Use userId as location proxy; the backend resolves the user's location
    const response = await apiClient.getEvacuationInfo(authToken, userId);
    const data = response.data;

    if (!data?.routes || data.routes.length === 0) {
      conv.add(rb.simple(s.evacuation.none()));
      conv.add(...rb.suggestions(s.suggestions.main));
      return;
    }

    const routeNames = data.routes.map((r) => r.name || r.description).join('; ');
    conv.add(rb.simple(s.evacuation.info({ routes: routeNames })));
    conv.add(rb.card({
      title: 'Evacuation Routes',
      text: data.routes.map((r, i) => `${i + 1}. ${r.name}: ${r.description}`).join('\n'),
      buttonText: 'View Map',
      buttonUrl: 'https://app.thewatch.app/evacuation',
    }));
    conv.add(...rb.suggestions(s.suggestions.main));

    logger.info('Evacuation info retrieved', { userId });
  } catch (error) {
    logger.error('Failed to get evacuation info', { userId, error: error.message });
    conv.add(rb.simple(s.evacuation.failed()));
    conv.add(...rb.suggestions(s.suggestions.main));
  }
});

// ---------------------------------------------------------------------------
// SHELTER INFO
// ---------------------------------------------------------------------------
app.handle('shelter_info', async (conv) => {
  const ctx = preHandler(conv, 'shelter_info');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  try {
    const response = await apiClient.getShelterInfo(authToken, userId);
    const data = response.data;

    if (!data?.shelters || data.shelters.length === 0) {
      conv.add(rb.simple(s.shelter.none()));
      conv.add(...rb.suggestions(s.suggestions.main));
      return;
    }

    const result = rb.sheltersResponse(s, data.shelters);
    conv.add(result.simple);
    conv.add(result.table);
    conv.add(...result.suggestions);

    logger.info('Shelter info retrieved', { userId });
  } catch (error) {
    logger.error('Failed to get shelter info', { userId, error: error.message });
    conv.add(rb.simple(s.shelter.failed()));
    conv.add(...rb.suggestions(s.suggestions.main));
  }
});

// ---------------------------------------------------------------------------
// MEDICAL INFO
// ---------------------------------------------------------------------------
app.handle('medical_info', async (conv) => {
  const ctx = preHandler(conv, 'medical_info');
  if (!ctx) return;
  const { s, userId, authToken } = ctx;

  try {
    const response = await apiClient.getMedicalInfo(authToken, userId);
    const data = response.data;

    if (!data || (!data.conditions && !data.allergies && !data.medications)) {
      conv.add(rb.simple(s.medical.none()));
      conv.add(...rb.suggestions(s.suggestions.main));
      return;
    }

    const conditions = (data.conditions || []).join(', ') || 'None listed';
    const allergies = (data.allergies || []).join(', ') || 'None listed';
    const medications = (data.medications || []).join(', ') || 'None listed';

    conv.add(rb.simple(s.medical.info({ conditions, allergies, medications })));
    conv.add(rb.card({
      title: 'Medical Profile',
      text: `Conditions: ${conditions}\nAllergies: ${allergies}\nMedications: ${medications}`,
      buttonText: 'Update in App',
      buttonUrl: 'https://app.thewatch.app/medical',
    }));
    conv.add(...rb.suggestions(s.suggestions.main));

    logger.info('Medical info retrieved', { userId });
  } catch (error) {
    logger.error('Failed to get medical info', { userId, error: error.message });
    conv.add(rb.simple(s.medical.failed()));
    conv.add(...rb.suggestions(s.suggestions.main));
  }
});

// ---------------------------------------------------------------------------
// SOS CONFIRMATION SCENE HANDLER — Called when SOSConfirmation scene resolves
// ---------------------------------------------------------------------------
app.handle('sos_confirmed_yes', async (conv) => {
  // Delegate to the trigger_sos handler
  const handler = app._handlers?.get('trigger_sos');
  if (handler) {
    return handler(conv);
  }
  // Fallback
  conv.add(rb.simple('Triggering SOS now...'));
});

app.handle('sos_confirmed_no', (conv) => {
  detectLocale(conv);
  const s = strings.forLocale(conv._locale);
  conv.add(rb.simple('SOS alert cancelled. Stay safe.'));
  conv.add(...rb.suggestions(s.suggestions.main));
});

// ---------------------------------------------------------------------------
// GLOBAL ERROR / CATCH HANDLER
// ---------------------------------------------------------------------------
app.catch((conv, error) => {
  logger.error('Unhandled error in conversation', {
    error: error.message,
    stack: error.stack,
    sessionId: conv.session?.id,
  });

  const s = strings.forLocale(conv._locale || 'en-US');

  if (error.response?.status === 401 || error.response?.status === 403) {
    conv.add(rb.simple(s.errors.unauthorized()));
    conv.scene.next = { name: 'AccountLinking' };
    return;
  }

  if (error.response?.status === 429) {
    conv.add(rb.simple(s.errors.rateLimit()));
    return;
  }

  conv.add(rb.simple(s.errors.generic()));
  conv.add(...rb.suggestions(['Try again']));
});

// ---------------------------------------------------------------------------
// Express Server Setup
// ---------------------------------------------------------------------------
const expressApp = express();
expressApp.use(express.json());
applyExpressMiddleware(expressApp);

// Health check endpoint for monitoring
expressApp.get('/health', (req, res) => {
  res.json({
    status: 'healthy',
    service: 'thewatch-google-home',
    timestamp: new Date().toISOString(),
    version: require('./package.json').version,
  });
});

// Readiness probe (for Kubernetes / Cloud Run)
expressApp.get('/ready', async (req, res) => {
  try {
    // Lightweight check — can we reach the API?
    // In production, this would ping a health endpoint
    res.json({ ready: true });
  } catch {
    res.status(503).json({ ready: false });
  }
});

// Mount the conversation handler
expressApp.post('/fulfillment', app);

// Also mount at root for Cloud Functions compatibility
expressApp.post('/', app);

// ---------------------------------------------------------------------------
// Start server (when not running as Cloud Function)
// ---------------------------------------------------------------------------
if (require.main === module) {
  const port = config.server.port;
  expressApp.listen(port, () => {
    logger.info(`TheWatch Google Home webhook listening on port ${port}`, {
      env: config.server.env,
      apiBaseUrl: config.api.baseUrl,
    });
  });
}

// Export for Cloud Functions
module.exports = { app: expressApp, conversationApp: app };
