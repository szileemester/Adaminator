# UI / UX Guidelines

## General direction

- Clean and modern bracket-focused interface
- Dark mode by default
- Responsive layout
- Clear distinction between editable admin views and read-only public views

## Dashboard

The dashboard should contain:

- Create Tournament action
- Running tournaments
- Planned tournaments
- Finished tournaments

Each tournament card should display at least:

- Name
- Date
- Tournament Type
- Status
- Participant count

## Tournament creation

Use a single simple form rather than a multi-step wizard.

After creation, navigate to the tournament management page.

## Tournament management page

Suggested sections:

- Overview
- Participants
- Bracket Preview or Bracket
- Public View link
- Delete action

## Bracket preview

The preview should support:

- random initial placement;
- regeneration;
- manual initial participant rearrangement;
- bye selection directly on the preview;
- visible validation state;
- Start Tournament action.

## Live bracket

- Horizontal round-based layout
- Smooth horizontal scrolling
- Zoom support
- Compact layout for large brackets
- Comfortable layout for smaller brackets
- Winner visually highlighted
- Loser visually muted
- Pending and unresolved matches visually distinct
- Forfeit visibly marked
- Match Format visible where useful

## Match card

A match card should show:

- Participant A
- Participant B
- Aggregate score
- Status
- Winner highlight
- Optional Match Format indicator

## Match dialog

Opened by clicking a match card.

Suggested content:

- Round or match label
- Participant names
- Match Format
- Match Format override control
- Score Type
- Detailed score rows
- Calculated aggregate result
- Save partial result
- Complete match
- Forfeit action
- Undo action when allowed

## Public view

The public page may display all tournament information but must be read-only.

## Destructive actions

Every destructive action requires confirmation.

Examples:

- tournament deletion;
- bracket regeneration when a preview already exists;
- tournament start;
- undo;
- forfeit completion.

## Keyboard and dialogs

- Dialogs should close with Escape unless unsaved data would be lost.
- Unsaved changes should trigger confirmation.
- Primary actions should be keyboard accessible.

## Error feedback

Validation errors should be shown close to the related field or action.

Server errors should show a clear, non-technical message, for example:

> Unable to save the match. Please try again.
