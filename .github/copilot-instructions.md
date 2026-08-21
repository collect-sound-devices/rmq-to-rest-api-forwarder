# Copilot Instructions

Purpose
- Short guidance for Copilot and human reviewers: prefer minimal, practical changes.

Principles
- KISS: keep solutions simple and easy to understand.
- YAGNI: don't add functionality until it's needed.

Migrations (quick checklist)
- State intent and data impact in the PR description.
- Prefer backward-compatible changes; use feature flags for phased rollouts.
- Include a data-migration plan, rollback steps, and smoke-test instructions.

PR expectations
- Small, single-responsibility PRs with clear commit messages and linked issue/decision.
- Include tests (unit/integration) and update docs when behavior changes.
- Call out performance, security, and API compatibility impacts.

Copilot prompt (template)
- "Change: <goal>. Scope: <files/dirs>. Constraints: <KISS/YAGNI, compat, tests>." 

Owner
- Add an explicit, machine-friendly owner line here and consider adding a .github/CODEOWNERS file to enforce review ownership.

Owner (machine-friendly example)
- Owner: "@org/team"  # or Owner: "Name <email@example.com>"