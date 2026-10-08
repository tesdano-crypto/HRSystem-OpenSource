# Security policy

## Release status and reporting

There is no supported public release or configured private reporting endpoint yet.
Before publication, the maintainer must establish a private security contact or
repository private vulnerability reporting channel and document it here.
Do not post vulnerabilities containing secrets or employee data in public issues.
Use only fictional records and a sanitized reproduction when reporting problems.
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
