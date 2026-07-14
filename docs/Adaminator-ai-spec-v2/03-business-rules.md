# Business Rules

## BR-001 — Tournament lifecycle

A tournament follows this lifecycle:

`Planned -> Running -> Finished`

A tournament can be deleted from any state.

## BR-002 — Planned tournament

While Planned:

- tournament settings may be edited;
- participants may be managed;
- bracket may be generated;
- bracket may be regenerated;
- initial placement may be edited;
- bye recipients may be selected.

## BR-003 — Running tournament

While Running:

- participants are locked;
- initial bracket placement is locked;
- bracket regeneration is forbidden;
- match results drive advancement.

## BR-004 — Finished tournament

A Finished tournament remains visible in history and in its public view.

## BR-005 — Single Elimination

A participant is eliminated after one match loss.

The final winner is the tournament champion.

## BR-006 — Third Place Match

A Third Place Match is optional and available only in Single Elimination.

Its participants are the two semifinal losers.

## BR-007 — Double Elimination

A participant is eliminated after two match losses.

The tournament includes:

- Winner Bracket;
- Loser Bracket;
- Grand Final.

There is no Grand Final Reset.

## BR-008 — Double Elimination placement

The Loser Bracket result determines third place. No separate bronze match exists.

## BR-009 — Random seeding

Initial participant distribution is random when a bracket is generated.

The admin may manually edit the resulting initial arrangement before start.

## BR-010 — Bracket regeneration

Regeneration creates a new random initial distribution and replaces the current preview.

Regeneration is allowed only while Planned.

## BR-011 — Bye count

The bracket size is the smallest power of two greater than or equal to the participant count.

Required bye count:

`Bracket size - Participant count`

Example:

- 13 participants
- bracket size 16
- 3 byes required

## BR-012 — Bye selection

The admin selects exactly the required number of bye recipients on the bracket preview.

A bye recipient has no first-round opponent and advances automatically.

## BR-013 — Match availability

A match may start only when both participant slots are resolved.

## BR-014 — Match format inheritance

Every match inherits the tournament Default Match Format unless explicitly overridden.

## BR-015 — Match format validity

Required match wins:

- BO1: 1
- BO3: 2
- BO5: 3
- BO7: 4

## BR-016 — No draws

No game or set may end in a draw.

## BR-017 — Partial result

A partial result may be saved before the match is decided.

The match remains In Progress.

## BR-018 — Match completion

A match is Completed when one participant reaches the required wins and all entered detailed scores are valid.

## BR-019 — Automatic winner

The winner is derived from detailed results and may not conflict with them.

## BR-020 — Forfeit

Forfeit completes the match without requiring detailed scores.

A winner must be selected.

No reason is stored.

## BR-021 — Automatic advancement

Completing a match automatically places the winner into the correct dependent match.

In Double Elimination, the loser is also routed to the correct Loser Bracket match when applicable.

## BR-022 — Undo restriction

Only the latest completed match may be undone.

Undo is forbidden after its winner has started a dependent next match.

## BR-023 — Match source of truth

The match graph and match relationships are authoritative.

The bracket is a visual projection of those matches.

## BR-024 — Unique participant names

Participant names are unique within one tournament.

## BR-025 — Destructive actions

Tournament deletion and other destructive actions require explicit confirmation.
