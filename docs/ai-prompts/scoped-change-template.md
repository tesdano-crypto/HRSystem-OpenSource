# Scoped Change Template

Copy this template and fill only task-specific facts. Link to repository guides instead of repeating their full contents.

```text
# Objective
<one measurable outcome>

# Repository baseline
Root: <REPOSITORY_ROOT>
Expected branch: <branch>
Expected HEAD: <sha or "discover">
Risk level: <1 | 2 | 3>

# Allowed scope
- <exact files, directories, modules, or operations>

# Explicitly excluded
- <nearby behavior or paths that must not change>
- Database/Migration: <not authorized | separately authorized gate>
- Publish/Deploy/Runtime: <not authorized | separately authorized gate>
- External BioWebTA and <RUNTIME_DATA_DIRECTORY>: <not authorized unless explicitly stated>

# Required discovery
- Read: <small starting set>
- Verify: <specific dependency or current behavior>
- Use module map: docs/architecture/module-map.md

# Acceptance criteria
1. <observable result>
2. <edge case>
3. No unrelated behavior change.

# Validation
- Solution filter: <file.slnf>
- Targeted script: <scripts/test-*.ps1>
- Additional exact tests: <if any>
- Full regression: <required once | not required, with Level 1 reason>
- git diff --check

# Git authorization
- Stage/commit: <no | yes, exact message>
- Push/merge/rebase/tag: no unless separately authorized

# Stop conditions
- Baseline mismatch
- Unexpected changed path or secret/runtime artifact
- Test, schema, data fingerprint, or runtime condition outside approved scope

# Final report
- Changed files
- Validation counts/results
- Schema/data/runtime impact
- Commit SHA and working tree, if authorized
- Remaining gate or next approved step
```
