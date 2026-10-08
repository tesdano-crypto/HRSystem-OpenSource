# HRSystem Agent Navigation

This file is the short entry point for AI-assisted work in this repository.
It does not replace the project guidance or approval gates.

## Start here

1. Read `docs/AI-WORKING-GUIDE.md`.
2. Use `docs/architecture/module-map.md` to identify the smallest relevant module.
3. Confirm repository root, branch, HEAD, working tree, staged files, and
   `git diff --check` before making changes.
4. Follow the task's explicit allowed paths, forbidden actions, validation,
   and stop conditions.

## Default search scope

- Start with the requested page, service, domain type, or test class.
- Use scoped `rg` searches and explicit paths.
- Do not begin by scanning or dumping the entire repository.
- Read only the relevant project and module, then follow actual constructor,
  interface, persistence, and test dependencies.
- Expand to another module only when a concrete dependency or failure requires it.
- Do not inspect `HrIntranet/` or `HrIntranet.slnx` unless the task explicitly
  places HrIntranet in scope. Use `rg --no-ignore <pattern> HrIntranet` when an
  authorized HrIntranet task must override `.rgignore`.

## Context exclusions

- Do not bulk-read `.ai/reports` during ordinary product or tooling work.
- Read historical reports only for AIOS/report tasks, historical investigation,
  or regression root-cause analysis.
- Do not batch-read migration Designer files or the complete model snapshot.
- For migration/schema work, read the migration `.cs` first. Read its Designer
  or `HRSystemDbContextModelSnapshot.cs` only when model metadata is required.
- Generated and runtime folders such as `bin`, `obj`, `.artifacts`,
  `TestResults`, `coverage`, `publish`, `Published`, `Staging`, `Backups`, and
  `logs` must not become normal task context.

## Validation

- Run focused tests first using the module script documented in the module map.
- Do not substitute a full regression for targeted validation.
- Run full regression only once at the final gate when the risk level or task
  explicitly requires it.
- Database, migration application, publish, deployment, scheduler, external
  BioWebTA, and `<RUNTIME_DATA_DIRECTORY>` actions always require explicit authorization.

## Working tree safety

- Preserve unrelated and pre-existing changes.
- Do not use reset, clean, broad restore, or broad stash without authorization.
- Do not include secrets, credentials, personal data, runtime output, or
  generated artifacts in commits or reports.
