/**
 * =============================================================================
 * WRITE-AHEAD LOG (WAL) - speech-builder.js
 * =============================================================================
 * PURPOSE:
 *   SSML speech builder for TheWatch Alexa Skill. Constructs Amazon-compliant
 *   SSML responses for SOS confirmations, status reports, error messages,
 *   check-in responses, and all other voice interactions.
 *
 * ARCHITECTURE:
 *   - Pure utility module, no side effects
 *   - Fluent builder pattern for composing complex SSML
 *   - Supports prosody, breaks, audio clips, emphasis, and say-as
 *   - Integrates with localization strings via i18n key references
 *   - Amazon Alexa SSML spec compliant (no unsupported tags)
 *
 * EXAMPLE USAGE:
 *   const speech = require('./speech-builder');
 *
 *   // Simple SOS confirmation
 *   const ssml = speech.sosConfirmation();
 *   // => '<speak><audio src="..."/><prosody rate="medium"><emphasis level="strong">...'
 *
 *   // Status report
 *   const statusSsml = speech.statusReport({ activeAlerts: 0, lastCheckIn: '2 hours ago' });
 *
 *   // Error with retry prompt
 *   const errorSsml = speech.error('SERVICE_DOWN');
 *
 *   // Fluent builder
 *   const custom = speech.builder()
 *     .addBreak('500ms')
 *     .addText('Hello')
 *     .addEmphasis('important part', 'strong')
 *     .build();
 * =============================================================================
 */

'use strict';

// ---------------------------------------------------------------------------
// SSML Builder (Fluent)
// ---------------------------------------------------------------------------
class SSMLBuilder {
  constructor() {
    this._parts = [];
  }

  addText(text) {
    this._parts.push(escapeXml(text));
    return this;
  }

  addBreak(time) {
    this._parts.push(`<break time="${time}"/>`);
    return this;
  }

  addProsody(text, { rate, pitch, volume } = {}) {
    const attrs = [];
    if (rate) attrs.push(`rate="${rate}"`);
    if (pitch) attrs.push(`pitch="${pitch}"`);
    if (volume) attrs.push(`volume="${volume}"`);
    this._parts.push(`<prosody ${attrs.join(' ')}>${escapeXml(text)}</prosody>`);
    return this;
  }

  addEmphasis(text, level = 'moderate') {
    this._parts.push(`<emphasis level="${level}">${escapeXml(text)}</emphasis>`);
    return this;
  }

  addSayAs(text, interpretAs, format) {
    const fmtAttr = format ? ` format="${format}"` : '';
    this._parts.push(`<say-as interpret-as="${interpretAs}"${fmtAttr}>${escapeXml(text)}</say-as>`);
    return this;
  }

  addAudio(src) {
    this._parts.push(`<audio src="${src}"/>`);
    return this;
  }

  addSentence(text) {
    this._parts.push(`<s>${escapeXml(text)}</s>`);
    return this;
  }

  addParagraph(text) {
    this._parts.push(`<p>${escapeXml(text)}</p>`);
    return this;
  }

  /**
   * Adds raw SSML content (no escaping).
   */
  addRaw(ssml) {
    this._parts.push(ssml);
    return this;
  }

  build() {
    return `<speak>${this._parts.join('')}</speak>`;
  }
}

// ---------------------------------------------------------------------------
// Helper: XML escape
// ---------------------------------------------------------------------------
function escapeXml(text) {
  if (!text) return '';
  return String(text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}

// ---------------------------------------------------------------------------
// Alexa sound effect library URLs
// ---------------------------------------------------------------------------
const SOUNDS = Object.freeze({
  ALERT_TONE: 'soundbank://soundlibrary/alarms/beeps_and_bloops/tone_05',
  CONFIRM_TONE: 'soundbank://soundlibrary/ui/gameshow/amzn_ui_sfx_gameshow_positive_response_01',
  ERROR_TONE: 'soundbank://soundlibrary/ui/gameshow/amzn_ui_sfx_gameshow_negative_response_01',
  URGENT_SIREN: 'soundbank://soundlibrary/alarms/car_alarms/car_alarm_04',
});

// ---------------------------------------------------------------------------
// Pre-built speech responses
// ---------------------------------------------------------------------------

function sosConfirmation() {
  return new SSMLBuilder()
    .addAudio(SOUNDS.URGENT_SIREN)
    .addBreak('300ms')
    .addProsody('Emergency alert activated!', { rate: 'medium', volume: 'x-loud' })
    .addBreak('500ms')
    .addSentence('Your emergency contacts and nearby responders are being notified now.')
    .addBreak('300ms')
    .addSentence('If this was a mistake, say "cancel alert" within the next 60 seconds.')
    .addBreak('300ms')
    .addSentence('Stay safe. Help is on the way.')
    .build();
}

function sosConfirmationPrompt() {
  return new SSMLBuilder()
    .addAudio(SOUNDS.ALERT_TONE)
    .addBreak('200ms')
    .addProsody('You are about to trigger an emergency alert.', { rate: 'slow', volume: 'loud' })
    .addBreak('500ms')
    .addEmphasis('Are you sure you need help?', 'strong')
    .addBreak('300ms')
    .addSentence('Say "yes" to confirm, or "no" to cancel.')
    .build();
}

function sosCancelled() {
  return new SSMLBuilder()
    .addAudio(SOUNDS.CONFIRM_TONE)
    .addBreak('200ms')
    .addSentence('Your emergency alert has been cancelled.')
    .addBreak('200ms')
    .addSentence('Your contacts have been notified that you are safe.')
    .build();
}

function silentDuressAck() {
  // Intentionally brief and innocuous - silent duress should not alert anyone nearby
  return new SSMLBuilder()
    .addSentence('Okay, your settings have been updated.')
    .build();
}

function checkInConfirmation() {
  return new SSMLBuilder()
    .addAudio(SOUNDS.CONFIRM_TONE)
    .addBreak('200ms')
    .addProsody('Check-in received!', { rate: 'medium', volume: 'loud' })
    .addBreak('300ms')
    .addSentence('Your safety contacts have been notified that you are okay.')
    .addSentence('Stay safe out there.')
    .build();
}

function statusReport({ activeAlerts = 0, lastCheckIn = 'unknown', nearbyResponders = 0, pendingCheckIns = 0 } = {}) {
  const b = new SSMLBuilder();
  b.addSentence('Here is your Watch safety status.');
  b.addBreak('300ms');

  if (activeAlerts > 0) {
    b.addProsody(`You have ${activeAlerts} active alert${activeAlerts > 1 ? 's' : ''}.`, { rate: 'medium', volume: 'loud' });
  } else {
    b.addSentence('You have no active alerts. All clear.');
  }
  b.addBreak('200ms');

  b.addSentence(`Your last check-in was ${lastCheckIn}.`);
  b.addBreak('200ms');

  if (nearbyResponders > 0) {
    b.addSentence(`There are ${nearbyResponders} volunteer responders near your registered location.`);
  }

  if (pendingCheckIns > 0) {
    b.addBreak('200ms');
    b.addProsody(`You have ${pendingCheckIns} pending check-in request${pendingCheckIns > 1 ? 's' : ''}.`, { rate: 'medium', volume: 'loud' });
  }

  return b.build();
}

function nearbyRespondersReport(count, names = []) {
  const b = new SSMLBuilder();
  if (count === 0) {
    b.addSentence('There are currently no volunteer responders near your registered location.');
    b.addBreak('200ms');
    b.addSentence('First responders will still be notified in an emergency.');
  } else {
    b.addSentence(`There are ${count} volunteer responders nearby.`);
    if (names.length > 0) {
      b.addBreak('200ms');
      const listed = names.slice(0, 3).join(', ');
      b.addSentence(`Including ${listed}.`);
    }
    b.addBreak('200ms');
    b.addSentence('They will be notified immediately if you trigger an alert.');
  }
  return b.build();
}

function volunteerStatusReport({ isActive = false, shiftsThisWeek = 0, totalResponses = 0 } = {}) {
  const b = new SSMLBuilder();
  b.addSentence(`Your volunteer responder status is currently ${isActive ? 'active' : 'inactive'}.`);
  b.addBreak('200ms');
  if (isActive) {
    b.addSentence(`You have ${shiftsThisWeek} shifts scheduled this week.`);
    b.addSentence(`You have responded to ${totalResponses} check-ins total.`);
  } else {
    b.addSentence('To activate volunteer mode, use the Watch mobile app.');
  }
  return b.build();
}

function emergencyContactsReport(contacts = []) {
  const b = new SSMLBuilder();
  if (contacts.length === 0) {
    b.addSentence('You have no emergency contacts configured.');
    b.addBreak('200ms');
    b.addSentence('Please add contacts using the Watch mobile app.');
  } else {
    b.addSentence(`You have ${contacts.length} emergency contact${contacts.length > 1 ? 's' : ''}.`);
    b.addBreak('200ms');
    contacts.slice(0, 5).forEach((c, i) => {
      b.addSentence(`${i + 1}. ${c.name}, ${c.relationship || 'contact'}.`);
    });
  }
  return b.build();
}

function emergencyPhraseSet(phrase) {
  return new SSMLBuilder()
    .addAudio(SOUNDS.CONFIRM_TONE)
    .addBreak('200ms')
    .addSentence('Your emergency phrase has been updated.')
    .addBreak('200ms')
    .addSentence('For security, I will not repeat it aloud.')
    .addSentence('You can test it anytime by saying it to any Watch-connected device.')
    .build();
}

function error(errorType) {
  const b = new SSMLBuilder();
  b.addAudio(SOUNDS.ERROR_TONE);
  b.addBreak('200ms');

  switch (errorType) {
    case 'AUTH':
      b.addSentence('I could not verify your identity. Please re-link your Watch account in the Alexa app.');
      break;
    case 'CIRCUIT_OPEN':
    case 'TRANSIENT':
    case 'SERVER':
      b.addSentence('The Watch service is temporarily unavailable.');
      b.addBreak('200ms');
      b.addSentence('Please try again in a moment. If this is a real emergency, call 911 directly.');
      break;
    case 'NETWORK':
      b.addSentence('I could not reach the Watch service. Please check your internet connection.');
      b.addBreak('200ms');
      b.addSentence('If this is a real emergency, call 911 directly.');
      break;
    case 'CLIENT':
      b.addSentence('Something went wrong with your request. Please try again.');
      break;
    default:
      b.addSentence('An unexpected error occurred. Please try again, or call 911 if this is an emergency.');
      break;
  }
  return b.build();
}

function welcome() {
  return new SSMLBuilder()
    .addAudio(SOUNDS.CONFIRM_TONE)
    .addBreak('200ms')
    .addProsody('Welcome to The Watch.', { rate: 'medium' })
    .addBreak('300ms')
    .addSentence('Your safety companion is ready.')
    .addBreak('200ms')
    .addSentence('You can say "I need help" to trigger an alert, "I\'m okay" to check in, or "what\'s my status" for a report.')
    .addBreak('200ms')
    .addSentence('What would you like to do?')
    .build();
}

function help() {
  return new SSMLBuilder()
    .addSentence('Here are things you can do with The Watch.')
    .addBreak('300ms')
    .addSentence('Say "I need help" or "emergency" to trigger an SOS alert.')
    .addBreak('200ms')
    .addSentence('Say "I\'m okay" or "check in" to confirm you are safe.')
    .addBreak('200ms')
    .addSentence('Say "what\'s my status" to hear your safety report.')
    .addBreak('200ms')
    .addSentence('Say "cancel alert" to cancel an active emergency.')
    .addBreak('200ms')
    .addSentence('Say "who is nearby" to hear about volunteer responders.')
    .addBreak('200ms')
    .addSentence('Say "my contacts" to hear your emergency contacts.')
    .addBreak('200ms')
    .addSentence('What would you like to do?')
    .build();
}

function goodbye() {
  return new SSMLBuilder()
    .addSentence('Stay safe. The Watch is always here when you need it.')
    .build();
}

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------
module.exports = {
  SSMLBuilder,
  builder: () => new SSMLBuilder(),
  SOUNDS,
  // Pre-built responses
  sosConfirmation,
  sosConfirmationPrompt,
  sosCancelled,
  silentDuressAck,
  checkInConfirmation,
  statusReport,
  nearbyRespondersReport,
  volunteerStatusReport,
  emergencyContactsReport,
  emergencyPhraseSet,
  error,
  welcome,
  help,
  goodbye,
};
