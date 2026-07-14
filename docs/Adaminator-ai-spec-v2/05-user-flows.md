# User Flows

## Flow 1 — Create tournament

1. Admin opens the dashboard.
2. Admin selects Create Tournament.
3. Admin enters:
   - name;
   - date;
   - notes;
   - tournament type;
   - default Match Format;
   - optional Third Place Match for Single Elimination.
4. Admin saves the tournament.
5. System creates it in Planned status.
6. System opens the tournament management page.

## Flow 2 — Manage participants

1. Admin opens a Planned tournament.
2. Admin opens Participants.
3. Admin adds participants by name.
4. System prevents duplicate names.
5. Admin may rename or remove participants.
6. System allows at most 32 participants.

## Flow 3 — Generate bracket preview

1. Admin has between 2 and 32 participants.
2. Admin selects Generate Bracket.
3. System creates a random initial distribution.
4. System calculates required byes.
5. System displays the bracket preview.

## Flow 4 — Edit bracket preview

1. Admin reviews the generated bracket.
2. Admin may move participants within the initial arrangement.
3. Admin selects the exact required number of bye recipients.
4. System validates:
   - each participant appears once;
   - bye count is correct;
   - no bye participant has an opponent;
   - all required first-round positions are valid.
5. Admin may regenerate the entire preview instead.

## Flow 5 — Start tournament

1. Admin selects Start Tournament.
2. System validates the preview.
3. System asks for confirmation.
4. System locks participants and initial placement.
5. System advances bye recipients.
6. System changes status to Running.
7. System opens the live bracket.

## Flow 6 — Record partial score

1. Admin clicks an available match.
2. Match dialog opens.
3. Admin confirms or overrides Match Format.
4. Admin selects a valid Score Type.
5. Admin enters one or more detailed scores.
6. System validates that no detailed score is a draw.
7. System calculates the aggregate score.
8. Admin saves.
9. System sets the match to In Progress.

## Flow 7 — Complete match

1. Admin opens an available match.
2. Admin enters enough detailed results for one participant to reach the required wins.
3. System validates the result.
4. System calculates the winner.
5. Admin saves.
6. System sets the match to Completed.
7. System advances the winner.
8. In Double Elimination, system routes the loser when applicable.
9. Dependent matches become available when both participants are known.

## Flow 8 — Complete by forfeit

1. Admin opens an available match.
2. Admin chooses Forfeit.
3. Admin selects the winner.
4. Admin confirms.
5. System marks the match as Forfeit.
6. System advances the winner.

## Flow 9 — Undo latest match

1. Admin selects Undo on the latest completed match.
2. System checks whether the dependent next match has started.
3. If not started, system asks for confirmation.
4. System removes advancement caused by the match.
5. System restores the match to Pending or In Progress.
6. System recalculates affected dependent match availability.

## Flow 10 — View public tournament

1. User opens the public tournament link.
2. System displays tournament information.
3. User views participants, bracket, match formats and results.
4. No editing actions are shown.

## Flow 11 — Finish tournament

1. Final required match is completed.
2. System identifies final placements.
3. Admin or system marks the tournament Finished.
4. Tournament remains accessible from history and public view.
