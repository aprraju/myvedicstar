# Email and Contact Business Requirements

## Module scope
This module covers contact workflows, issue reporting, and outbound notification messaging used by the identity and support surfaces.

## Business objectives
The system shall support user communication flows such as contact requests, issue reporting, and feedback while protecting identity and avoiding exposure of untrusted client-supplied identity values.

## Functional requirements
### 1. Contact and issue submission
1. The system shall accept contact requests with email, phone number (optional), subject or reason, message, and type.
2. The system shall require a valid message payload and preserve the expected business metadata for support routing.
3. The system shall derive immutable user identity from validated authenticated claims rather than trusting caller-provided identity metadata.

### 2. Notification delivery
1. The system shall send collected contact messages to the configured support destination, including `contact@myvedicstar.com` in the documented configuration.
2. The system shall integrate with SMTP or email provider configuration defined through application settings.
3. The system shall preserve message subject, body, and user metadata in the outbound communication process.

### 3. Identity integrity
1. The system shall prevent clients from submitting arbitrary user identity claims for an authenticated user request.
2. The system shall use validated identity and profile claims from the current authorized session wherever a user record is required.

## Business rules
1. Contact or support requests shall be treated as an operational workflow rather than an unvalidated content channel.
2. The system shall not trust client-provided identity fields when the user is authenticated.
3. Support submissions shall include safe, minimal metadata needed for triage and response dispatch.

## Non-functional requirements
1. Email operations shall be configurable and separate from business logic where possible.
2. The system shall support failure handling for outbound messaging without exposing internal service details to the user.
3. Contact requests shall support API-level validation and structured error responses.

## External dependencies
- email integration service or SMTP configuration
- authenticated user context
- configuration providers and secrets management

## UI consumer requirements
1. Provide Contact Us, Feedback, and Report an Issue entry points that select the corresponding reason/type and submit through the single configured contact API. Do not reveal the support recipient as a client-controlled field.
2. The form shall collect a reply email, subject, message, and reason/type, with optional phone number. It may prefill a signed-in user's name and email from trusted session/profile data, but the caller's submitted name/email must not establish or override account identity.
3. Anonymous contact submission is a deliberate API exception to the protected application surface. If a caller supplies an Authorization header, an invalid token must fail rather than silently downgrade to anonymous; authenticated callers must also satisfy the API scope.
4. Validate fields before sending and keep entered values available after a failed submission. Disable duplicate submits while pending; show confirmation only after API/provider acceptance.
5. On success, expose a clear confirmation and allow a fresh submission. On validation, throttling, size, authentication, or delivery errors, show a safe actionable error without exposing SMTP/provider details.
6. Web and mobile clients shall use the same supported reason/type mapping and compatible field limits. The client shall not automatically retry the send mutation because delivery may already have been accepted.
