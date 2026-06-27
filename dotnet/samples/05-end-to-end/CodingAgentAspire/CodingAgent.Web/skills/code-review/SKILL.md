---
name: code-review
description: A checklist and method for reviewing code changes for correctness, safety, and quality. Load this when asked to review code or a diff.
---

# Code Review

Work through this checklist when reviewing code. Read the relevant files first; do not review blind.

## Correctness
- Does it do what was asked, including edge cases (null/empty, boundaries, concurrency, timeouts)?
- Any off-by-one, wrong operator, or inverted condition?

## Tests
- Is new behavior covered by tests? Does a bug fix include a test that would have caught it?
- Run the tests in the sandbox (`run_shell`) rather than assuming they pass.

## Security
- Untrusted input validated/escaped? Any injection (SQL, shell, path)?
- No secrets hardcoded or logged. Auth/permission checks not weakened.

## Quality & consistency
- Matches existing style, naming, and patterns in the file.
- Errors handled explicitly (no empty catch, no silent failure).
- No dead code, debug prints, or unrelated changes.

## Report format
For each finding: `path:line — issue — suggested fix`. Lead with the most important issues.
