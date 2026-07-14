# Functional Requirements

## Tournament

Fields:
- Name
- Date
- Notes
- Type (Single / Double Elimination)
- Default Match Format (BO1/BO3/BO5/BO7)
- Third Place Match (Single Elimination only)

States:
- Planned
- Running
- Finished

## Participants

- Name only
- Maximum 32
- Random seeding
- Random bye assignment
- Cannot be modified after start

## Brackets

### Single Elimination
- Auto generation
- Auto advancement
- Optional Third Place Match

### Double Elimination
- Winner Bracket
- Loser Bracket
- Grand Final
- No Grand Final Reset

## Matches

Each match supports:
- Match Format (inherits tournament default, override allowed)
- Score Type
- Score
- Winner
- Forfeit

## Score Types

- Winner Only
- Games
- Points
- Sets

## Undo

Undo the last completed match and restore bracket state.

## Public View

Read-only public page showing the current bracket.
