# Email communication

`POST /api/v1/email/send` (alias `POST /api/email/send`) sends Feedback, ReportIssue, or ContactUs
communications to `contact@myvedicstar.com`. There is no client-controlled recipient or account ID.

```json
{
  "email": "visitor@example.com",
  "phoneNumber": null,
  "subject": "Calendar question",
  "message": "Please explain the selected calendar date.",
  "type": "ContactUs",
  "reason": "panchang"
}
```

Email, subject, message and type are required. Limits: email 254 characters, subject 200,
message 4000, optional phone 40 (7–15 digits), optional reason 200. Header/control characters are
rejected; message newlines and tabs are accepted. Unknown fields, including recipient and user ID,
are rejected. The configured sender is used for From; the submitted email is used for Reply-To.
The MIME body is plain text; user text is never interpreted as HTML.

Anonymous requests are accepted. If Authorization is supplied, it must be valid; authenticated
requests require the existing API scope. The existing profile facade reads the account keyed by
validated identity without registering a user or making a self-HTTP call. The email includes the
registered ID, first and last names, registered email, submitted reply address and UTC timestamp.

Success is HTTP 200 with `{"success":true,"message":"Your message has been submitted successfully."}`
only after SMTP acceptance. Invalid input returns 400, authentication/scope failures 401/403,
oversized requests 413, throttling 429, and delivery/configuration failures safe Problem Details with 503.
Existing correlation IDs and logging apply. Message bodies, tokens and SMTP credentials are not logged.

Five requests per five minutes are allowed per validated caller identity or trusted client IP, in addition
to existing global limits. Identical submissions from the same caller coalesce for one minute in bounded
instance memory; failed sends can retry. This is not durable, cross-replica exactly-once delivery.
The forms disable Submit while sending and keep entered values on failure. No database or queue is added.

## SMTP setup

For the confirmed GoDaddy Microsoft 365 mailbox, defaults are `smtp.office365.com:587` with required
STARTTLS, and `contact@myvedicstar.com` for username and From. See
[GoDaddy's SMTP settings](https://www.godaddy.com/help/set-up-microsoft-365-email-with-smtp-on-a-multifunction-device-41962).
Check the mailbox's SMTP AUTH and authentication policy with its administrator; Graph credentials
do not authenticate this mailbox. Do not weaken tenant security defaults solely to enable this feature.
The current adapter supports SMTP username/password authentication; OAuth-only mailboxes need an OAuth
credential adapter before activation. Generic SMTP servers can use implicit TLS with `SecurityMode=SslOnConnect`.
Certificate validation remains enabled. No plaintext or opportunistic TLS fallback is allowed.

Email is disabled until configured. In Visual Studio's Manage User Secrets for `MyVedicStar.API`, merge:

```json
{
  "EmailSettings:Enabled": "true",
  "EmailSettings:Password": "<mailbox SMTP credential>"
}
```

Override host, port, security mode, username, sender and timeout as necessary. Do not replace other
user-secret entries. Enabled configuration is validated at startup and before sending. Disabled
email does not prevent unrelated API functionality and returns 503 for submissions.

Email is enabled in `appsettings.Development.json`, so local startup requires the
`EmailSettings:Password` user secret above. Integration test hosts set `EmailSettings:Enabled=false`
so they never reach a real mailbox.

Production sets `EmailSettings__Enabled=true` in `deploy/environments/prod.json` `settings` and maps
`EmailSettings__Password` to the Key Vault secret `EmailSettingsPassword`; `deploy-app.ps1` registers it
as the lower-case Container App secret `emailsettingspassword`. Populate it before deploying with
`./set-secrets.ps1 -Environment prod -Name EmailSettingsPassword`; `deploy-app.ps1` fails before
creating a revision if the secret is missing. The Container App identity resolves the Key Vault
reference into the environment variable. Do not commit credentials.

## APIM and clients

Import the new POST operation from the combined API's OpenAPI document. Keep existing protected routes
under their JWT validation. The API-level JWT gate must permit only the email POST without Authorization;
requests with Authorization still require validation. The inspected companion repository's APIM template
is an example rather than a verified deployed policy. `deploy/apim/email-auth-condition.xml` provides
the conditional gate and production Entra validator to use at the level where JWT validation is enforced;
retain any additional checks from the effective existing validator.
An operation-level policy cannot bypass a parent JWT validator. Keep JSON content validation,
correlation headers, HTTPS, CORS POST support and IP throttling enabled. If a subscription key is required
at APIM, provide the existing approved public submission path without embedding a privileged key in clients.

The shared React/React Native API client uses optional authentication for contact submissions only,
maps feedback/technical/other reasons to Feedback/ReportIssue/ContactUs, passes reason separately,
and allows 45 seconds for the configured 30-second SMTP operation. Web footer links reuse the contact
form. Native More provides Contact Us, Feedback and Report an Issue links to one shared screen.

Automated tests use captured email/SMTP adapters, not real mailboxes. They verify TLS mode selection,
credentials, fixed recipient, Reply-To, SMTP acceptance/failure behavior, input validation, optional
authentication, registered-profile lookup, throttling, reason forwarding and duplicate suppression.
Live GoDaddy delivery, effective APIM policies, and Android/iOS device delivery require environment testing.
