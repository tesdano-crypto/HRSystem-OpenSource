# Change Risk Levels and Validation Gates

Classify a task before editing. Use the lowest level that accurately represents every changed path; a higher-risk dependency raises the whole task.

| Level | Typical scope | Required validation | Migration / deployment gate |
|---|---|---|---|
| Level 1 | Razor text, styling, local binding, documentation, or tooling with no business-rule or persistence effect | affected component tests or exact targeted test classes; build the relevant `.slnf`; `git diff --check` | none unless deployment is requested separately |
| Level 2 | one module's domain/application behavior, authorization, transaction behavior, or query semantics | module Unit and Integration/Web tests through the scoped script; relevant `.slnf` restore/build; full regression once before final commit/release | no database action unless schema changes; deployment remains a separate approval |
| Level 3 | migration, DbContext/model snapshot, shared security or Identity policy, cross-module transaction, external source integration, formal data, or release deployment | targeted tests, all affected module tests, database/disposable tests, full solution restore/build, and one final full regression | mandatory human-approved Migration Gate and/or Deployment Gate with backup, fingerprints, stop conditions, and rollback plan |

## Execution rules

- Do not rerun full regression after every edit. Run fast targeted tests while iterating, then the required broader gate once the change is stable.
- A failing targeted test expands investigation only to its concrete dependency; it does not authorize unrelated fixes.
- Solution-filter build success is not a substitute for tests.
- Test filters are explicit class allowlists in `scripts/test-*.ps1`. Update the relevant allowlist when adding a test class to a module.
- A formal database, external BioWebTA, runtime, scheduler, publish, or deployment action is never implied by a Level 3 classification. It still needs explicit authorization.
- Tests that require SQL Server must use disposable databases and verify cleanup. Never point repeatable tests at `HRSystemDb`.

## Minimum completion evidence

Every task report should state the risk level, changed paths, command results, whether schema/data/runtime changed, and whether a separate gate remains. A commit is allowed only when the user authorized it and the staged set matches the approved scope.
