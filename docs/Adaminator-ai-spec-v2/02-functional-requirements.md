# Functional Requirements

## 1. Authentication

### FR-AUTH-001

The application shall provide basic protection for the admin area.

### FR-AUTH-002

The public tournament view shall not require authentication.

---

## 2. Tournament management

### Tournament fields

- Name
- Date
- Notes
- Tournament Type
- Default Match Format
- Third Place Match enabled flag, available only for Single Elimination
- Status

### Supported statuses

- Planned
- Running
- Finished

### FR-TOUR-001

The admin shall be able to create a tournament.

### FR-TOUR-002

The admin shall be able to edit a tournament while it is Planned.

### FR-TOUR-003

The admin shall be able to delete a tournament in any status.

### FR-TOUR-004

Every tournament deletion shall require confirmation.

### FR-TOUR-005

The tournament list shall display Planned, Running and Finished tournaments.

### FR-TOUR-006

Finished tournaments shall remain accessible.

### FR-TOUR-007

Third Place Match shall be configurable only for Single Elimination tournaments.

### FR-TOUR-008

Double Elimination tournaments shall not provide a separate Third Place Match.

---

## 3. Participant management

### Participant fields

- Name

### FR-PART-001

The admin shall be able to add participants while the tournament is Planned.

### FR-PART-002

The admin shall be able to rename and remove participants while the tournament is Planned and not yet started.

### FR-PART-003

The participant list shall contain between 2 and 32 participants before tournament start.

### FR-PART-004

Participant names shall be unique inside a tournament.

### FR-PART-005

Participants shall not have user accounts or reusable player profiles.

### FR-PART-006

Participants shall not be added, renamed or removed after tournament start.

---

## 4. Bracket generation and editing

### FR-BRACKET-001

The system shall generate an initial bracket using random participant distribution.

### FR-BRACKET-002

The admin shall be able to regenerate the bracket while the tournament is Planned.

### FR-BRACKET-003

The admin shall be able to manually edit the initial participant placement before tournament start.

### FR-BRACKET-004

Manual bracket editing shall affect only the initial bracket arrangement.

### FR-BRACKET-005

The admin shall select bye recipients directly on the bracket preview.

### FR-BRACKET-006

The system shall require the exact number of bye recipients needed for the bracket size.

### FR-BRACKET-007

The system shall validate the bracket before tournament start.

### FR-BRACKET-008

The bracket shall become locked when the tournament starts.

### FR-BRACKET-009

A Running tournament bracket shall change only through match results and advancement rules.

### FR-BRACKET-010

Matches shall be the source of truth. The bracket shall be a visualization derived from matches and their relationships.

---

## 5. Tournament start

### FR-START-001

The admin shall start a tournament from an approved bracket preview.

### FR-START-002

Starting the tournament shall:

- lock the participant list;
- lock the initial bracket arrangement;
- apply selected byes;
- create or finalize required matches;
- advance bye recipients automatically;
- change the tournament status to Running.

### FR-START-003

A tournament shall not start if bracket validation fails.

---

## 6. Match management

### Match fields

- Participant A
- Participant B
- Match Format
- Score Type
- Match Status
- Detailed score
- Calculated aggregate score
- Winner
- Forfeit flag

### Supported match statuses

- Pending
- In Progress
- Completed
- Forfeit

### FR-MATCH-001

A match shall be actionable only when both participants are known.

### FR-MATCH-002

A match with one or both participants unknown shall remain locked.

### FR-MATCH-003

The admin shall open a match editor by clicking the match card in the bracket.

### FR-MATCH-004

The tournament Default Match Format shall be inherited by each match.

### FR-MATCH-005

The admin shall be able to override Match Format per match.

### FR-MATCH-006

The admin shall be able to save a partial result.

### FR-MATCH-007

Saving a partial result shall set or retain the match status as In Progress.

### FR-MATCH-008

The system shall calculate the aggregate score from detailed game or set results.

### FR-MATCH-009

The system shall determine the winner automatically from valid detailed results.

### FR-MATCH-010

A completed match shall advance its winner automatically.

### FR-MATCH-011

A match shall not accept additional games after it has been decided.

---

## 7. Match formats

Supported Match Formats:

- BO1
- BO3
- BO5
- BO7

### Required wins

- BO1: 1
- BO3: 2
- BO5: 3
- BO7: 4

### FR-FORMAT-001

The selected Match Format shall determine the number of wins required.

### FR-FORMAT-002

The system shall reject a completed result if neither participant has reached the required number of wins.

### FR-FORMAT-003

The system shall reject more games than the selected Match Format permits.

---

## 8. Score types

Supported Score Types:

- Winner Only
- Games
- Points
- Sets

### FR-SCORE-001

Winner Only shall be valid only with BO1.

### FR-SCORE-002

Games shall support BO1, BO3, BO5 and BO7.

### FR-SCORE-003

Sets shall support BO-based formats.

### FR-SCORE-004

Points shall store detailed point values for each game or scoring unit.

### FR-SCORE-005

No detailed game or set may end in a draw.

### FR-SCORE-006

Each detailed score entry shall identify a winner.

---

## 9. Forfeit

### FR-FORFEIT-001

The admin shall be able to complete a match as a forfeit.

### FR-FORFEIT-002

A forfeit shall require selecting the winner.

### FR-FORFEIT-003

A forfeit shall not require a reason or note.

### FR-FORFEIT-004

The selected winner shall advance normally.

---

## 10. Undo

### FR-UNDO-001

Only the chronologically latest completed match shall be undoable.

### FR-UNDO-002

A match shall not be undoable if the advancing participant's next match has already started.

### FR-UNDO-003

Undo shall remove the winner from the dependent next match.

### FR-UNDO-004

Undo shall restore the affected match to Pending or In Progress according to its remaining saved score.

### FR-UNDO-005

The application shall not maintain a general result edit history in the MVP.

---

## 11. Public view

### FR-PUBLIC-001

Each tournament shall have a public read-only view.

### FR-PUBLIC-002

The public view may display:

- tournament name;
- date;
- notes;
- tournament type;
- tournament status;
- participant list;
- complete bracket;
- match formats;
- aggregate scores;
- detailed scores;
- forfeit indicators;
- final placements.

### FR-PUBLIC-003

The public view shall not expose editing actions.

---

## 12. Dashboard

### FR-DASH-001

The dashboard shall provide a Create Tournament action.

### FR-DASH-002

The dashboard shall group or clearly distinguish Running, Planned and Finished tournaments.

### FR-DASH-003

The admin shall be able to open any historical tournament from the dashboard.
