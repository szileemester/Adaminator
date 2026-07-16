# Adaminator – Double Elimination Specification

## 1. Scope

This document defines the required Double Elimination behavior for Adaminator.

Supported bracket capacities:

- 4 slots
- 8 slots
- 16 slots
- 32 slots

Actual participant counts may be lower. Missing participants are handled with first-round Winner Bracket byes.

## 2. Core rules

1. Every participant starts in the Winner Bracket.
2. The first loss routes the participant to the Loser Bracket.
3. A loss in the Loser Bracket eliminates the participant.
4. The Winner Bracket winner reaches the Grand Final.
5. The Loser Bracket winner reaches the Grand Final.
6. The Loser Bracket Final loser is third.
7. The Grand Final loser is second.
8. The Grand Final winner is champion.
9. There is no separate third-place match.
10. There is no Grand Final Reset.
11. Matches and their routes are the source of truth.
12. The bracket is a visual projection of the match graph.

The lack of a Grand Final Reset is intentional. The Winner Bracket winner can lose the tournament in the Grand Final even when that is their first loss.

## 3. Generated graph sizes

For capacity `N = 2^k`:

- Winner Bracket rounds: `k`
- Loser Bracket rounds: `2k - 2`
- Grand Finals: `1`

| Capacity | WB rounds | LB rounds |
|---:|---:|---:|
| 4 | 2 | 2 |
| 8 | 3 | 4 |
| 16 | 4 | 6 |
| 32 | 5 | 8 |

The implementation shall generate the structure algorithmically, but only for these four capacities.

## 4. Match graph

Each match may define:

```text
WinnerToMatchId
WinnerToSlot
LoserToMatchId
LoserToSlot
```

Valid destination slots:

```text
A
B
```

Routing behavior:

### Winner Bracket match

```text
Winner -> next Winner Bracket match or Grand Final
Loser  -> predetermined Loser Bracket match
```

### Loser Bracket match

```text
Winner -> next Loser Bracket match or Grand Final
Loser  -> eliminated
```

### Winner Bracket Final

```text
Winner -> Grand Final Slot A
Loser  -> Loser Bracket Final
```

### Loser Bracket Final

```text
Winner -> Grand Final Slot B
Loser  -> third place
```

### Grand Final

```text
Winner -> first place
Loser  -> second place
```

The graph topology and all routes become immutable when the tournament starts.

## 5. Winner Bracket

The Winner Bracket uses the existing Single Elimination progression logic.

For every match except the Winner Bracket Final:

- winner advances within the Winner Bracket;
- loser drops to a fixed Loser Bracket destination.

The loser destination shall be generated before tournament start and shall not be recalculated from actual results.

## 6. Loser Bracket

Loser Bracket rounds alternate between two roles.

### Consolidation round

Participants already in the Loser Bracket play each other.

- winner remains in the Loser Bracket;
- loser is eliminated.

### Drop-in round

Surviving Loser Bracket participants play fresh Winner Bracket losers.

- winner remains in the Loser Bracket;
- loser is eliminated.

The Loser Bracket contains more rounds than the Winner Bracket because consolidation and drop-in rounds alternate.

## 7. Crossover routing and rematch avoidance

Routing shall be deterministic and shall reduce immediate rematches.

Priority order:

1. Avoid an immediate rematch in the next playable match.
2. Avoid routing a fresh Winner Bracket loser into the same local branch from which their likely previous opponent came, when another valid destination exists.
3. Delay possible rematches as long as the fixed topology reasonably allows.
4. Preserve deterministic and testable routing.
5. Never dynamically rewire the graph based on actual winners or losers.

Conceptual rule:

```text
upper Winner Bracket branch losers
    -> lower Loser Bracket destinations

lower Winner Bracket branch losers
    -> upper Loser Bracket destinations
```

The implementation may use reverse order, half swapping, pair swapping or recursive crossover, provided that the generated result matches approved snapshots.

A rematch is not forbidden forever. It may become unavoidable in later rounds.

## 8. Example: 8-slot routing

### Winner Bracket

```text
WB1 winner -> WB5 A
WB1 loser  -> LB1 A

WB2 winner -> WB5 B
WB2 loser  -> LB1 B

WB3 winner -> WB6 A
WB3 loser  -> LB2 A

WB4 winner -> WB6 B
WB4 loser  -> LB2 B

WB5 winner -> WB7 A
WB5 loser  -> LB4 B

WB6 winner -> WB7 B
WB6 loser  -> LB3 B

WB7 winner -> GF A
WB7 loser  -> LB6 B
```

### Loser Bracket

```text
LB1 winner -> LB3 A
LB2 winner -> LB4 A

LB3 winner -> LB5 A
LB4 winner -> LB5 B

LB5 winner -> LB6 A

LB6 winner -> GF B
LB6 loser  -> third place
```

### Grand Final

```text
GF winner -> first place
GF loser  -> second place
```

The reversal of the WB5 and WB6 loser destinations is intentional crossover routing.

## 9. Algorithm outline

```text
GenerateDoubleEliminationGraph(capacity):

    ValidateSupportedCapacity(capacity)

    winnerBracket = GenerateWinnerBracket(capacity)

    loserBracket = GenerateLoserBracketSkeleton(capacity)

    ConnectWinnerBracketWinnerRoutes(winnerBracket)

    ConnectLoserBracketWinnerRoutes(loserBracket)

    RouteWinnerBracketLosers(
        winnerBracket,
        loserBracket,
        deterministicCrossoverPermutation
    )

    grandFinal = CreateGrandFinal()

    ConnectWinnerBracketFinal(
        winnerBracket,
        loserBracket,
        grandFinal
    )

    ConnectLoserBracketFinal(
        loserBracket,
        grandFinal
    )

    ValidateGraph()

    return graph
```

The graph generator produces topology only. Random participant placement and bye assignment are separate concerns.

## 10. Deterministic permutation

A round-specific permutation shall determine where fresh Winner Bracket losers enter the Loser Bracket.

Conceptual signature:

```text
GetDropPermutation(
    winnerRoundIndex,
    sourceMatchCount
) -> ordered destination indexes
```

Requirements:

- same input always produces the same output;
- every source match maps exactly once;
- every destination slot receives at most one source;
- output must match an approved routing snapshot;
- the permutation should cross bracket halves where possible.

## 11. Byes

A bye is not a match.

When a participant receives a bye:

- no Match entity is created for the bye;
- no win is recorded;
- no loss is recorded;
- the participant remains undefeated;
- the participant is inserted directly into the next Winner Bracket match slot.

Byes are allowed only in the first Winner Bracket round.

A bye never routes anyone into the Loser Bracket.

The graph is still generated at the full selected capacity. Bye application happens after graph generation.

Examples:

```text
3–4 participants   -> 4-slot graph
5–8 participants   -> 8-slot graph
9–16 participants  -> 16-slot graph
17–32 participants -> 32-slot graph
```

## 12. Match availability

A match is actionable only when both participant slots are resolved.

An unresolved match:

- remains locked;
- cannot start;
- cannot accept scores;
- cannot be completed by forfeit.

When result propagation fills the second slot, the match becomes available.

## 13. Result propagation

### Winner Bracket completion

1. Put winner into `WinnerToMatchId / WinnerToSlot`.
2. Put loser into `LoserToMatchId / LoserToSlot`.
3. Recalculate availability of both dependent matches.

### Loser Bracket completion

1. Put winner into `WinnerToMatchId / WinnerToSlot`.
2. Mark loser eliminated.
3. Recalculate availability of the dependent match.

### Loser Bracket Final completion

1. Put winner into Grand Final Slot B.
2. Assign loser to third place.

### Grand Final completion

1. Assign winner to first place.
2. Assign loser to second place.
3. Finish the tournament.

## 14. Undo

Only the chronologically latest completed match may be undone.

### Winner Bracket match undo

Undo shall:

- remove the winner from the Winner destination;
- remove the loser from the Loser destination;
- restore the source match to Pending or In Progress;
- recalculate dependent match availability.

Undo is forbidden if either routed participant has already started a dependent match.

### Loser Bracket match undo

Undo shall:

- remove the winner from the next Loser Bracket destination;
- restore the previously eliminated participant to active status;
- restore the source match to Pending or In Progress.

### Loser Bracket Final undo

Undo shall remove:

- the winner from the Grand Final;
- the third-place assignment.

### Grand Final undo

Undo shall remove first- and second-place assignments and return the tournament to Running.

## 15. Graph validation

Before tournament start, validation shall verify:

- capacity is 4, 8, 16 or 32;
- every Match ID is unique;
- exactly one Grand Final exists;
- every route references an existing destination;
- every destination slot has at most one incoming route;
- every required destination slot has a valid source;
- every Winner Bracket non-final winner has a destination;
- every Winner Bracket loser has a Loser Bracket destination;
- Loser Bracket losers have no match destination;
- Winner Bracket Final winner reaches Grand Final Slot A;
- Winner Bracket Final loser reaches the Loser Bracket Final;
- Loser Bracket Final winner reaches Grand Final Slot B;
- the graph contains no cycle;
- no route points to an earlier logical round.

Tournament start shall fail if validation fails.

## 16. Snapshot specification

The repository shall contain approved graph snapshots for:

- 4-slot Double Elimination;
- 8-slot Double Elimination;
- 16-slot Double Elimination;
- 32-slot Double Elimination.

Each snapshot shall contain:

```text
Match ID
Bracket type
Round index
Position in round
Winner destination
Winner destination slot
Loser destination
Loser destination slot
```

The algorithm is considered correct only when generated graphs match these snapshots.

A snapshot change requires explicit review. It must not be updated automatically merely to make a failing test pass.

## 17. Required tests

### Structural tests

For every supported capacity:

- correct Winner Bracket round count;
- correct Loser Bracket round count;
- correct total match count;
- exactly one Grand Final;
- unique Match IDs;
- acyclic graph.

### Routing tests

- every Winner Bracket loser has exactly one Loser Bracket route;
- every route matches the approved snapshot;
- crossover routes reduce immediate rematches in covered scenarios;
- no destination slot has multiple incoming sources;
- final routes are correct.

### Execution tests

- first loss routes to the Loser Bracket;
- second loss eliminates the participant;
- Winner Bracket Final loser reaches the Loser Bracket Final;
- Loser Bracket Final loser becomes third;
- Loser Bracket Final winner reaches the Grand Final;
- Grand Final winner becomes champion;
- no Grand Final Reset is created.

### Bye tests

- bye does not create a match;
- bye does not create a win;
- bye does not create a loss;
- bye participant reaches the correct next Winner Bracket slot;
- bye does not create a Loser Bracket route.

### Undo tests

- Winner Bracket undo reverses both outgoing routes;
- undo is blocked if either dependent match started;
- Loser Bracket undo restores an eliminated participant;
- Loser Bracket Final undo removes third place;
- Grand Final undo removes final placements.

## 18. Acceptance criteria

### AC-DE-001

Given a supported capacity, when the graph is generated repeatedly, then its topology and routing are identical.

### AC-DE-002

Given a generated graph, then it matches the approved snapshot for its capacity.

### AC-DE-003

Given a participant loses a Winner Bracket match, then the participant is routed to the predetermined Loser Bracket destination and is not eliminated.

### AC-DE-004

Given a participant loses a Loser Bracket match, then the participant is eliminated.

### AC-DE-005

Given a crossover alternative exists in the approved topology, then a fresh Winner Bracket loser is not assigned to an immediate rematch.

### AC-DE-006

Given the Winner Bracket Final completes, then its winner reaches Grand Final Slot A and its loser reaches the Loser Bracket Final.

### AC-DE-007

Given the Loser Bracket Final completes, then its winner reaches Grand Final Slot B and its loser becomes third.

### AC-DE-008

Given the Grand Final completes, then its winner is first, its loser is second and no reset match is created.

### AC-DE-009

Given a participant receives a bye, then no match result, win or loss is created.

### AC-DE-010

Given either dependent match of a completed Winner Bracket match has started, then undo of the source match is rejected.

## 19. AI implementation rules

An AI coding assistant working on this feature shall:

- not invent a different Double Elimination topology;
- not add Grand Final Reset;
- not treat a bye as a completed match;
- not dynamically rewire the graph from actual results;
- preserve crossover routing;
- keep graph generation separate from result propagation;
- make matches and routes authoritative, not the rendered bracket;
- add snapshot tests for all four capacities;
- ask before changing an approved routing snapshot.
