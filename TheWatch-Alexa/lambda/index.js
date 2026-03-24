/**
 * =============================================================================
 * WRITE-AHEAD LOG (WAL) - index.js
 * =============================================================================
 * PURPOSE:
 *   Main AWS Lambda handler for TheWatch Alexa Skill. Routes Alexa voice
 *   requests to the appropriate intent handler, which in turn calls TheWatch
 *   Dashboard API via the api-client adapter. This is the entry point for
 *   all voice interactions with TheWatch on Amazon Alexa devices.
 *
 * ARCHITECTURE:
 *   - ASK SDK v2 (ask-sdk-core) skill builder pattern
 *   - Hexagonal: Lambda is an inbound adapter; api-client is outbound adapter
 *   - Interceptors handle logging, auth, and localization (cross-cutting)
 *   - Session attributes maintain state (SOS confirmation pending, etc.)
 *   - SSML responses built via speech-builder module
 *   - Localized strings via strings.js + interceptor injection
 *
 * INTENT HANDLERS:
 *   LaunchRequest               - Welcome, help overview
 *   TriggerSOSIntent            - "I need help" -> POST /api/iot/alert
 *   TriggerSOSConfirmIntent     - "Yes" confirmation for SOS
 *   CheckInIntent               - "I'm okay" -> POST /api/iot/checkin
 *   StatusIntent                - "What's my status" -> GET /api/iot/status
 *   CancelSOSIntent             - "Cancel alert" -> POST /api/iot/cancel
 *   SetEmergencyPhraseIntent    - "Set my emergency phrase to..."
 *   GetNearbyRespondersIntent   - "Who is nearby"
 *   VolunteerStatusIntent       - "My volunteer status"
 *   EmergencyContactsIntent     - "My emergency contacts"
 *   SilentDuressIntent          - Silent panic (innocuous response, real alert)
 *   AMAZON.HelpIntent           - Help menu
 *   AMAZON.CancelIntent         - Exit
 *   AMAZON.StopIntent           - Exit
 *   AMAZON.FallbackIntent       - Unrecognized
 *   SessionEndedRequest         - Cleanup
 *
 * EXAMPLE USAGE:
 *   // Deploy as Lambda function, ASK CLI sets this as the handler:
 *   exports.handler = skill.lambda();
 *
 *   // Voice: "Alexa, tell The Watch I need help"
 *   // -> TriggerSOSIntentHandler -> sosConfirmationPrompt (if confirm required)
 *   // -> User: "Yes" -> TriggerSOSConfirmIntentHandler -> POST /api/iot/alert
 *   // -> SSML: "Emergency alert activated! Your contacts are being notified..."
 *
 *   // Voice: "Alexa, ask The Watch to check in"
 *   // -> CheckInIntentHandler -> POST /api/iot/checkin
 *   // -> SSML: "Check-in received! Your safety contacts have been notified..."
 * =============================================================================
 */

'use strict';

const Alexa = require('ask-sdk-core');
const apiClient = require('./api-client');
const speech = require('./speech-builder');
const config = require('./config');
const interceptors = require('./interceptors');

// ---------------------------------------------------------------------------
// Helper: get Alexa user ID from handler input
// ---------------------------------------------------------------------------
function getAlexaUserId(handlerInput) {
  return handlerInput.requestEnvelope.context?.System?.user?.userId || null;
}

// ---------------------------------------------------------------------------
// Helper: get localized string
// ---------------------------------------------------------------------------
function t(handlerInput, key, vars) {
  const tFn = handlerInput.attributesManager.getRequestAttributes().t;
  return tFn ? tFn(key, vars) : key;
}

// ---------------------------------------------------------------------------
// Helper: build error response from API result
// ---------------------------------------------------------------------------
function buildErrorResponse(handlerInput, result) {
  const errorSpeech = speech.error(result.errorType);
  return handlerInput.responseBuilder
    .speak(errorSpeech)
    .reprompt(t(handlerInput, 'REPROMPT'))
    .getResponse();
}

// =============================================================================
// INTENT HANDLERS
// =============================================================================

/**
 * LaunchRequest - User opens the skill: "Alexa, open The Watch"
 */
const LaunchRequestHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'LaunchRequest';
  },
  handle(handlerInput) {
    console.log('[WAL] LaunchRequestHandler invoked');
    const welcomeSpeech = speech.welcome();
    return handlerInput.responseBuilder
      .speak(welcomeSpeech)
      .reprompt(t(handlerInput, 'REPROMPT'))
      .withSimpleCard('The Watch', t(handlerInput, 'WELCOME'))
      .getResponse();
  },
};

/**
 * TriggerSOSIntent - "I need help", "emergency", "SOS"
 * If confirmation is required (config), prompts first. Otherwise triggers immediately.
 */
const TriggerSOSIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && Alexa.getIntentName(handlerInput.requestEnvelope) === 'TriggerSOSIntent';
  },
  async handle(handlerInput) {
    console.log('[WAL] TriggerSOSIntentHandler invoked');
    const alexaUserId = getAlexaUserId(handlerInput);

    if (config.sos.confirmationRequired) {
      // Set session flag and prompt for confirmation
      const sessionAttributes = handlerInput.attributesManager.getSessionAttributes();
      sessionAttributes[config.sessionKeys.SOS_PENDING] = true;
      handlerInput.attributesManager.setSessionAttributes(sessionAttributes);

      const confirmSpeech = speech.sosConfirmationPrompt();
      return handlerInput.responseBuilder
        .speak(confirmSpeech)
        .reprompt(t(handlerInput, 'SOS_CONFIRM_PROMPT'))
        .withSimpleCard('The Watch - Confirm SOS', 'Say "yes" to confirm your emergency alert.')
        .getResponse();
    }

    // No confirmation required - trigger immediately
    const result = await apiClient.triggerSOS({
      alexaUserId,
      source: config.iotSource,
    });

    if (!result.success) {
      return buildErrorResponse(handlerInput, result);
    }

    const sosSpeech = speech.sosConfirmation();
    return handlerInput.responseBuilder
      .speak(sosSpeech)
      .withSimpleCard('The Watch - SOS ACTIVE', 'Emergency alert has been activated. Help is on the way.')
      .withShouldEndSession(false)
      .getResponse();
  },
};

/**
 * TriggerSOSConfirmIntent - User says "Yes" after SOS confirmation prompt.
 * Also handles AMAZON.YesIntent when SOS is pending.
 */
const TriggerSOSConfirmIntentHandler = {
  canHandle(handlerInput) {
    if (Alexa.getRequestType(handlerInput.requestEnvelope) !== 'IntentRequest') return false;
    const intentName = Alexa.getIntentName(handlerInput.requestEnvelope);
    if (intentName !== 'AMAZON.YesIntent' && intentName !== 'TriggerSOSConfirmIntent') return false;
    const sessionAttributes = handlerInput.attributesManager.getSessionAttributes();
    return sessionAttributes[config.sessionKeys.SOS_PENDING] === true;
  },
  async handle(handlerInput) {
    console.log('[WAL] TriggerSOSConfirmIntentHandler invoked - SOS CONFIRMED');
    const alexaUserId = getAlexaUserId(handlerInput);

    // Clear pending flag
    const sessionAttributes = handlerInput.attributesManager.getSessionAttributes();
    sessionAttributes[config.sessionKeys.SOS_PENDING] = false;
    handlerInput.attributesManager.setSessionAttributes(sessionAttributes);

    const result = await apiClient.triggerSOS({
      alexaUserId,
      source: config.iotSource,
    });

    if (!result.success) {
      return buildErrorResponse(handlerInput, result);
    }

    const sosSpeech = speech.sosConfirmation();
    return handlerInput.responseBuilder
      .speak(sosSpeech)
      .withSimpleCard('The Watch - SOS ACTIVE', 'Emergency alert has been activated. Help is on the way.')
      .withShouldEndSession(false)
      .getResponse();
  },
};

/**
 * SOSDeniedHandler - User says "No" after SOS confirmation prompt.
 */
const SOSDeniedHandler = {
  canHandle(handlerInput) {
    if (Alexa.getRequestType(handlerInput.requestEnvelope) !== 'IntentRequest') return false;
    const intentName = Alexa.getIntentName(handlerInput.requestEnvelope);
    if (intentName !== 'AMAZON.NoIntent') return false;
    const sessionAttributes = handlerInput.attributesManager.getSessionAttributes();
    return sessionAttributes[config.sessionKeys.SOS_PENDING] === true;
  },
  handle(handlerInput) {
    console.log('[WAL] SOSDeniedHandler invoked - SOS cancelled by user before trigger');
    const sessionAttributes = handlerInput.attributesManager.getSessionAttributes();
    sessionAttributes[config.sessionKeys.SOS_PENDING] = false;
    handlerInput.attributesManager.setSessionAttributes(sessionAttributes);

    return handlerInput.responseBuilder
      .speak(t(handlerInput, 'SOS_CANCELLED'))
      .reprompt(t(handlerInput, 'REPROMPT'))
      .getResponse();
  },
};

/**
 * CheckInIntent - "I'm okay", "check in", "I'm safe"
 */
const CheckInIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && Alexa.getIntentName(handlerInput.requestEnvelope) === 'CheckInIntent';
  },
  async handle(handlerInput) {
    console.log('[WAL] CheckInIntentHandler invoked');
    const alexaUserId = getAlexaUserId(handlerInput);

    const result = await apiClient.checkIn({
      alexaUserId,
      status: 'OK',
    });

    if (!result.success) {
      return buildErrorResponse(handlerInput, result);
    }

    const checkinSpeech = speech.checkInConfirmation();
    return handlerInput.responseBuilder
      .speak(checkinSpeech)
      .withSimpleCard('The Watch - Check-In', 'You have checked in successfully. Your contacts have been notified.')
      .reprompt(t(handlerInput, 'REPROMPT'))
      .getResponse();
  },
};

/**
 * StatusIntent - "What's my status", "safety report", "any alerts"
 */
const StatusIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && Alexa.getIntentName(handlerInput.requestEnvelope) === 'StatusIntent';
  },
  async handle(handlerInput) {
    console.log('[WAL] StatusIntentHandler invoked');
    const alexaUserId = getAlexaUserId(handlerInput);

    const result = await apiClient.getStatus(alexaUserId);

    if (!result.success) {
      return buildErrorResponse(handlerInput, result);
    }

    const data = result.data || {};
    const statusSpeech = speech.statusReport({
      activeAlerts: data.activeAlerts || 0,
      lastCheckIn: data.lastCheckIn || 'unknown',
      nearbyResponders: data.nearbyResponders || 0,
      pendingCheckIns: data.pendingCheckIns || 0,
    });

    return handlerInput.responseBuilder
      .speak(statusSpeech)
      .withSimpleCard('The Watch - Status', `Active Alerts: ${data.activeAlerts || 0}\nLast Check-In: ${data.lastCheckIn || 'N/A'}\nNearby Responders: ${data.nearbyResponders || 0}`)
      .reprompt(t(handlerInput, 'REPROMPT'))
      .getResponse();
  },
};

/**
 * CancelSOSIntent - "Cancel alert", "cancel my emergency", "false alarm"
 */
const CancelSOSIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && Alexa.getIntentName(handlerInput.requestEnvelope) === 'CancelSOSIntent';
  },
  async handle(handlerInput) {
    console.log('[WAL] CancelSOSIntentHandler invoked');
    const alexaUserId = getAlexaUserId(handlerInput);

    const result = await apiClient.cancelSOS({
      alexaUserId,
      reason: 'UserCancelled',
    });

    if (!result.success) {
      return buildErrorResponse(handlerInput, result);
    }

    const cancelSpeech = speech.sosCancelled();
    return handlerInput.responseBuilder
      .speak(cancelSpeech)
      .withSimpleCard('The Watch - Alert Cancelled', 'Your emergency alert has been cancelled.')
      .reprompt(t(handlerInput, 'REPROMPT'))
      .getResponse();
  },
};

/**
 * SetEmergencyPhraseIntent - "Set my emergency phrase to {phrase}"
 * Uses a dialog model slot {emergencyPhrase} to capture the phrase.
 */
const SetEmergencyPhraseIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && Alexa.getIntentName(handlerInput.requestEnvelope) === 'SetEmergencyPhraseIntent';
  },
  async handle(handlerInput) {
    console.log('[WAL] SetEmergencyPhraseIntentHandler invoked');
    const alexaUserId = getAlexaUserId(handlerInput);
    const slots = handlerInput.requestEnvelope.request.intent.slots || {};
    const phrase = slots.emergencyPhrase?.value;

    if (!phrase) {
      return handlerInput.responseBuilder
        .speak(t(handlerInput, 'PHRASE_PROMPT'))
        .reprompt(t(handlerInput, 'PHRASE_PROMPT'))
        .addElicitSlotDirective('emergencyPhrase')
        .getResponse();
    }

    const result = await apiClient.setEmergencyPhrase({
      alexaUserId,
      phrase,
    });

    if (!result.success) {
      return buildErrorResponse(handlerInput, result);
    }

    const phraseSpeech = speech.emergencyPhraseSet(phrase);
    return handlerInput.responseBuilder
      .speak(phraseSpeech)
      .withSimpleCard('The Watch - Emergency Phrase', 'Your emergency phrase has been updated securely.')
      .reprompt(t(handlerInput, 'REPROMPT'))
      .getResponse();
  },
};

/**
 * GetNearbyRespondersIntent - "Who is nearby", "nearby volunteers", "responders near me"
 */
const GetNearbyRespondersIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && Alexa.getIntentName(handlerInput.requestEnvelope) === 'GetNearbyRespondersIntent';
  },
  async handle(handlerInput) {
    console.log('[WAL] GetNearbyRespondersIntentHandler invoked');
    const alexaUserId = getAlexaUserId(handlerInput);

    const result = await apiClient.getNearbyResponders(alexaUserId);

    if (!result.success) {
      return buildErrorResponse(handlerInput, result);
    }

    const data = result.data || {};
    const count = data.count || 0;
    const names = (data.responders || []).map(r => r.displayName || r.name || 'Anonymous');

    const responderSpeech = speech.nearbyRespondersReport(count, names);
    return handlerInput.responseBuilder
      .speak(responderSpeech)
      .withSimpleCard('The Watch - Nearby Responders', `${count} volunteer responder(s) nearby.`)
      .reprompt(t(handlerInput, 'REPROMPT'))
      .getResponse();
  },
};

/**
 * VolunteerStatusIntent - "My volunteer status", "am I on duty"
 */
const VolunteerStatusIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && Alexa.getIntentName(handlerInput.requestEnvelope) === 'VolunteerStatusIntent';
  },
  async handle(handlerInput) {
    console.log('[WAL] VolunteerStatusIntentHandler invoked');
    const alexaUserId = getAlexaUserId(handlerInput);

    const result = await apiClient.getVolunteerStatus(alexaUserId);

    if (!result.success) {
      return buildErrorResponse(handlerInput, result);
    }

    const data = result.data || {};
    const volunteerSpeech = speech.volunteerStatusReport({
      isActive: data.isActive || false,
      shiftsThisWeek: data.shiftsThisWeek || 0,
      totalResponses: data.totalResponses || 0,
    });

    return handlerInput.responseBuilder
      .speak(volunteerSpeech)
      .withSimpleCard('The Watch - Volunteer Status', `Status: ${data.isActive ? 'Active' : 'Inactive'}`)
      .reprompt(t(handlerInput, 'REPROMPT'))
      .getResponse();
  },
};

/**
 * EmergencyContactsIntent - "My contacts", "who are my emergency contacts"
 */
const EmergencyContactsIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && Alexa.getIntentName(handlerInput.requestEnvelope) === 'EmergencyContactsIntent';
  },
  async handle(handlerInput) {
    console.log('[WAL] EmergencyContactsIntentHandler invoked');
    const alexaUserId = getAlexaUserId(handlerInput);

    const result = await apiClient.getEmergencyContacts(alexaUserId);

    if (!result.success) {
      return buildErrorResponse(handlerInput, result);
    }

    const contacts = result.data?.contacts || [];
    const contactsSpeech = speech.emergencyContactsReport(contacts);

    return handlerInput.responseBuilder
      .speak(contactsSpeech)
      .withSimpleCard('The Watch - Emergency Contacts', contacts.map(c => `${c.name} (${c.relationship || 'contact'})`).join('\n') || 'No contacts configured.')
      .reprompt(t(handlerInput, 'REPROMPT'))
      .getResponse();
  },
};

/**
 * SilentDuressIntent - Covert duress signal.
 * Triggers a real SOS alert with scope="SilentDuress" but gives an innocuous
 * audible response so that a threatening party nearby does not realize an alert
 * was sent. The user activates this by saying a pre-configured innocuous phrase
 * or "update my settings" (mapped in interaction model).
 *
 * CRITICAL SAFETY FEATURE: The response MUST sound completely normal.
 */
const SilentDuressIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && Alexa.getIntentName(handlerInput.requestEnvelope) === 'SilentDuressIntent';
  },
  async handle(handlerInput) {
    console.log('[WAL] SilentDuressIntentHandler invoked - SILENT DURESS ACTIVATED');
    const alexaUserId = getAlexaUserId(handlerInput);

    // Fire and forget pattern - send alert but respond innocuously regardless
    // We do NOT await in a way that would delay the innocuous response on failure
    try {
      await apiClient.triggerSOS({
        alexaUserId,
        source: config.iotSource,
        scope: 'SilentDuress',
      });
    } catch (err) {
      // Log but do not expose failure to user - maintain cover
      console.error('[WAL] Silent duress API call failed:', err.message);
    }

    // Innocuous response - sounds like a settings update
    const duressSpeech = speech.silentDuressAck();
    return handlerInput.responseBuilder
      .speak(duressSpeech)
      .withShouldEndSession(true)
      .getResponse();
  },
};

// ---------------------------------------------------------------------------
// Built-in Intent Handlers
// ---------------------------------------------------------------------------

const HelpIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && Alexa.getIntentName(handlerInput.requestEnvelope) === 'AMAZON.HelpIntent';
  },
  handle(handlerInput) {
    console.log('[WAL] HelpIntentHandler invoked');
    const helpSpeech = speech.help();
    return handlerInput.responseBuilder
      .speak(helpSpeech)
      .reprompt(t(handlerInput, 'REPROMPT'))
      .withSimpleCard('The Watch - Help', t(handlerInput, 'HELP'))
      .getResponse();
  },
};

const CancelAndStopIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && (Alexa.getIntentName(handlerInput.requestEnvelope) === 'AMAZON.CancelIntent'
        || Alexa.getIntentName(handlerInput.requestEnvelope) === 'AMAZON.StopIntent');
  },
  handle(handlerInput) {
    console.log('[WAL] CancelAndStopIntentHandler invoked');
    const goodbyeSpeech = speech.goodbye();
    return handlerInput.responseBuilder
      .speak(goodbyeSpeech)
      .withShouldEndSession(true)
      .getResponse();
  },
};

const FallbackIntentHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'IntentRequest'
      && Alexa.getIntentName(handlerInput.requestEnvelope) === 'AMAZON.FallbackIntent';
  },
  handle(handlerInput) {
    console.log('[WAL] FallbackIntentHandler invoked');
    return handlerInput.responseBuilder
      .speak(t(handlerInput, 'FALLBACK'))
      .reprompt(t(handlerInput, 'REPROMPT'))
      .getResponse();
  },
};

const SessionEndedRequestHandler = {
  canHandle(handlerInput) {
    return Alexa.getRequestType(handlerInput.requestEnvelope) === 'SessionEndedRequest';
  },
  handle(handlerInput) {
    const reason = handlerInput.requestEnvelope.request.reason;
    const error = handlerInput.requestEnvelope.request.error;
    console.log(JSON.stringify({
      wal: 'SESSION_ENDED',
      reason,
      errorType: error?.type,
      errorMessage: error?.message,
    }));
    return handlerInput.responseBuilder.getResponse();
  },
};

// ---------------------------------------------------------------------------
// Error Handler (catch-all)
// ---------------------------------------------------------------------------
const ErrorHandler = {
  canHandle() {
    return true;
  },
  handle(handlerInput, error) {
    console.error(JSON.stringify({
      wal: 'UNHANDLED_ERROR',
      message: error.message,
      stack: error.stack,
      intentName: handlerInput.requestEnvelope.request?.intent?.name || 'N/A',
    }));

    const errorSpeech = speech.error('UNKNOWN');
    return handlerInput.responseBuilder
      .speak(errorSpeech)
      .reprompt(t(handlerInput, 'REPROMPT'))
      .getResponse();
  },
};

// =============================================================================
// SKILL BUILDER
// =============================================================================

/**
 * Handler registration order matters: more specific handlers first.
 * - TriggerSOSConfirmIntentHandler checks session state, so it must precede
 *   a generic YesIntent handler.
 * - SOSDeniedHandler checks session state for pending SOS.
 * - SilentDuressIntentHandler is a custom intent, registered normally.
 */
const skill = Alexa.SkillBuilders.custom()
  .addRequestHandlers(
    LaunchRequestHandler,
    TriggerSOSConfirmIntentHandler,   // Must be before generic Yes handler
    SOSDeniedHandler,                  // Must be before generic No handler
    TriggerSOSIntentHandler,
    CheckInIntentHandler,
    StatusIntentHandler,
    CancelSOSIntentHandler,
    SetEmergencyPhraseIntentHandler,
    GetNearbyRespondersIntentHandler,
    VolunteerStatusIntentHandler,
    EmergencyContactsIntentHandler,
    SilentDuressIntentHandler,
    HelpIntentHandler,
    CancelAndStopIntentHandler,
    FallbackIntentHandler,
    SessionEndedRequestHandler,
  )
  .addRequestInterceptors(
    interceptors.TimingInterceptor,
    interceptors.LogRequestInterceptor,
    interceptors.LocalizationInterceptor,
    interceptors.AuthVerificationInterceptor,
  )
  .addResponseInterceptors(
    interceptors.LogResponseInterceptor,
    interceptors.SessionPersistenceInterceptor,
  )
  .addErrorHandlers(ErrorHandler)
  .withCustomUserAgent('TheWatch/1.0.0')
  .create();

exports.handler = skill.lambda ? skill.lambda() : async (event, context) => {
  console.log('[WAL] Lambda invoked', JSON.stringify({ requestId: context.awsRequestId }));
  return skill.invoke(event, context);
};

// Export handlers for testing
module.exports = {
  handler: exports.handler,
  LaunchRequestHandler,
  TriggerSOSIntentHandler,
  TriggerSOSConfirmIntentHandler,
  SOSDeniedHandler,
  CheckInIntentHandler,
  StatusIntentHandler,
  CancelSOSIntentHandler,
  SetEmergencyPhraseIntentHandler,
  GetNearbyRespondersIntentHandler,
  VolunteerStatusIntentHandler,
  EmergencyContactsIntentHandler,
  SilentDuressIntentHandler,
  HelpIntentHandler,
  CancelAndStopIntentHandler,
  FallbackIntentHandler,
  SessionEndedRequestHandler,
  ErrorHandler,
};
