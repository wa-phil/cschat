Here’s the short, pragmatic version—the 20% you need to do to get 80% of the value.

### The core idea (why “spec-driven” works) ###

Think of a spec as a reviewable, testable to-do list that locks down intent before code changes. It should be:
 * Close to code (live in the repo, co-located with the module),
 * Executable where possible (tests/contracts),
 * Small in scope (one behavior, one PR).

If you do only three things, do these:
 1. Write a one-page spec per change.
 2. Turn that spec into tests/contracts first.
 3. Generate/modify code only to satisfy the tests (and update the spec when reality differs).

### what goes in the 1-page spec (the “80/20” fields) ###

Put this in /specs/<area>/<feature>.md (or beside the code it touches):
 * Problem & intent (2–3 sentences): why this change exists.
 * Interfaces & data shapes: function signatures, request/response, noteworthy enums/errors. (Link to OpenAPI/JSON Schema if applicable.)
 * Examples (Given/When/Then): 3–5 realistic examples, including an edge case and a failure.
 * Invariants & non-functionals: what must always hold (idempotency, perf budget, memory cap, latency SLO, backward compat rules).
 * Done definition: measurable checks—tests pass, perf budget met, telemetry added, docs touched, migration note written.

That’s it. Keep it under ~60 lines.

### how it works with Copilot CLI (conceptually) ###

You’ll use the CLI as a context-aware assistant that:
 * Reads the code you point at,
 * Drafts a spec from the code (or vice-versa),
 * Generates tests that encode the spec,
 * Proposes patches to satisfy those tests,
 * Helps you explain diffs, summarize PRs, and iterate.

A tight loop looks like this:
 1. Scope & branch
     * Create a short spec stub + feature branch.
 2. Harvest current behavior
     * Ask Copilot CLI to explain the target files so you’re not guessing about legacy behavior.
     * Use that explanation to refine your Examples and Invariants sections.
3. Make it executable
     * Have Copilot draft unit/acceptance tests from the spec’s examples.
     * If you expose APIs, generate/update OpenAPI/JSON Schema or contract tests (consumer/provider).
4. Drive the code from tests
     * Ask Copilot to propose surgical patches (small diffs tied to each example).
     * Run tests; iterate until green.
5. PR with traceability
     * Generate a PR body from the spec’s “Problem, Examples, Invariants, Done” so reviewers see intent → tests → code lined up.

Tip: keep each loop tiny (one behavior). Your spec, tests, and patch should all fit comfortably in one review.

### what you’d actually do (a minimal recipe) ###

* Add a SPEC_TEMPLATE.md (below) and a /specs/ folder.
* For each change:
  1. Create specs/auth/refresh-tokens.md with the template.
  2. Use Copilot CLI to summarize the current module and fill the spec’s Interfaces/Examples.
  3. Ask Copilot to generate tests from Examples (xUnit/NUnit/Jest, etc.).
  4. Ask Copilot to propose diff(s) to make those tests pass.
  5. Run locally; keep diffs small; commit.
  6. Ask Copilot to draft the PR description from the spec + test names.

### what you gain vs your current Copilot workflow ###
 * Reviewable intent: reviewers see why before how; fewer “bike-shed” PRs.
 * Better codegen: Copilot produces tighter diffs when fed a spec + examples + interfaces, instead of a vague inline prompt.
 * Fewer regressions: executable specs (tests/contracts) gate changes and catch drift early.
 * Traceability: spec ↔ tests ↔ code ↔ PR all link; great for audits and onboarding.
 * Non-functional discipline: perf/error/compat rules are explicit, not tribal knowledge.

### pitfalls (and guardrails) ###
 * Spec rot: keep specs tiny and tied to one PR; delete or merge them into module docs once shipped.
 * Over-specifying: describe observable behaviors, not private implementation.
 * Unverifiable claims: every statement in “Done” must be checkable in CI (tests, perf checks, linters).
 * Big bang changes: slice by behavior; one spec per slice.

 bonus: small habits that 10× results

### Golden tests for tricky formatters/serializers (approval tests). ###
 * Property-based tests for invariants (e.g., parsing/round-trip).
 * Contract tests at boundaries (HTTP, message bus).
 * “Unknowns” box in the spec to surface risks/assumptions earl