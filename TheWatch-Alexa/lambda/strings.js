/**
 * =============================================================================
 * WRITE-AHEAD LOG (WAL) - strings.js
 * =============================================================================
 * PURPOSE:
 *   Localization strings for TheWatch Alexa Skill. Supports en-US, en-GB,
 *   es-US, es-ES, and es-MX. Used by the localization interceptor to inject
 *   the correct string table into the request attributes based on device locale.
 *
 * ARCHITECTURE:
 *   - Flat key-value string tables per locale
 *   - Interpolation via {{variable}} placeholders (resolved by interceptor)
 *   - Falls back en-US -> en -> default if locale not found
 *   - Keys match intent handler string references exactly
 *
 * EXAMPLE USAGE:
 *   const strings = require('./strings');
 *   const t = strings['en-US'];
 *   console.log(t.WELCOME);
 *   // => "Welcome to The Watch. Your safety companion is ready. ..."
 *
 *   // With interpolation (handled by interceptor):
 *   // t.STATUS_ACTIVE_ALERTS = "You have {{count}} active alerts."
 *   // resolve('STATUS_ACTIVE_ALERTS', { count: 2 })
 *   // => "You have 2 active alerts."
 * =============================================================================
 */

'use strict';

const strings = {

  // ---------------------------------------------------------------------------
  // English (United States)
  // ---------------------------------------------------------------------------
  'en-US': {
    SKILL_NAME: 'The Watch',
    WELCOME: 'Welcome to The Watch. Your safety companion is ready. You can say "I need help" to trigger an alert, "I\'m okay" to check in, or "what\'s my status" for a report. What would you like to do?',
    WELCOME_BACK: 'Welcome back to The Watch. What would you like to do?',
    HELP: 'Here are things you can do with The Watch. Say "I need help" or "emergency" to trigger an SOS alert. Say "I\'m okay" or "check in" to confirm you are safe. Say "what\'s my status" to hear your safety report. Say "cancel alert" to cancel an active emergency. Say "who is nearby" to hear about volunteer responders. Say "my contacts" to hear your emergency contacts. What would you like to do?',
    GOODBYE: 'Stay safe. The Watch is always here when you need it.',
    SOS_CONFIRM_PROMPT: 'You are about to trigger an emergency alert. Are you sure you need help? Say "yes" to confirm, or "no" to cancel.',
    SOS_TRIGGERED: 'Emergency alert activated! Your emergency contacts and nearby responders are being notified now. If this was a mistake, say "cancel alert" within the next 60 seconds. Stay safe. Help is on the way.',
    SOS_CANCELLED: 'Your emergency alert has been cancelled. Your contacts have been notified that you are safe.',
    CHECKIN_SUCCESS: 'Check-in received! Your safety contacts have been notified that you are okay. Stay safe out there.',
    STATUS_NO_ALERTS: 'You have no active alerts. All clear.',
    STATUS_ACTIVE_ALERTS: 'You have {{count}} active alert{{plural}}.',
    STATUS_LAST_CHECKIN: 'Your last check-in was {{time}}.',
    STATUS_NEARBY_RESPONDERS: 'There are {{count}} volunteer responders near your registered location.',
    STATUS_PENDING_CHECKINS: 'You have {{count}} pending check-in request{{plural}}.',
    NEARBY_NONE: 'There are currently no volunteer responders near your registered location. First responders will still be notified in an emergency.',
    NEARBY_COUNT: 'There are {{count}} volunteer responders nearby. They will be notified immediately if you trigger an alert.',
    VOLUNTEER_ACTIVE: 'Your volunteer responder status is currently active. You have {{shifts}} shifts scheduled this week.',
    VOLUNTEER_INACTIVE: 'Your volunteer responder status is currently inactive. To activate volunteer mode, use the Watch mobile app.',
    CONTACTS_NONE: 'You have no emergency contacts configured. Please add contacts using the Watch mobile app.',
    CONTACTS_COUNT: 'You have {{count}} emergency contact{{plural}}.',
    PHRASE_SET: 'Your emergency phrase has been updated. For security, I will not repeat it aloud. You can test it anytime by saying it to any Watch-connected device.',
    PHRASE_PROMPT: 'What would you like your new emergency phrase to be?',
    SILENT_DURESS_ACK: 'Okay, your settings have been updated.',
    ERROR_AUTH: 'I could not verify your identity. Please re-link your Watch account in the Alexa app.',
    ERROR_SERVICE: 'The Watch service is temporarily unavailable. Please try again in a moment. If this is a real emergency, call 911 directly.',
    ERROR_NETWORK: 'I could not reach the Watch service. Please check your internet connection. If this is a real emergency, call 911 directly.',
    ERROR_GENERIC: 'An unexpected error occurred. Please try again, or call 911 if this is an emergency.',
    REPROMPT: 'What would you like to do? You can say "help" for a list of commands.',
    FALLBACK: 'I\'m not sure how to help with that. Say "help" for a list of things I can do.',
  },

  // ---------------------------------------------------------------------------
  // English (United Kingdom)
  // ---------------------------------------------------------------------------
  'en-GB': {
    SKILL_NAME: 'The Watch',
    WELCOME: 'Welcome to The Watch. Your safety companion is ready. You can say "I need help" to trigger an alert, "I\'m okay" to check in, or "what\'s my status" for a report. What would you like to do?',
    WELCOME_BACK: 'Welcome back to The Watch. What would you like to do?',
    HELP: 'Here are things you can do with The Watch. Say "I need help" or "emergency" to trigger an SOS alert. Say "I\'m okay" or "check in" to confirm you are safe. Say "what\'s my status" to hear your safety report. Say "cancel alert" to cancel an active emergency. Say "who is nearby" to hear about volunteer responders. Say "my contacts" to hear your emergency contacts. What would you like to do?',
    GOODBYE: 'Stay safe. The Watch is always here when you need it.',
    SOS_CONFIRM_PROMPT: 'You are about to trigger an emergency alert. Are you sure you need help? Say "yes" to confirm, or "no" to cancel.',
    SOS_TRIGGERED: 'Emergency alert activated! Your emergency contacts and nearby responders are being notified now. If this was a mistake, say "cancel alert" within the next 60 seconds. Stay safe. Help is on the way.',
    SOS_CANCELLED: 'Your emergency alert has been cancelled. Your contacts have been notified that you are safe.',
    CHECKIN_SUCCESS: 'Check-in received! Your safety contacts have been notified that you are all right. Stay safe out there.',
    STATUS_NO_ALERTS: 'You have no active alerts. All clear.',
    STATUS_ACTIVE_ALERTS: 'You have {{count}} active alert{{plural}}.',
    STATUS_LAST_CHECKIN: 'Your last check-in was {{time}}.',
    STATUS_NEARBY_RESPONDERS: 'There are {{count}} volunteer responders near your registered location.',
    STATUS_PENDING_CHECKINS: 'You have {{count}} pending check-in request{{plural}}.',
    NEARBY_NONE: 'There are currently no volunteer responders near your registered location. Emergency services will still be notified in an emergency.',
    NEARBY_COUNT: 'There are {{count}} volunteer responders nearby. They will be notified immediately if you trigger an alert.',
    VOLUNTEER_ACTIVE: 'Your volunteer responder status is currently active. You have {{shifts}} shifts scheduled this week.',
    VOLUNTEER_INACTIVE: 'Your volunteer responder status is currently inactive. To activate volunteer mode, use the Watch mobile app.',
    CONTACTS_NONE: 'You have no emergency contacts configured. Please add contacts using the Watch mobile app.',
    CONTACTS_COUNT: 'You have {{count}} emergency contact{{plural}}.',
    PHRASE_SET: 'Your emergency phrase has been updated. For security, I will not repeat it aloud. You can test it anytime by saying it to any Watch-connected device.',
    PHRASE_PROMPT: 'What would you like your new emergency phrase to be?',
    SILENT_DURESS_ACK: 'Okay, your settings have been updated.',
    ERROR_AUTH: 'I could not verify your identity. Please re-link your Watch account in the Alexa app.',
    ERROR_SERVICE: 'The Watch service is temporarily unavailable. Please try again in a moment. If this is a real emergency, ring 999 directly.',
    ERROR_NETWORK: 'I could not reach the Watch service. Please check your internet connection. If this is a real emergency, ring 999 directly.',
    ERROR_GENERIC: 'An unexpected error occurred. Please try again, or ring 999 if this is an emergency.',
    REPROMPT: 'What would you like to do? You can say "help" for a list of commands.',
    FALLBACK: 'I\'m not sure how to help with that. Say "help" for a list of things I can do.',
  },

  // ---------------------------------------------------------------------------
  // Spanish (United States / Latin America)
  // ---------------------------------------------------------------------------
  'es-US': {
    SKILL_NAME: 'The Watch',
    WELCOME: 'Bienvenido a The Watch. Tu companhero de seguridad esta listo. Puedes decir "necesito ayuda" para activar una alerta, "estoy bien" para registrarte, o "cual es mi estado" para un reporte. Que deseas hacer?',
    WELCOME_BACK: 'Bienvenido de vuelta a The Watch. Que deseas hacer?',
    HELP: 'Estas son las cosas que puedes hacer con The Watch. Di "necesito ayuda" o "emergencia" para activar una alerta SOS. Di "estoy bien" para confirmar que estas a salvo. Di "cual es mi estado" para escuchar tu reporte de seguridad. Di "cancelar alerta" para cancelar una emergencia activa. Di "quien esta cerca" para saber sobre voluntarios cercanos. Di "mis contactos" para escuchar tus contactos de emergencia. Que deseas hacer?',
    GOODBYE: 'Cuidate. The Watch siempre esta aqui cuando lo necesites.',
    SOS_CONFIRM_PROMPT: 'Estas a punto de activar una alerta de emergencia. Estas seguro de que necesitas ayuda? Di "si" para confirmar, o "no" para cancelar.',
    SOS_TRIGGERED: 'Alerta de emergencia activada! Tus contactos de emergencia y voluntarios cercanos estan siendo notificados ahora. Si fue un error, di "cancelar alerta" dentro de los proximos 60 segundos. Cuidate. La ayuda va en camino.',
    SOS_CANCELLED: 'Tu alerta de emergencia ha sido cancelada. Tus contactos han sido notificados de que estas a salvo.',
    CHECKIN_SUCCESS: 'Registro recibido! Tus contactos de seguridad han sido notificados de que estas bien. Cuidate.',
    STATUS_NO_ALERTS: 'No tienes alertas activas. Todo despejado.',
    STATUS_ACTIVE_ALERTS: 'Tienes {{count}} alerta{{plural}} activa{{plural}}.',
    STATUS_LAST_CHECKIN: 'Tu ultimo registro fue {{time}}.',
    STATUS_NEARBY_RESPONDERS: 'Hay {{count}} voluntarios cerca de tu ubicacion registrada.',
    STATUS_PENDING_CHECKINS: 'Tienes {{count}} solicitud{{plural}} de registro pendiente{{plural}}.',
    NEARBY_NONE: 'Actualmente no hay voluntarios cerca de tu ubicacion registrada. Los servicios de emergencia seran notificados en caso de emergencia.',
    NEARBY_COUNT: 'Hay {{count}} voluntarios cercanos. Seran notificados inmediatamente si activas una alerta.',
    VOLUNTEER_ACTIVE: 'Tu estado de voluntario esta actualmente activo. Tienes {{shifts}} turnos programados esta semana.',
    VOLUNTEER_INACTIVE: 'Tu estado de voluntario esta actualmente inactivo. Para activar el modo voluntario, usa la aplicacion movil de Watch.',
    CONTACTS_NONE: 'No tienes contactos de emergencia configurados. Por favor agrega contactos usando la aplicacion movil de Watch.',
    CONTACTS_COUNT: 'Tienes {{count}} contacto{{plural}} de emergencia.',
    PHRASE_SET: 'Tu frase de emergencia ha sido actualizada. Por seguridad, no la repetire en voz alta. Puedes probarla en cualquier momento diciendola a cualquier dispositivo conectado a Watch.',
    PHRASE_PROMPT: 'Cual te gustaria que fuera tu nueva frase de emergencia?',
    SILENT_DURESS_ACK: 'Listo, tu configuracion ha sido actualizada.',
    ERROR_AUTH: 'No pude verificar tu identidad. Por favor vuelve a vincular tu cuenta de Watch en la aplicacion de Alexa.',
    ERROR_SERVICE: 'El servicio de Watch no esta disponible temporalmente. Por favor intenta de nuevo en un momento. Si es una emergencia real, llama al 911 directamente.',
    ERROR_NETWORK: 'No pude conectar con el servicio de Watch. Por favor verifica tu conexion a internet. Si es una emergencia real, llama al 911 directamente.',
    ERROR_GENERIC: 'Ocurrio un error inesperado. Por favor intenta de nuevo, o llama al 911 si es una emergencia.',
    REPROMPT: 'Que deseas hacer? Puedes decir "ayuda" para una lista de comandos.',
    FALLBACK: 'No estoy seguro de como ayudar con eso. Di "ayuda" para una lista de cosas que puedo hacer.',
  },
};

// Aliases for locale variants
strings['es-ES'] = strings['es-US'];
strings['es-MX'] = strings['es-US'];
strings['en'] = strings['en-US'];
strings['es'] = strings['es-US'];

/**
 * Resolve a string key with interpolation.
 * @param {string} locale - e.g. 'en-US'
 * @param {string} key    - e.g. 'SOS_TRIGGERED'
 * @param {Object} vars   - interpolation variables, e.g. { count: 3, plural: 's' }
 * @returns {string}
 */
function resolve(locale, key, vars = {}) {
  const table = strings[locale] || strings['en-US'];
  let text = table[key] || strings['en-US'][key] || key;
  for (const [k, v] of Object.entries(vars)) {
    text = text.replace(new RegExp(`\\{\\{${k}\\}\\}`, 'g'), String(v));
  }
  return text;
}

module.exports = { strings, resolve };
