# Domain Model

## Tournament
- Id
- Name
- Date
- Notes
- Status
- TournamentType
- DefaultMatchFormat
- ThirdPlaceEnabled

## Participant
- Id
- Name
- Seed

## Match
- Id
- Round
- Bracket
- MatchFormat
- ScoreType
- Status
- PlayerA
- PlayerB
- Winner

## Score

Generic value interpreted by ScoreType.

## PublicLink
- Id
- Token
- TournamentId
