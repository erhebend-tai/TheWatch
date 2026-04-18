# Google Cloud Functions (GCF) Architecture Plan

This document outlines the required serverless functions to support the asynchronous, event-driven, and scheduled tasks for The Watch project. Using GCFs (or equivalent like Azure Functions/AWS Lambda) decouples the main API from long-running background processes, improving scalability and resilience.

## Core Functions

| Function Name                  | Trigger                                       | Domain                | Description                                                                                             |
| ------------------------------ | --------------------------------------------- | --------------------- | ------------------------------------------------------------------------------------------------------- |
| `onEvidenceSubmitted`          | Cloud Storage (onFinalize)                    | Evidence Processing   | Transcribes audio, extracts video frames, runs virus scans, and updates Firestore metadata upon file upload. |
| `dispatchNotification`         | Firestore (`onCreate` on `/notifications/{id}`) | Notifications         | Sends cross-platform push notifications, SMS, and emails via providers like FCM, Twilio, and SendGrid.    |
| `processSurveyResponse`        | Firestore (`onCreate` on `/surveyResponses/{id}`) | Surveys & Insights   | Aggregates answers from crowd-sourcing surveys in real-time to generate actionable insights.            |
| `checkMissedCheckIns`          | Cloud Scheduler (Every Minute)                | Child Safety          | Queries for `ChildCheckIn` documents that are past their `scheduledTime` and triggers escalation rules.     |
| `aggregateHealthTrends`        | Cloud Scheduler (Hourly)                      | Family Health         | Rolls up raw `vital_sign_readings` into daily and weekly trend aggregates for the dashboard.              |
| `githubWebhookReceiver`        | HTTP Request                                  | Developer Experience  | Listens for GitHub webhook events (e.g., commits, PRs) to update work items or trigger documentation jobs. |
| `iotAlertReceiver`             | HTTP Request / Pub/Sub                        | IoT & Smart Home      | Ingests alerts from third-party smart devices (e.g., smoke detectors, fall sensors) and creates an `Alert`. |
| `cleanupExpiredData`           | Cloud Scheduler (Daily)                       | System Maintenance    | Deletes expired `FileDownload` tokens, archives old `Incidents`, and enforces data retention policies.    |
| `onUserCreate`                 | Firebase Auth (`onCreate`)                    | User Management       | Initializes a user profile in Firestore when a new user signs up via Firebase Authentication.             |

## Implementation Details

### 1. `onEvidenceSubmitted`
- **Trigger:** `google.storage.object.finalize` on the `gs://thewatch-evidence-uploads` bucket.
- **Actions:**
    1.  Determine file type (audio, video, image).
    2.  If audio, call the Speech-to-Text API to get a transcript.
    3.  If video, use Cloud Vision API to extract keyframes and detect sensitive content.
    4.  Generate a thumbnail for video/image files.
    5.  Update the corresponding `FileUpload` document in Firestore with the transcript, metadata, and thumbnail URL.
- **Dependencies:** Google Cloud Storage, Google Cloud Speech-to-Text, Google Cloud Vision, Firestore.

### 2. `dispatchNotification`
- **Trigger:** `providers/cloud.firestore/eventTypes/document.create` on `documents/notifications/{notificationId}`.
- **Actions:**
    1.  Read the notification payload (target users, message, type).
    2.  Fetch user device tokens and contact preferences from Firestore.
    3.  If push notification, send via Firebase Cloud Messaging (FCM).
    4.  If SMS, send via Twilio.
    5.  Update the notification document with the delivery status (`sent`, `failed`).
- **Dependencies:** Firestore, FCM, Twilio API.

### 3. `checkMissedCheckIns`
- **Trigger:** Cloud Scheduler job running every 60 seconds.
- **Actions:**
    1.  Query the `childCheckIns` collection in Firestore for documents where `status == 'pending'` and `scheduledTime < now() - autoEscalateDelay`.
    2.  For each missed check-in, trigger the associated `EscalationRule` (e.g., create a high-priority notification for guardians).
    3.  Update the check-in status to `escalated`.
- **Dependencies:** Firestore, Cloud Scheduler.

### 4. `githubWebhookReceiver`
- **Trigger:** HTTP request to a dedicated endpoint.
- **Actions:**
    1.  Verify the webhook signature using the GitHub secret.
    2.  Parse the event payload (e.g., `pull_request.opened`, `push`).
    3.  Create or update a `WorkItem` document in Firestore to reflect the activity.
    4.  Optionally, trigger other workflows like documentation generation.
- **Dependencies:** Firestore, GitHub API.

This serverless architecture ensures that the main API remains fast and focused on user-facing requests, while all complex, long-running, or periodic tasks are handled efficiently in the background.
