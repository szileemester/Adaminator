# Domain Glossary

## Tournament

A single competitive event managed in Adaminator.

## Tournament Type

The elimination structure used by a tournament.

Values:

- Single Elimination
- Double Elimination

## Participant

A name-only competitor belonging to exactly one tournament.

A participant is not a reusable user or player profile.

## Match

The primary competitive unit between two participant slots.

A match is the source of truth for bracket progression.

## Match Slot

A participant position inside a match.

A slot may be unresolved until an earlier match completes.

## Match Format

The number of games or sets needed to win a match.

Values:

- BO1
- BO3
- BO5
- BO7

## Default Match Format

The tournament-level Match Format inherited by matches unless overridden.

## Score Type

The interpretation method for a match result.

Values:

- Winner Only
- Games
- Points
- Sets

## Detailed Score

The stored result of an individual game, set or scoring unit.

Example:

- Game 1: 13–8
- Game 2: 9–13
- Game 3: 13–11

## Aggregate Score

The match-level number of game or set wins calculated from detailed scores.

Example:

`2–1`

## Pending Match

A match that has not started.

It may be locked if one or both participants are unresolved.

## In Progress Match

A match with a saved partial result that is not yet decided.

## Completed Match

A normally finished match with a valid winner.

## Forfeit Match

A match completed by selecting a winner without requiring detailed score entry.

## Bracket

A visual representation of matches, rounds and advancement paths.

The bracket is not the primary data source.

## Round

A logical stage of a bracket containing one or more matches.

## Winner Bracket

The upper bracket in Double Elimination.

Participants enter it without losses.

## Loser Bracket

The lower bracket in Double Elimination.

Participants enter it after their first loss.

## Grand Final

The final match between the Winner Bracket winner and the Loser Bracket winner.

## Grand Final Reset

A second final match sometimes used in Double Elimination.

Adaminator does not support it.

## Third Place Match

An optional match between the semifinal losers in Single Elimination.

## Seed

The participant's initial placement value in a bracket.

Adaminator generates the initial distribution randomly.

## Bye

Automatic advancement from the first round without playing a match.

## Bracket Preview

The editable pre-start representation of the generated bracket.

## Undo

The reversal of the latest completed match result, subject to dependency restrictions.

## Public View

A read-only tournament page accessible without admin authentication.
