# Adaminator AI Project Rules

These rules apply to Claude, Codex and other AI coding assistants working on this repository.

## 1. Requirements discipline

- Implement only documented requirements.
- Do not invent features.
- Do not silently change business rules.
- Ask for clarification when documents conflict or leave an important case undefined.
- Prefer the simplest implementation that satisfies the current specification.

## 2. Scope discipline

Adaminator is a test project.

Do not add:

- multiple-user administration;
- player profiles;
- teams;
- Swiss;
- Round Robin;
- statistics;
- exports;
- Grand Final Reset;
- result history;
- unrelated infrastructure.

## 3. Domain rules

- Matches are the source of truth.
- The bracket is a projection of matches and match relationships.
- Business rules belong in backend/domain logic, not only in React.
- Participant management is locked after tournament start.
- Bracket placement is editable only before tournament start.
- No game or set may end in a draw.
- Partial match results are allowed.
- Only the latest valid completed match may be undone.
- Double Elimination has no Grand Final Reset.
- Third Place Match exists only for Single Elimination.

## 4. Change safety

- Update or add tests whenever business behavior changes.
- Do not change database/domain structure without explaining the reason.
- Preserve backward compatibility unless the task explicitly allows breaking changes.
- Avoid broad refactors during feature work.
- Keep changes small and reviewable.

## 5. Coding behavior

- Favor clear names and readable code.
- Avoid premature abstraction.
- Avoid duplicated business logic.
- Keep UI components focused.
- Keep domain calculations independent from UI rendering.
- Validate important rules on the backend.
- Return useful validation errors.

## 6. Testing priorities

Always prioritize tests for:

- bracket generation;
- bye calculation;
- manual bye selection validation;
- Single Elimination advancement;
- Double Elimination routing;
- Match Format validation;
- detailed score validation;
- partial results;
- forfeit;
- undo restrictions;
- tournament start validation.

## 7. Working method

Before implementing a feature:

1. Read the relevant specification files.
2. Summarize the intended behavior.
3. Identify ambiguous cases.
4. Ask questions if required.
5. Implement the smallest coherent change.
6. Run or add tests.
7. Report what changed and any remaining assumptions.
