# Pre-publish finalization

Reviewed 2026-10-08. Scope: documentation, repository hygiene and read-only package
license review. No business code, package version, migration or production changes.
Apache-2.0 and the first local source commit were subsequently authorized by the
owner. No remote creation or push is authorized.

## README readiness

Overview, features, dependency direction, SDK/SQL prerequisites, local restore/build,
secret configuration, empty-database migration, tests, security and generic deployment
are documented. LINE is explicitly optional/disabled by default. No default password,
external integration enablement or deployment destination is supplied. Startup admin
requirements and development-only sample data are documented from current code.

## Dependency/license outcome

See dependency-license-review.md and dependency-license-metadata.json for all 100
resolved package versions and exact metadata (plus the separately declared EF tool).
90 MIT expressions; 8 Apache-2.0 expressions; one Microsoft SNI custom license file;
one legacy URL-only xUnit abstraction package. No package was upgraded or replaced.

The SNI native binary must not be described as MIT or bundled without reviewing its
object-code distribution conditions. The URL-only test dependency remains REVIEW
for immutable version-specific license evidence; current upstream text alone is not
proof for every historical version. Dependency references are not binary vendoring.
Recommendation: Apache-2.0 for permissive business adoption with explicit patent
terms; MIT is simpler; GPL-3.0 needs a deliberate copyleft decision and SNI review.
The owner selected Apache-2.0 in the follow-up authorization; third-party terms remain.

## Repository hygiene

- .gitignore covers build/test outputs, local secrets, private keys, SQL storage,
  exports, operational reports and logs; configuration examples stay visible.
- Root SECURITY.md is canonical; docs/SECURITY.md links to it. A real private
  disclosure channel must be established before public release; none was invented.
- CONTRIBUTING.md documents safe fixtures, scoped tests and dependency review.
  Contribution guidance follows Apache-2.0 section 5.
- LICENSE contains the official Apache-2.0 text; NOTICE records project and calendar
  attribution. No third-party package is relicensed.
- CI is recommended as a later task. Use pinned SDK/tool/action versions, minimal
  read permissions, separate build/unit and isolated Windows SQL Express integration
  jobs, and Gitleaks/sanitization checks. Provision disposable SQL explicitly; do not
  assume a hosted runner supplies the required instance. Do not give PR jobs production
  secrets, import credentials or deployment access. Publish only reviewed test artifacts.
  No CI workflow or cloud resource was created in this phase.

## Verification

- dotnet restore: PASS.
- Full solution Release build: PASS, 0 warnings, 0 errors.
- scripts/test-full.ps1 -NoBuild: PASS; Unit 1,136 / Integration 574,
  total 1,710, zero failed or skipped. SQL tests used isolated local fixtures.
- Final Gitleaks 8.30.0: PASS, no leaks; scanned the source-only folder after
  removing this run's build/test outputs (approximately 11.03 MB).
- Sanitization: 726 source files scanned, zero company/private infrastructure/
  personal-ID/private-key/operational-artifact blockers. Reviewed email/phone/LINE
  candidates remain fictional fixtures or Razor syntax; URLs are local/example or
  official third-party references. No raw candidate values were added to reports.
- Whitespace/conflict-marker checks and .gitignore positive/negative checks: PASS.
- Original source integrity: 894/894 SHA-256 values unchanged.
- Pre-commit baseline: codex/open-source, zero commits and remotes. The owner
  authorized the first local source commit after license finalization. No GitHub
  repository creation or push is part of that authorization.
- Original application, database, deployment and Git history are outside this task.

## Readiness and blockers

Technical readiness for a FIRST LOCAL SOURCE COMMIT does not imply public licensing
or binary distribution clearance. The validated technical
state is READY FOR FIRST OPEN SOURCE COMMIT; the first local commit is authorized.

PROJECT LICENSE: Apache-2.0 selected and added under explicit owner authorization.
PUBLIC RELEASE SETUP: establish the private security reporting channel.
BINARY RELEASE REVIEW: resolve SNI distribution obligations, collect applicable
third-party notices from the actual publish output and review any redistributed tools.
The source snapshot does not include those binaries. Calendar attribution is retained;
check any source-attachment-specific reuse exception before a data release.

No unqualified claim that all dependencies are permissively licensed is made.
