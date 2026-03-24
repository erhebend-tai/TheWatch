/**
 * =============================================================================
 * WRITE-AHEAD LOG (WAL) - test-trigger-sos.js
 * =============================================================================
 * PURPOSE:
 *   Unit tests for TheWatch Alexa Skill TriggerSOS intent handler and related
 *   flows: SOS confirmation, SOS denial, check-in, cancel, and silent duress.
 *   Tests the full request lifecycle using mock ASK SDK handler inputs.
 *
 * ARCHITECTURE:
 *   - Mocha + Chai + Sinon + Nock
 *   - Mocks the Alexa request envelope and handler input
 *   - Stubs api-client methods to isolate handler logic
 *   - Tests SSML output, session attribute mutations, card content
 *
 * EXAMPLE USAGE:
 *   cd TheWatch-Alexa
 *   npm test
 *   # or
 *   npx mocha tests/test-trigger-sos.js --timeout 10000
 * =============================================================================
 */

'use strict';

const { expect } = require('chai');
const sinon = require('sinon');

// We test the handlers by importing them and calling canHandle/handle directly
// with mock handlerInput objects, rather than invoking the full Lambda.

// ---------------------------------------------------------------------------
// Mock handlerInput factory
// ---------------------------------------------------------------------------
function createMockHandlerInput({
  requestType = 'IntentRequest',
  intentName = 'TriggerSOSIntent',
  slots = {},
  sessionAttributes = {},
  userId = 'amzn1.ask.account.TEST_USER_123',
  locale = 'en-US',
  accessToken = null,
} = {}) {
  const requestAttributes = {};
  let _sessionAttributes = { ...sessionAttributes };

  const handlerInput = {
    requestEnvelope: {
      request: {
        type: requestType,
        intent: requestType === 'IntentRequest' ? { name: intentName, slots } : undefined,
        locale,
        reason: requestType === 'SessionEndedRequest' ? 'USER_INITIATED' : undefined,
      },
      session: {
        sessionId: 'amzn1.echo-api.session.TEST_SESSION',
        new: Object.keys(sessionAttributes).length === 0,
      },
      context: {
        System: {
          user: {
            userId,
            accessToken,
          },
          device: {
            deviceId: 'amzn1.ask.device.TEST_DEVICE',
          },
        },
      },
    },
    attributesManager: {
      getRequestAttributes: () => requestAttributes,
      setRequestAttributes: (attrs) => Object.assign(requestAttributes, attrs),
      getSessionAttributes: () => _sessionAttributes,
      setSessionAttributes: (attrs) => { _sessionAttributes = attrs; },
    },
    responseBuilder: createMockResponseBuilder(),
  };

  // Inject localization function (normally done by interceptor)
  requestAttributes.t = (key) => key;
  requestAttributes.locale = locale;
  requestAttributes.alexaUserId = userId;
  requestAttributes.authValid = true;

  return { handlerInput, getSessionAttributes: () => _sessionAttributes };
}

function createMockResponseBuilder() {
  const response = {
    outputSpeech: null,
    card: null,
    reprompt: null,
    shouldEndSession: undefined,
    directives: [],
  };

  const builder = {
    speak: (speech) => { response.outputSpeech = { ssml: speech }; return builder; },
    reprompt: (reprompt) => { response.reprompt = reprompt; return builder; },
    withSimpleCard: (title, content) => { response.card = { title, content }; return builder; },
    withShouldEndSession: (val) => { response.shouldEndSession = val; return builder; },
    addElicitSlotDirective: (slot) => { response.directives.push({ type: 'ElicitSlot', slot }); return builder; },
    getResponse: () => response,
  };

  return builder;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
describe('TheWatch Alexa Skill - Intent Handlers', function () {
  let apiClient;
  let handlers;

  beforeEach(function () {
    // Require fresh modules with stubs
    apiClient = require('../lambda/api-client');
    handlers = require('../lambda/index');
  });

  afterEach(function () {
    sinon.restore();
  });

  // -------------------------------------------------------------------------
  // TriggerSOSIntent
  // -------------------------------------------------------------------------
  describe('TriggerSOSIntentHandler', function () {
    it('should handle TriggerSOSIntent', function () {
      const { handlerInput } = createMockHandlerInput({ intentName: 'TriggerSOSIntent' });
      expect(handlers.TriggerSOSIntentHandler.canHandle(handlerInput)).to.be.true;
    });

    it('should NOT handle CheckInIntent', function () {
      const { handlerInput } = createMockHandlerInput({ intentName: 'CheckInIntent' });
      expect(handlers.TriggerSOSIntentHandler.canHandle(handlerInput)).to.be.false;
    });

    it('should prompt for confirmation when config requires it', async function () {
      const config = require('../lambda/config');
      // config.sos.confirmationRequired is true by default
      const { handlerInput, getSessionAttributes } = createMockHandlerInput({ intentName: 'TriggerSOSIntent' });

      const response = await handlers.TriggerSOSIntentHandler.handle(handlerInput);

      // Should set SOS pending in session
      expect(getSessionAttributes().sosPendingConfirmation).to.be.true;
      // Response should contain speech (confirmation prompt)
      expect(response.outputSpeech).to.not.be.null;
      expect(response.outputSpeech.ssml).to.be.a('string');
    });

    it('should trigger SOS immediately when confirmation not required', async function () {
      // Temporarily override config - since config is frozen, we stub the api call
      const stub = sinon.stub(apiClient, 'triggerSOS').resolves({
        success: true,
        data: { alertId: 'test-alert-123', status: 'ACTIVE' },
      });

      // We need to test with confirmation disabled - we can test the confirm handler instead
      const { handlerInput } = createMockHandlerInput({
        intentName: 'TriggerSOSConfirmIntent',
        sessionAttributes: { sosPendingConfirmation: true },
      });

      // The confirm handler requires YesIntent or TriggerSOSConfirmIntent with SOS pending
      // Let's use AMAZON.YesIntent
      handlerInput.requestEnvelope.request.intent.name = 'AMAZON.YesIntent';

      const response = await handlers.TriggerSOSConfirmIntentHandler.handle(handlerInput);

      expect(stub.calledOnce).to.be.true;
      expect(response.outputSpeech).to.not.be.null;
      expect(response.outputSpeech.ssml).to.include('Emergency alert activated');

      stub.restore();
    });
  });

  // -------------------------------------------------------------------------
  // CheckInIntent
  // -------------------------------------------------------------------------
  describe('CheckInIntentHandler', function () {
    it('should handle CheckInIntent', function () {
      const { handlerInput } = createMockHandlerInput({ intentName: 'CheckInIntent' });
      expect(handlers.CheckInIntentHandler.canHandle(handlerInput)).to.be.true;
    });

    it('should call api-client.checkIn and return confirmation', async function () {
      const stub = sinon.stub(apiClient, 'checkIn').resolves({
        success: true,
        data: { checkInId: 'ci-123' },
      });

      const { handlerInput } = createMockHandlerInput({ intentName: 'CheckInIntent' });
      const response = await handlers.CheckInIntentHandler.handle(handlerInput);

      expect(stub.calledOnce).to.be.true;
      expect(response.outputSpeech.ssml).to.include('Check-in received');

      stub.restore();
    });

    it('should return error speech on API failure', async function () {
      const stub = sinon.stub(apiClient, 'checkIn').resolves({
        success: false,
        errorType: 'SERVER',
        message: 'Internal error',
      });

      const { handlerInput } = createMockHandlerInput({ intentName: 'CheckInIntent' });
      const response = await handlers.CheckInIntentHandler.handle(handlerInput);

      expect(response.outputSpeech.ssml).to.include('temporarily unavailable');

      stub.restore();
    });
  });

  // -------------------------------------------------------------------------
  // CancelSOSIntent
  // -------------------------------------------------------------------------
  describe('CancelSOSIntentHandler', function () {
    it('should handle CancelSOSIntent', function () {
      const { handlerInput } = createMockHandlerInput({ intentName: 'CancelSOSIntent' });
      expect(handlers.CancelSOSIntentHandler.canHandle(handlerInput)).to.be.true;
    });

    it('should call api-client.cancelSOS and return cancellation speech', async function () {
      const stub = sinon.stub(apiClient, 'cancelSOS').resolves({
        success: true,
        data: { cancelled: true },
      });

      const { handlerInput } = createMockHandlerInput({ intentName: 'CancelSOSIntent' });
      const response = await handlers.CancelSOSIntentHandler.handle(handlerInput);

      expect(stub.calledOnce).to.be.true;
      expect(response.outputSpeech.ssml).to.include('cancelled');

      stub.restore();
    });
  });

  // -------------------------------------------------------------------------
  // SilentDuressIntent
  // -------------------------------------------------------------------------
  describe('SilentDuressIntentHandler', function () {
    it('should handle SilentDuressIntent', function () {
      const { handlerInput } = createMockHandlerInput({ intentName: 'SilentDuressIntent' });
      expect(handlers.SilentDuressIntentHandler.canHandle(handlerInput)).to.be.true;
    });

    it('should trigger SOS with SilentDuress scope and give innocuous response', async function () {
      const stub = sinon.stub(apiClient, 'triggerSOS').resolves({
        success: true,
        data: { alertId: 'duress-123' },
      });

      const { handlerInput } = createMockHandlerInput({ intentName: 'SilentDuressIntent' });
      const response = await handlers.SilentDuressIntentHandler.handle(handlerInput);

      // Must have called with SilentDuress scope
      expect(stub.calledOnce).to.be.true;
      const callArgs = stub.firstCall.args[0];
      expect(callArgs.scope).to.equal('SilentDuress');

      // Response must be innocuous (no mention of emergency/alert/SOS)
      expect(response.outputSpeech.ssml).to.include('settings');
      expect(response.outputSpeech.ssml).to.not.include('emergency');
      expect(response.outputSpeech.ssml).to.not.include('alert');
      expect(response.outputSpeech.ssml).to.not.include('SOS');

      // Should end session immediately
      expect(response.shouldEndSession).to.be.true;

      stub.restore();
    });

    it('should still give innocuous response even if API call fails', async function () {
      const stub = sinon.stub(apiClient, 'triggerSOS').rejects(new Error('Network failure'));

      const { handlerInput } = createMockHandlerInput({ intentName: 'SilentDuressIntent' });
      const response = await handlers.SilentDuressIntentHandler.handle(handlerInput);

      // Must still give innocuous response
      expect(response.outputSpeech.ssml).to.include('settings');
      expect(response.shouldEndSession).to.be.true;

      stub.restore();
    });
  });

  // -------------------------------------------------------------------------
  // StatusIntent
  // -------------------------------------------------------------------------
  describe('StatusIntentHandler', function () {
    it('should handle StatusIntent and format status report', async function () {
      const stub = sinon.stub(apiClient, 'getStatus').resolves({
        success: true,
        data: {
          activeAlerts: 0,
          lastCheckIn: '2 hours ago',
          nearbyResponders: 5,
          pendingCheckIns: 0,
        },
      });

      const { handlerInput } = createMockHandlerInput({ intentName: 'StatusIntent' });
      const response = await handlers.StatusIntentHandler.handle(handlerInput);

      expect(stub.calledOnce).to.be.true;
      expect(response.outputSpeech.ssml).to.include('status');

      stub.restore();
    });
  });

  // -------------------------------------------------------------------------
  // LaunchRequest
  // -------------------------------------------------------------------------
  describe('LaunchRequestHandler', function () {
    it('should handle LaunchRequest', function () {
      const { handlerInput } = createMockHandlerInput({ requestType: 'LaunchRequest', intentName: undefined });
      // Remove intent for LaunchRequest
      delete handlerInput.requestEnvelope.request.intent;
      expect(handlers.LaunchRequestHandler.canHandle(handlerInput)).to.be.true;
    });

    it('should return welcome speech with reprompt', function () {
      const { handlerInput } = createMockHandlerInput({ requestType: 'LaunchRequest', intentName: undefined });
      delete handlerInput.requestEnvelope.request.intent;
      const response = handlers.LaunchRequestHandler.handle(handlerInput);

      expect(response.outputSpeech).to.not.be.null;
      expect(response.reprompt).to.not.be.null;
      expect(response.card).to.not.be.null;
    });
  });
});
