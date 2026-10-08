# Contributing

The project is licensed under Apache-2.0; see LICENSE. Contributions intentionally
submitted for inclusion are governed by section 5 of that license unless explicitly
stated otherwise. Submit only material you are entitled to contribute and preserve
third-party attribution. No separate CLA or DCO requirement is imposed here.

For authorized local work, read AGENTS.md and docs/AI-WORKING-GUIDE.md, keep changes
scoped to a module, and preserve the Domain/Application/Infrastructure/Web boundary.
Use fictional fixtures only. Never include employee data, credentials or deployment
identities. Report security issues according to SECURITY.md, not in public issues.

Run the relevant scripts/test-*.ps1 first, then the full build and required regression
before a release. SQL tests require isolated local SQL Express databases. Review
migration Up/Down behavior separately; tests do not authorize deployment or imports.
Describe the problem, behavior change and validation in a pull request. Record new
dependencies, their exact versions, license metadata and redistribution requirements.
Do not bundle package binaries, tool caches, test outputs or operational artifacts.
