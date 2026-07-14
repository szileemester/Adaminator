# Non-functional Requirements

## Performance

- A bracket for up to 32 participants should generate within 1 second under normal local or small-hosted deployment conditions.
- A normal match save should complete within 500 ms under normal conditions.
- The public bracket page should remain usable for a 32-participant tournament.

These are target values, not hard real-time guarantees.

## Browser support

Target current versions of:

- Google Chrome
- Microsoft Edge
- Mozilla Firefox

## Responsive behavior

- Admin features should work on desktop and tablet widths.
- Public view should be usable on mobile.
- Large brackets may use horizontal scrolling and zooming.

## Reliability

- Business validation must run on the backend.
- UI validation alone is insufficient.
- Match advancement must be atomic from the user's perspective.
- A failed save must not partially advance the bracket.

## Data integrity

- A participant may appear only once in the initial bracket.
- A match winner must be one of the match participants.
- Detailed score, aggregate score and winner must remain consistent.
- Dependent match slots must reflect upstream match results.
- Undo must restore a consistent match graph.

## Security

- Admin actions require authentication.
- Public pages are read-only.
- Public identifiers should not expose internal sequential IDs directly when avoidable.
- Secrets must not be committed to source control.

## Usability

- Destructive actions require confirmation.
- Validation messages should be understandable without technical knowledge.
- Match result entry should require minimal clicks.
- The bracket should clearly show progression.

## Maintainability

- Business rules should be isolated from presentation logic.
- Bracket behavior should be covered by automated tests.
- The match graph should be the authoritative domain structure.
- The implementation should favor readability over clever abstractions.

## Accessibility

- Interactive controls should be keyboard accessible.
- Important state differences should not rely on color alone.
- Dialogs should manage focus correctly.
- Text should maintain sufficient contrast in dark mode.
