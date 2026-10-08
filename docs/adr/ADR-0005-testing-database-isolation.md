# ADR-0005: Test database isolation

Status: Accepted

Tests must not use an existing application database or development secrets.
Web and unit tests use isolated InMemory providers. SQL-specific integration
fixtures generate unique local disposable database names, validate their prefix
(and suffix where applicable), require local integrated authentication, and drop
only their own test databases in cleanup. They apply the sanitized migration chain
from an empty database and exercise rollback, indexes and transaction behavior.
Do not configure test runs with production credentials or external data sources.
