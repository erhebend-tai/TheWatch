/**
 * ============================================================================
 * WRITE-AHEAD LOG (WAL)
 * ============================================================================
 * File:        strings.js
 * Module:      TheWatch Google Home Integration - Localization Strings
 * Created:     2026-03-24
 * Author:      TheWatch Platform Team
 * ----------------------------------------------------------------------------
 * PURPOSE:
 *   All user-facing strings in a single module for easy localization.
 *   Supports en-US (default), es-US (Spanish), and fr-CA (French Canadian).
 *   Each key returns a function that accepts template variables.
 *
 * EXAMPLE USAGE:
 *   const strings = require('./strings');
 *   const s = strings.forLocale('en-US');
 *   s.sos.triggered({ name: 'John' });
 *   // => "SOS alert triggered, John. Help is on the way. ..."
 *
 * CHANGES:
 *   2026-03-24  Initial creation with en-US, es-US, fr-CA
 * ============================================================================
 */

'use strict';

const locales = {
  'en-US': {
    welcome: () =>
      'Welcome to The Watch. I can help you trigger an SOS alert, check in, check your safety status, manage emergency contacts, and more. What would you like to do?',
    welcomeReturning: ({ name }) =>
      `Welcome back, ${name}. Your safety is my priority. What can I help you with?`,

    accountLinkingRequired: () =>
      'To use The Watch, I need to link your account. Please open the Google Home app to complete account linking.',

    sos: {
      confirm: () =>
        'Are you sure you want to trigger an SOS alert? This will notify your emergency contacts and nearby responders.',
      triggered: ({ name }) =>
        `SOS alert triggered, ${name}. Help is on the way. Your emergency contacts and nearby volunteers have been notified. Stay calm and stay on the line if you can.`,
      alreadyActive: () =>
        'You already have an active SOS alert. Responders are on their way. Would you like to check your status instead?',
      failed: () =>
        'I was unable to trigger the SOS alert. Please try again, or call 911 directly if you are in immediate danger.',
    },

    checkIn: {
      success: ({ time }) =>
        `Check-in recorded at ${time}. Your contacts have been notified that you are safe.`,
      scheduled: ({ nextTime }) =>
        `Your next scheduled check-in is at ${nextTime}. I will remind you when it is time.`,
      failed: () =>
        'I was unable to record your check-in. Please try again.',
    },

    status: {
      safe: ({ name, lastCheckIn }) =>
        `${name}, your status is safe. Your last check-in was ${lastCheckIn}.`,
      alertActive: ({ name, alertTime, respondersCount }) =>
        `${name}, you have an active SOS alert from ${alertTime}. ${respondersCount} responders have been notified.`,
      noData: () =>
        'I do not have any status information for your account. Please make sure your profile is set up in the app.',
      failed: () =>
        'I was unable to retrieve your status. Please try again.',
    },

    cancel: {
      confirm: () =>
        'Are you sure you want to cancel your active SOS alert?',
      success: () =>
        'Your SOS alert has been cancelled. Your emergency contacts have been notified that you are safe.',
      noActiveAlert: () =>
        'You do not have an active SOS alert to cancel.',
      failed: () =>
        'I was unable to cancel the SOS alert. Please try again or use the app.',
    },

    emergencyPhrase: {
      set: ({ phrase }) =>
        `Your emergency phrase has been set to "${phrase}". When you say this phrase, an SOS alert will be triggered automatically.`,
      confirm: ({ phrase }) =>
        `I will set your emergency phrase to "${phrase}". Is that correct?`,
      failed: () =>
        'I was unable to update your emergency phrase. Please try again.',
    },

    responders: {
      found: ({ count, nearest }) =>
        `There are ${count} volunteer responders near you. The nearest is ${nearest}.`,
      none: () =>
        'There are currently no volunteer responders in your immediate area. Your emergency contacts have still been notified.',
      failed: () =>
        'I was unable to check for nearby responders. Please try again.',
    },

    volunteer: {
      active: ({ since }) =>
        `You are registered as an active volunteer since ${since}. Thank you for your service.`,
      inactive: () =>
        'You are not currently registered as a volunteer. You can sign up through The Watch app.',
      status: ({ respondedCount, rating }) =>
        `You have responded to ${respondedCount} emergencies with a ${rating} star rating. Thank you for keeping your community safe.`,
      failed: () =>
        'I was unable to retrieve your volunteer status. Please try again.',
    },

    contacts: {
      list: ({ contacts }) =>
        `Your emergency contacts are: ${contacts}. Would you like to update them through the app?`,
      none: () =>
        'You have no emergency contacts set up. Please add them through The Watch app for your safety.',
      failed: () =>
        'I was unable to retrieve your emergency contacts. Please try again.',
    },

    silentDuress: {
      activated: () =>
        'Understood.',
      failed: () =>
        'I was unable to process that. Please try again.',
    },

    evacuation: {
      info: ({ routes }) =>
        `Here are the current evacuation routes for your area: ${routes}. Follow official instructions and stay safe.`,
      none: () =>
        'There are no active evacuation orders for your area at this time.',
      failed: () =>
        'I was unable to retrieve evacuation information. Please check local news or call emergency services.',
    },

    shelter: {
      info: ({ shelters }) =>
        `Nearby shelters: ${shelters}. Please proceed to the nearest one if you need assistance.`,
      none: () =>
        'There are no registered shelters near your current location.',
      failed: () =>
        'I was unable to retrieve shelter information. Please contact local emergency services.',
    },

    medical: {
      info: ({ conditions, allergies, medications }) =>
        `Your medical profile: Conditions: ${conditions}. Allergies: ${allergies}. Medications: ${medications}.`,
      none: () =>
        'You have not set up a medical profile. Please add your medical information through The Watch app.',
      failed: () =>
        'I was unable to retrieve your medical information. Please try again.',
    },

    errors: {
      generic: () =>
        'Something went wrong. Please try again. If this is an emergency, call 911 immediately.',
      unauthorized: () =>
        'Your session has expired. Please re-link your account in the Google Home app.',
      rateLimit: () =>
        'You are making too many requests. Please wait a moment and try again.',
      maintenance: () =>
        'The Watch is currently under maintenance. If this is an emergency, please call 911.',
    },

    suggestions: {
      main: ['Trigger SOS', 'Check in', 'My status', 'Cancel SOS', 'Emergency contacts'],
      afterSos: ['Check status', 'Cancel SOS', 'Nearby responders'],
      afterCheckIn: ['My status', 'Emergency contacts', 'Volunteer status'],
      afterCancel: ['Check in', 'My status', 'Emergency contacts'],
    },
  },

  // ---------------------------------------------------------------------------
  // Spanish (US)
  // ---------------------------------------------------------------------------
  'es-US': {
    welcome: () =>
      'Bienvenido a The Watch. Puedo ayudarte a activar una alerta SOS, registrar tu estado, verificar tu seguridad y mas. Que deseas hacer?',
    welcomeReturning: ({ name }) =>
      `Bienvenido de nuevo, ${name}. Tu seguridad es mi prioridad. En que puedo ayudarte?`,
    accountLinkingRequired: () =>
      'Para usar The Watch, necesito vincular tu cuenta. Por favor abre la aplicacion Google Home para completar la vinculacion.',
    sos: {
      confirm: () =>
        'Estas seguro de que deseas activar una alerta SOS? Esto notificara a tus contactos de emergencia y voluntarios cercanos.',
      triggered: ({ name }) =>
        `Alerta SOS activada, ${name}. La ayuda esta en camino. Tus contactos de emergencia y voluntarios cercanos han sido notificados.`,
      alreadyActive: () =>
        'Ya tienes una alerta SOS activa. Los respondedores estan en camino.',
      failed: () =>
        'No pude activar la alerta SOS. Intenta de nuevo o llama al 911 directamente.',
    },
    checkIn: {
      success: ({ time }) => `Registro exitoso a las ${time}. Tus contactos han sido notificados.`,
      scheduled: ({ nextTime }) => `Tu proximo registro es a las ${nextTime}.`,
      failed: () => 'No pude registrar tu estado. Intenta de nuevo.',
    },
    status: {
      safe: ({ name, lastCheckIn }) => `${name}, tu estado es seguro. Ultimo registro: ${lastCheckIn}.`,
      alertActive: ({ name, alertTime, respondersCount }) => `${name}, tienes una alerta SOS activa desde ${alertTime}. ${respondersCount} respondedores notificados.`,
      noData: () => 'No tengo informacion de estado para tu cuenta.',
      failed: () => 'No pude obtener tu estado. Intenta de nuevo.',
    },
    cancel: {
      confirm: () => 'Estas seguro de que deseas cancelar tu alerta SOS activa?',
      success: () => 'Tu alerta SOS ha sido cancelada. Tus contactos han sido notificados.',
      noActiveAlert: () => 'No tienes una alerta SOS activa para cancelar.',
      failed: () => 'No pude cancelar la alerta SOS. Intenta de nuevo.',
    },
    errors: {
      generic: () => 'Algo salio mal. Intenta de nuevo. Si es una emergencia, llama al 911.',
      unauthorized: () => 'Tu sesion ha expirado. Vincula tu cuenta nuevamente.',
      rateLimit: () => 'Demasiadas solicitudes. Espera un momento.',
      maintenance: () => 'The Watch esta en mantenimiento. Si es una emergencia, llama al 911.',
    },
    suggestions: {
      main: ['Activar SOS', 'Registrarme', 'Mi estado', 'Cancelar SOS', 'Contactos'],
      afterSos: ['Ver estado', 'Cancelar SOS', 'Respondedores'],
      afterCheckIn: ['Mi estado', 'Contactos', 'Voluntario'],
      afterCancel: ['Registrarme', 'Mi estado', 'Contactos'],
    },
  },

  // ---------------------------------------------------------------------------
  // French Canadian
  // ---------------------------------------------------------------------------
  'fr-CA': {
    welcome: () =>
      'Bienvenue sur The Watch. Je peux vous aider a declencher une alerte SOS, a vous enregistrer, a verifier votre statut de securite et plus. Que souhaitez-vous faire?',
    welcomeReturning: ({ name }) =>
      `Bon retour, ${name}. Votre securite est ma priorite. Comment puis-je vous aider?`,
    accountLinkingRequired: () =>
      'Pour utiliser The Watch, je dois lier votre compte. Veuillez ouvrir Google Home pour completer la liaison.',
    sos: {
      confirm: () => 'Etes-vous sur de vouloir declencher une alerte SOS?',
      triggered: ({ name }) => `Alerte SOS declenchee, ${name}. L aide est en route.`,
      alreadyActive: () => 'Vous avez deja une alerte SOS active.',
      failed: () => 'Impossible de declencher l alerte SOS. Appelez le 911.',
    },
    checkIn: {
      success: ({ time }) => `Enregistrement effectue a ${time}.`,
      scheduled: ({ nextTime }) => `Votre prochain enregistrement est a ${nextTime}.`,
      failed: () => 'Impossible d enregistrer votre statut.',
    },
    status: {
      safe: ({ name, lastCheckIn }) => `${name}, votre statut est sur. Dernier enregistrement: ${lastCheckIn}.`,
      alertActive: ({ name, alertTime, respondersCount }) => `${name}, alerte SOS active depuis ${alertTime}. ${respondersCount} intervenants notifies.`,
      noData: () => 'Aucune information de statut disponible.',
      failed: () => 'Impossible de recuperer votre statut.',
    },
    cancel: {
      confirm: () => 'Voulez-vous vraiment annuler votre alerte SOS?',
      success: () => 'Votre alerte SOS a ete annulee.',
      noActiveAlert: () => 'Aucune alerte SOS active a annuler.',
      failed: () => 'Impossible d annuler l alerte.',
    },
    errors: {
      generic: () => 'Quelque chose s est mal passe. Si c est une urgence, appelez le 911.',
      unauthorized: () => 'Session expiree. Veuillez relancer la liaison de compte.',
      rateLimit: () => 'Trop de requetes. Veuillez patienter.',
      maintenance: () => 'The Watch est en maintenance. Appelez le 911 en cas d urgence.',
    },
    suggestions: {
      main: ['Declencher SOS', 'Enregistrement', 'Mon statut', 'Annuler SOS', 'Contacts'],
      afterSos: ['Statut', 'Annuler SOS', 'Intervenants'],
      afterCheckIn: ['Mon statut', 'Contacts', 'Benevole'],
      afterCancel: ['Enregistrement', 'Mon statut', 'Contacts'],
    },
  },
};

/**
 * Get a locale-specific string set. Falls back to en-US.
 * @param {string} locale - BCP 47 locale tag (e.g., "en-US", "es-US")
 * @returns {object} Locale string set
 */
function forLocale(locale) {
  // Try exact match, then language-only, then default
  if (locales[locale]) return locales[locale];
  const lang = locale?.split('-')[0];
  const match = Object.keys(locales).find((k) => k.startsWith(lang));
  return locales[match] || locales['en-US'];
}

module.exports = { locales, forLocale };
