# Security policy

## Release status and reporting

GitHub Private Vulnerability Reporting is enabled for this repository.
Report security vulnerabilities privately through GitHub **Security Advisories**
using **Report a vulnerability**:
[Submit a private vulnerability report](https://github.com/tesdano-crypto/HRSystem-OpenSource/security/advisories/new).

Do not use public issues to report sensitive security vulnerabilities.
Do not include real employee data, credentials or production secrets in reports
or attachments. Use fictional test data and a sanitized reproduction instead.
No response-time or support commitment is implied by this preparation snapshot.

## Deployment security

Use HTTPS, least-privilege application/database identities, protected configuration,
reviewed authorization and audited administrator actions. Development User Secrets
is not encrypted storage and must not be reused as a production secret vault.
Use a separate migration identity where possible. Never commit credentials, database
backups, exports, employee data, Data Protection keys or runtime logs.

The application uses confidential HR information; review access, retention, backups,
restore testing and applicable obligations before production use. LINE and external
attendance integrations are optional and disabled/unconfigured by default.
SQL tests use only validated disposable local databases, never a live installation.

See [deployment setup](README.md#generic-deployment-guidance) and the
[dependency review](docs/dependency-license-review.md). A successful secret scan
is not a guarantee that every possible sensitive value has been detected.
