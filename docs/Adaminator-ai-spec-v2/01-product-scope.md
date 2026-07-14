# Product Scope

## Product name

**Adaminator**

## Purpose

Adaminator is a lightweight web application for creating, running and viewing small esports tournaments.

The application focuses on bracket-based tournaments and simple event management. It is intended as a test project and should remain deliberately limited in scope.

## Primary user

One administrator.

The administrator creates tournaments, adds participants, generates and edits brackets, records results and manages tournament progress.

## Public users

Public users do not authenticate.

They can open a read-only public tournament page and view all tournament information.

## Supported tournament types

- Single Elimination
- Double Elimination

## Maximum participants

- Minimum: 2
- Maximum: 32

## Included in scope

- Basic admin protection
- Tournament creation, update and deletion
- Planned, Running and Finished tournament states
- Participant management before tournament start
- Random initial seeding
- Editable pre-start bracket
- Manual bye selection from bracket preview
- Single Elimination
- Optional Third Place Match for Single Elimination
- Double Elimination
- Winner Bracket
- Loser Bracket
- Grand Final
- No Grand Final Reset
- Tournament-level default Match Format
- Per-match Match Format override
- Multiple Score Types
- Detailed game or set score storage
- Partial score saving
- Forfeit
- Undo of the latest completed match
- Public read-only tournament view
- Historical finished tournament list

## Explicitly out of scope

- Multiple admin users
- Player accounts
- Persistent player profiles
- Teams
- Swiss format
- Round Robin format
- Statistics
- Export to PDF, image or spreadsheet
- Grand Final Reset
- Result edit history
- Arbitrary bracket editing after tournament start
- Judge management
- Match notes
