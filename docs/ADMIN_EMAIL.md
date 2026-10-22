# Admin profile reminder email

Admins can select incomplete member accounts in the Members list and send a fixed
profile completion reminder. The send action rechecks that each account is active
and still incomplete, then retrieves its verified email from Firebase Authentication
and sends it through the configured SMTP server. Addresses are not returned to the
browser or stored in VeganAdda's database. A recipient without a verified Firebase
email is skipped.

Configure these environment variables on the `web` and `web_standby` services:

| Variable | Purpose |
| --- | --- |
| `EMAIL_SMTP_HOST` | SMTP server host |
| `EMAIL_SMTP_PORT` | SMTP submission port; defaults to `587` (STARTTLS) |
| `EMAIL_SMTP_USERNAME` | SMTP username, if required |
| `EMAIL_SMTP_PASSWORD` | SMTP password, if required; keep it out of Git |
| `EMAIL_FROM_ADDRESS` | Valid sender address |
| `EMAIL_FROM_NAME` | Sender display name; defaults to `VeganAdda` |
| `EMAIL_APP_URL` | HTTPS public app origin used for the profile link |

The admin screen enables the send control only when the sender, SMTP host and
HTTPS app URL are configured. Username and password must either both be set or
both be empty for an unauthenticated relay. The endpoint accepts at most 100
selected accounts per request and reports sent, skipped, and failed counts.
