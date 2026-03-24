/**
 * ============================================================================
 * WRITE-AHEAD LOG (WAL)
 * ============================================================================
 * File:        response-builder.js
 * Module:      TheWatch Google Home Integration - Response Builder
 * Created:     2026-03-24
 * Author:      TheWatch Platform Team
 * ----------------------------------------------------------------------------
 * PURPOSE:
 *   Builds rich Google Assistant responses using the @assistant/conversation
 *   SDK. Supports Simple responses, Card responses, Suggestion chips,
 *   Table cards, and Media responses. Encapsulates response construction
 *   to keep handler code clean.
 *
 * RICH RESPONSE TYPES:
 *   - Simple: Text + optional SSML speech
 *   - Card: Title + subtitle + text + image + button
 *   - Table: Columnar data for lists (responders, contacts, shelters)
 *   - Suggestion: Quick-reply chips for next actions
 *   - Media: Audio playback (future: alert sounds)
 *
 * EXAMPLE USAGE:
 *   const rb = require('./response-builder');
 *   conv.add(rb.simple('SOS triggered.'));
 *   conv.add(rb.card({ title: 'SOS Alert', text: 'Help is on the way.' }));
 *   conv.add(...rb.suggestions(['Check status', 'Cancel SOS']));
 *
 * CHANGES:
 *   2026-03-24  Initial creation with all response types
 * ============================================================================
 */

'use strict';

const {
  Simple,
  Card,
  Suggestion,
  Table,
  Image,
  Link,
} = require('@assistant/conversation');

const responseBuilder = {
  /**
   * Build a Simple response with optional SSML.
   * @param {string} text - Plain text response
   * @param {string} [ssml] - SSML speech markup (if different from text)
   * @returns {Simple}
   *
   * Example:
   *   rb.simple('Help is on the way.', '<speak>Help is <emphasis>on the way</emphasis>.</speak>');
   */
  simple(text, ssml) {
    const response = new Simple({ text });
    if (ssml) {
      response.speech = ssml;
    }
    return response;
  },

  /**
   * Build a Card response for visual displays (Smart Displays, phones).
   * @param {{ title: string, subtitle?: string, text: string, imageUrl?: string, imageAlt?: string, buttonText?: string, buttonUrl?: string }} opts
   * @returns {Card}
   *
   * Example:
   *   rb.card({
   *     title: 'SOS Alert Active',
   *     subtitle: 'Triggered at 10:35 AM',
   *     text: '3 responders notified. ETA 4 minutes.',
   *     imageUrl: 'https://cdn.thewatch.app/icons/sos-active.png',
   *     imageAlt: 'SOS Active Icon',
   *     buttonText: 'Open The Watch',
   *     buttonUrl: 'https://app.thewatch.app/sos',
   *   });
   */
  card({ title, subtitle, text, imageUrl, imageAlt, buttonText, buttonUrl }) {
    const cardOpts = { title, text };
    if (subtitle) cardOpts.subtitle = subtitle;
    if (imageUrl) {
      cardOpts.image = new Image({
        url: imageUrl,
        alt: imageAlt || title,
        width: 400,
        height: 300,
      });
    }
    if (buttonText && buttonUrl) {
      cardOpts.button = new Link({
        name: buttonText,
        open: { url: buttonUrl },
      });
    }
    return new Card(cardOpts);
  },

  /**
   * Build Suggestion chips for quick follow-up actions.
   * @param {string[]} chips - Array of suggestion text strings
   * @returns {Suggestion[]}
   *
   * Example:
   *   rb.suggestions(['Check status', 'Cancel SOS', 'Emergency contacts']);
   */
  suggestions(chips) {
    return chips.map((chip) => new Suggestion({ title: chip }));
  },

  /**
   * Build a Table card for displaying structured data (contacts, responders, shelters).
   * @param {{ title: string, subtitle?: string, columns: string[], rows: string[][] }} opts
   * @returns {Table}
   *
   * Example:
   *   rb.table({
   *     title: 'Emergency Contacts',
   *     columns: ['Name', 'Phone', 'Relationship'],
   *     rows: [
   *       ['Jane Doe', '555-0100', 'Spouse'],
   *       ['John Smith', '555-0101', 'Neighbor'],
   *     ],
   *   });
   */
  table({ title, subtitle, columns, rows }) {
    return new Table({
      title,
      subtitle: subtitle || '',
      columns: columns.map((col) => ({ header: col })),
      rows: rows.map((row) => ({
        cells: row.map((cell) => ({ text: String(cell) })),
      })),
    });
  },

  /**
   * Build a complete SOS triggered response with card and suggestions.
   * @param {object} strings - Locale string set
   * @param {string} userName
   * @returns {{ simple: Simple, card: Card, suggestions: Suggestion[] }}
   */
  sosTriggered(strings, userName) {
    const text = strings.sos.triggered({ name: userName });
    return {
      simple: this.simple(
        text,
        `<speak><emphasis level="strong">SOS alert triggered.</emphasis> <break time="300ms"/> Help is on the way, ${userName}. Your emergency contacts and nearby volunteers have been notified. <break time="200ms"/> Stay calm.</speak>`
      ),
      card: this.card({
        title: 'SOS Alert Active',
        text,
        imageUrl: 'https://cdn.thewatch.app/icons/sos-active.png',
        imageAlt: 'SOS Active',
        buttonText: 'Open The Watch',
        buttonUrl: 'https://app.thewatch.app/sos',
      }),
      suggestions: this.suggestions(strings.suggestions.afterSos),
    };
  },

  /**
   * Build a status response with conditional card content.
   * @param {object} strings - Locale string set
   * @param {{ safe: boolean, name: string, lastCheckIn?: string, alertTime?: string, respondersCount?: number }} data
   * @returns {{ simple: Simple, card: Card, suggestions: Suggestion[] }}
   */
  statusResponse(strings, data) {
    let text;
    if (data.safe) {
      text = strings.status.safe({ name: data.name, lastCheckIn: data.lastCheckIn || 'unknown' });
    } else {
      text = strings.status.alertActive({
        name: data.name,
        alertTime: data.alertTime || 'unknown',
        respondersCount: data.respondersCount || 0,
      });
    }
    return {
      simple: this.simple(text),
      card: this.card({
        title: data.safe ? 'Status: Safe' : 'Status: Alert Active',
        subtitle: data.safe ? `Last check-in: ${data.lastCheckIn || 'N/A'}` : `Alert since: ${data.alertTime || 'N/A'}`,
        text,
        imageUrl: data.safe
          ? 'https://cdn.thewatch.app/icons/safe.png'
          : 'https://cdn.thewatch.app/icons/alert.png',
        imageAlt: data.safe ? 'Safe' : 'Alert',
        buttonText: 'View Details',
        buttonUrl: 'https://app.thewatch.app/status',
      }),
      suggestions: this.suggestions(data.safe ? strings.suggestions.afterCheckIn : strings.suggestions.afterSos),
    };
  },

  /**
   * Build an error response.
   * @param {string} message
   * @param {string[]} [chipSuggestions]
   * @returns {{ simple: Simple, suggestions: Suggestion[] }}
   */
  errorResponse(message, chipSuggestions = ['Try again']) {
    return {
      simple: this.simple(message),
      suggestions: this.suggestions(chipSuggestions),
    };
  },

  /**
   * Build a contacts table response.
   * @param {object} strings
   * @param {{ name: string, phone: string, relationship: string }[]} contacts
   * @returns {{ simple: Simple, table: Table, suggestions: Suggestion[] }}
   */
  contactsResponse(strings, contacts) {
    const names = contacts.map((c) => c.name).join(', ');
    return {
      simple: this.simple(strings.contacts.list({ contacts: names })),
      table: this.table({
        title: 'Emergency Contacts',
        columns: ['Name', 'Phone', 'Relationship'],
        rows: contacts.map((c) => [c.name, c.phone, c.relationship]),
      }),
      suggestions: this.suggestions(strings.suggestions.main),
    };
  },

  /**
   * Build a responders info response.
   * @param {object} strings
   * @param {{ count: number, nearest: string, responders?: { name: string, distance: string, eta: string }[] }} data
   * @returns {{ simple: Simple, table?: Table, suggestions: Suggestion[] }}
   */
  respondersResponse(strings, data) {
    const result = {
      simple: this.simple(strings.responders.found({ count: data.count, nearest: data.nearest })),
      suggestions: this.suggestions(strings.suggestions.afterSos),
    };
    if (data.responders && data.responders.length > 0) {
      result.table = this.table({
        title: 'Nearby Responders',
        columns: ['Name', 'Distance', 'ETA'],
        rows: data.responders.map((r) => [r.name, r.distance, r.eta]),
      });
    }
    return result;
  },

  /**
   * Build a shelters info response.
   * @param {object} strings
   * @param {{ name: string, address: string, capacity: string, distance: string }[]} shelters
   * @returns {{ simple: Simple, table: Table, suggestions: Suggestion[] }}
   */
  sheltersResponse(strings, shelters) {
    const names = shelters.map((s) => `${s.name} (${s.distance})`).join(', ');
    return {
      simple: this.simple(strings.shelter.info({ shelters: names })),
      table: this.table({
        title: 'Nearby Shelters',
        columns: ['Name', 'Address', 'Capacity', 'Distance'],
        rows: shelters.map((s) => [s.name, s.address, s.capacity, s.distance]),
      }),
      suggestions: this.suggestions(strings.suggestions.main),
    };
  },
};

module.exports = responseBuilder;
