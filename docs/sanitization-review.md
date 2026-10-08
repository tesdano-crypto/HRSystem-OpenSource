# Sanitized source review

## Scope and isolation

This is an independent source snapshot without upstream Git history. No original
repository files, deployed application, existing database, service, task, tunnel,
DNS configuration or remote repository was changed. Source integrity was checked
against a private SHA-256 manifest: 894 files matched. That private manifest and
original deployment identities are deliberately not included here.

The snapshot includes the existing, uncommitted holiday-training Phase 1 sources;
it is not represented as an exact snapshot of an upstream committed release.
No production database or export was used. No symlinks or reparse points were copied.
Build outputs are disposable and are not part of the publication content.

## Inventory and disposition

The initial inventory contains paths/categories/counts only. Its matches were
candidates, not a count of confirmed secrets. The candidate review also contains
only paths/counts; raw candidate values are not retained in this repository.

| Category | Disposition |
| --- | --- |
| A: company/personal | Company branding generalized; original employee prefixes replaced with fictional EMP examples; employee-specific insurance scenario names, amounts and applicable dates generalized. Operational narratives and reports excluded or rewritten. |
| B: infrastructure | Host/user/domain/IP literals removed or replaced with examples. SQL test safety checks use generic forbidden names plus strict local disposable database validation. Original User Secrets identity replaced with an independent identity. |
| C: fictional test/example | Reserved example email domains, dummy phone/address strings, synthetic LINE IDs/tokens, in-memory test credentials, generated GUIDs and explicit fingerprint test vectors retained as fixtures. These do not configure an external connection or live account. |
| D: technical docs | Architecture, formulas, immutable ledger/audit rules and generic installation guidance retained. Historical deployment success records removed. |
| E: third party | Framework/library names and official calendar/insurance reference links retained. Public calendar manifest hashes and hashes of explicit synthetic golden vectors are not runtime deployment fingerprints. |

## Removed capability

The attendance-report file import has been removed from domain models, services,
persistence configuration, DbContext, migrations/Designer metadata/snapshot, DI,
authorization, UI/menu, test classes and script allowlists. The removal list names
old paths for review only; those names are not live functionality. Manual attendance
adjustments and raw attendance evidence remain separate supported capabilities.

Independent LINE private pairing, approval callbacks, notifications and token
validation remain. They are disabled/unconfigured by default and need the user's
own secure bridge configuration. No live binding/channel data is supplied.

The entire operational .ai directory was excluded, including reports. No raw CSV,
Excel, database backup, runtime log or production verification artifact was copied.

## Migration decision

Use option A: retain 38 sanitized migrations, excluding the removed import-only
migration and its entity metadata. Retaining the chain preserves custom SQL,
sequences, seed definitions and regression coverage. A fresh consolidated baseline
would require re-implementing and separately validating those details.

This fork supports an EMPTY database. It is not an upgrade or rollback path for
an existing installation. Training grants/allocations and legacy-pool migration
remain because they are part of the general comp-time model. Expiry execution and
payroll conversion are not implemented. No existing database was migrated here.

## Configuration

Organization:Name drives UI titles and payslip presentation (Example Company is
the default). User Secrets uses HRSystem-OpenSource-Isolated. appsettings.example.json
contains placeholders only. Supply local SQL connection/admin credentials yourself.
BioWebTA:Server must be supplied explicitly; scheduled imports default to false.
ApprovalLineBridge defaults to disabled. Never reuse an existing deployment's store.

## Validation status

Validated on 2026-10-08:

- Restore: PASS; full solution Release build: PASS (0 warnings, 0 errors).
- Unit tests: 1,136 passed, 0 failed, 0 skipped.
- Integration tests: 574 passed, 0 failed, 0 skipped.
- Focused SQL regression after generic safety-name cleanup: 13 passed,
  0 failed, 0 skipped (subset, not an additional unique-test count).
- EF pending-model check: no changes since the last migration (exit 0).
- Gitleaks 8.30.0, official Windows x64 archive checksum verified:
  default rules, redacted output, no source allowlist: PASS, no leaks.
- Company/legacy employee prefix/private IP/original host/account/database
  identifier/national-ID candidate scans: 0 remaining matches.
- Email/phone/LINE/name candidates: reviewed as explicit synthetic fixtures,
  reserved example domains, UI labels or third-party metadata; no unresolved
  personal-data candidate. Candidate values are not stored in the reports.
- Feature residue: 0 live source/config/test references; old file names occur
  only in the inventory and removed-path manifest.
- Source-only files: UTF-8 text; no raw export/database/backup/log files;
  no reparse points; whitespace/conflict-marker checks PASS.
- Original source manifest: 894/894 files unchanged. Original uncommitted work
  was preserved. No production connectivity or deployment action was performed.

No sanitization BLOCKER remains within the inspected snapshot. Scans detect
configured patterns and reviewed candidates; they are not a guarantee against
all unknown forms of sensitive information. Git initialization is allowed locally
only after this gate. Publication remains a separate owner decision.

## Remaining publication decisions

- Project license: Apache-2.0 selected by the owner; see ../LICENSE and ../NOTICE.
  Third-party redistribution requirements remain subject to their own terms.
- REVIEW: each adopter must supply and review its own secrets, database, deployment
  policy, calendar data and payroll/insurance policies. Examples are not legal advice.
- No GitHub repository creation, remote configuration or push is authorized here.
