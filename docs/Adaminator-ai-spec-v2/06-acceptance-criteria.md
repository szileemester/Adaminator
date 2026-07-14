# Acceptance Criteria

## Tournament creation

### AC-TOUR-001

Given the admin is authenticated  
When a valid tournament form is submitted  
Then a Planned tournament is created.

### AC-TOUR-002

Given Double Elimination is selected  
Then the Third Place Match option is unavailable.

### AC-TOUR-003

Given any tournament status  
When the admin confirms deletion  
Then the tournament is deleted.

---

## Participants

### AC-PART-001

Given a Planned tournament  
When the admin adds a unique participant name  
Then the participant appears in the list.

### AC-PART-002

Given a participant name already exists in the tournament  
When the same name is submitted again  
Then the operation is rejected with a validation message.

### AC-PART-003

Given the tournament has 32 participants  
When another participant is added  
Then the operation is rejected.

### AC-PART-004

Given the tournament is Running  
Then participant create, rename and delete actions are unavailable and rejected by the backend.

---

## Bracket preview

### AC-BRACKET-001

Given 13 valid participants  
When the bracket is generated  
Then the system creates a 16-slot bracket preview and requires exactly 3 byes.

### AC-BRACKET-002

Given a bracket preview exists  
When the admin regenerates it  
Then a new random initial distribution replaces the previous preview.

### AC-BRACKET-003

Given the bracket is Planned  
When the admin manually rearranges participants  
Then each participant can still appear exactly once.

### AC-BRACKET-004

Given the required bye count is 3  
When only 2 bye recipients are selected  
Then tournament start is rejected.

### AC-BRACKET-005

Given a valid preview  
When the tournament starts  
Then participant placement and bye selection become locked.

---

## Match availability

### AC-MATCH-001

Given a match has two known participants  
Then it can be opened for result entry.

### AC-MATCH-002

Given at least one participant is unresolved  
Then the match cannot be started or edited.

---

## Match format and score

### AC-SCORE-001

Given a BO3 match  
When one participant has two game wins  
Then the match may be completed.

### AC-SCORE-002

Given a BO3 match at 1–1  
When the admin saves  
Then the match is saved as In Progress.

### AC-SCORE-003

Given a BO3 match at 1–1  
When the admin attempts to complete it  
Then the operation is rejected.

### AC-SCORE-004

Given a detailed score of 10–10  
When it is submitted  
Then the operation is rejected because draws are not allowed.

### AC-SCORE-005

Given a BO3 match already has a winner  
When another game is added  
Then the operation is rejected.

### AC-SCORE-006

Given Winner Only Score Type  
When Match Format is BO3  
Then the configuration is rejected.

### AC-SCORE-007

Given valid detailed scores  
Then aggregate score and winner are calculated automatically.

---

## Match Format override

### AC-FORMAT-001

Given a tournament default of BO3  
When a match is created  
Then the match uses BO3 by default.

### AC-FORMAT-002

Given a match inherits BO3  
When the admin overrides it to BO5  
Then BO5 validation rules apply only to that match.

---

## Forfeit

### AC-FORFEIT-001

Given an available match  
When the admin chooses Forfeit and selects a winner  
Then the match is completed as Forfeit and the winner advances.

### AC-FORFEIT-002

Given Forfeit is selected without a winner  
Then the operation is rejected.

---

## Undo

### AC-UNDO-001

Given Match B is the latest completed match  
And its dependent match has not started  
When Undo is confirmed  
Then Match B is restored and its advancement is removed.

### AC-UNDO-002

Given the winner of the latest match has started the next match  
When Undo is requested  
Then the operation is rejected.

### AC-UNDO-003

Given a completed match is not the chronologically latest completed match  
Then Undo is unavailable.

---

## Public view

### AC-PUBLIC-001

Given a valid public tournament page  
When an unauthenticated visitor opens it  
Then all public tournament and bracket information is visible.

### AC-PUBLIC-002

Given the public view  
Then no admin editing actions are displayed.

---

## Tournament history

### AC-HISTORY-001

Given a tournament is Finished  
Then it remains listed and can be opened later.
