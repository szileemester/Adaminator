import { useState } from 'react';
import type { ReactNode } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  Collapse,
  IconButton,
  Snackbar,
  Stack,
  Tooltip,
  Typography,
} from '@mui/material';
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/Delete';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import FastForwardIcon from '@mui/icons-material/FastForward';
import BalanceIcon from '@mui/icons-material/Balance';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import {
  deleteTournament,
  finishTournament,
  getBracket,
  getTournament,
  startNextSwissRound,
  startPlayoffs,
  startTiebreakers,
} from '../api/tournaments';
import { extractErrorMessage } from '../api/client';
import {
  groupStageKindLabels,
  matchFormatLabels,
  playoffKindLabels,
  scoreTypeLabels,
  tiebreakerPolicyLabels,
  tournamentSettingsShape,
  tournamentTypeLabels,
} from '../api/types';
import { SectionHeading } from '../components/SectionHeading';
import { StatusChip } from '../components/StatusChip';
import { TournamentTitle } from '../components/TournamentTitle';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ParticipantsSection } from '../components/ParticipantsSection';
import { BracketPreview } from '../components/BracketPreview';
import { GroupsPreview } from '../components/GroupsPreview';
import { BracketView } from '../components/BracketView';
import type { FocusStage } from '../components/BracketView';

export function TournamentDetailPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [confirmFinishOpen, setConfirmFinishOpen] = useState(false);
  const [confirmPlayoffsOpen, setConfirmPlayoffsOpen] = useState(false);
  const [confirmSwissRoundOpen, setConfirmSwissRoundOpen] = useState(false);
  const [confirmTiebreakersOpen, setConfirmTiebreakersOpen] = useState(false);
  const [copied, setCopied] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  // Set when a bracket action creates a stage's matches, so the view below jumps to that tab.
  const [focusStage, setFocusStage] = useState<FocusStage | null>(null);
  const [overviewOpen, setOverviewOpen] = useState(false);
  // Raised by the participants section while its panels differ from the saved roster, so seeding and
  // starting can't run against a roster that is about to change.
  const [rosterUnsaved, setRosterUnsaved] = useState(false);

  const { data: tournament, isLoading, isError, error } = useQuery({
    queryKey: ['tournaments', id],
    queryFn: () => getTournament(id),
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteTournament(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['tournaments'] });
      navigate('/');
    },
    // Without this a failed delete just re-enables the dialog button and says nothing at all.
    onError: (err: unknown) => {
      setConfirmOpen(false);
      setActionError(extractErrorMessage(err));
    },
  });

  const isPlanned = tournament?.status === 'Planned';

  const { data: bracket, isError: bracketFailed, error: bracketError } = useQuery({
    queryKey: ['bracket', id],
    queryFn: () => getBracket(id),
    enabled: Boolean(tournament) && !isPlanned,
  });

  /**
   * The three bracket actions (finish / start playoffs / resolve tie-breakers) differ only in the call
   * they make and the confirm dialog they close - refreshing and error handling are identical. This
   * builds the shared options; the useMutation calls stay top-level so hook order never varies.
   * `alsoRefreshList` is for Finish, which also changes the status shown on the dashboard.
   */
  const bracketAction = (
    action: () => Promise<unknown>,
    closeDialog: (open: boolean) => void,
    alsoRefreshList = false,
    /** The stage this action creates matches in, so the bracket view can jump to it. */
    focuses?: FocusStage['stage'],
  ) => ({
    mutationFn: action,
    onSuccess: async () => {
      closeDialog(false);
      if (focuses) {
        setFocusStage({ stage: focuses, at: Date.now() });
      }

      await Promise.all([
        ...(alsoRefreshList ? [queryClient.invalidateQueries({ queryKey: ['tournaments'] })] : []),
        queryClient.invalidateQueries({ queryKey: ['tournaments', id] }),
        queryClient.invalidateQueries({ queryKey: ['bracket', id] }),
      ]);
    },
    onError: (err: unknown) => {
      closeDialog(false);
      setActionError(extractErrorMessage(err));
    },
  });

  const finishMutation = useMutation(bracketAction(() => finishTournament(id), setConfirmFinishOpen, true));
  const startPlayoffsMutation = useMutation(bracketAction(() => startPlayoffs(id), setConfirmPlayoffsOpen, false, 'playoffs'));
  const startTiebreakersMutation = useMutation(bracketAction(() => startTiebreakers(id), setConfirmTiebreakersOpen, false, 'tiebreakers'));
  const startSwissRoundMutation = useMutation(bracketAction(() => startNextSwissRound(id), setConfirmSwissRoundOpen, false, 'groupStage'));

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  if (isError || !tournament) {
    return <Alert severity="error">{extractErrorMessage(error, 'Tournament not found.')}</Alert>;
  }

  const publicUrl = `${window.location.origin}/public/${tournament.publicToken}`;
  // The same rules the create form uses, so the Overview reports exactly the settings it offered.
  const shape = tournamentSettingsShape(tournament.type, tournament.groupStageKind, tournament.playoffKind);

  const copyPublicLink = async () => {
    // navigator.clipboard is undefined outside a secure context - reaching the dev server from a phone
    // over plain http is exactly that, and an unhandled rejection there would look like a dead button.
    try {
      await navigator.clipboard.writeText(publicUrl);
      setCopied(true);
    } catch {
      setActionError('Could not copy the link. Copy it from the address bar instead.');
    }
  };

  return (
    <Stack spacing={3}>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 2 }}>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center', minWidth: 0, flexShrink: 1 }}>
          <TournamentTitle name={tournament.name} />
          <StatusChip status={tournament.status} />
        </Stack>
        <Stack direction="row" spacing={1}>
          {tournament.status === 'Planned' && (
            <Button
              component={RouterLink}
              to={`/tournaments/${tournament.id}/edit`}
              startIcon={<EditIcon />}
              variant="outlined"
            >
              Edit
            </Button>
          )}
          <Button color="error" variant="outlined" startIcon={<DeleteIcon />} onClick={() => setConfirmOpen(true)}>
            Delete
          </Button>
        </Stack>
      </Stack>

      {/*
        Page level rather than inside the bracket card: Delete and Copy link are on every tournament,
        including a Planned one, which has no bracket card to show their failure in.
      */}
      {actionError && (
        <Alert severity="error" onClose={() => setActionError(null)}>
          {actionError}
        </Alert>
      )}

      {/*
        Collapsed by default: the settings here are fixed once the tournament starts, so they are
        reference material - the bracket below is what the admin actually works in.
      */}
      <Card>
        <CardContent>
          {/*
            Full width with the chevron pushed to the far edge, so the whole header row toggles - a
            button sized to just the word is a needle to hit, and the row reads as clickable anyway.
            MUI's default hover tint is left in place as the affordance that it is one.
          */}
          <Button
            onClick={() => setOverviewOpen((open) => !open)}
            endIcon={overviewOpen ? <ExpandLessIcon /> : <ExpandMoreIcon />}
            color="inherit"
            aria-expanded={overviewOpen}
            fullWidth
            sx={{ p: 0, textTransform: 'none', justifyContent: 'space-between' }}
          >
            <Typography variant="h6">Overview</Typography>
          </Button>
          {/* unmountOnExit: collapsed is the default, so there is no reason to keep a dozen rows
              mounted and re-rendering for content nobody has asked to see. */}
          <Collapse in={overviewOpen} unmountOnExit>
            {/* Same grouping and the same applies-to rules as the create form, so every setting the
                form offered is reported back and nothing it hid appears here. */}
            <Stack spacing={2.5} sx={{ mt: 2 }}>
              <DetailSection title="Basics">
                <DetailRow label="Date" value={tournament.date} />
                <DetailRow label="Type" value={tournamentTypeLabels[tournament.type]} />
                <DetailRow label="Score type" value={scoreTypeLabels[tournament.defaultScoreType]} />
              </DetailSection>

              {shape.isGroupStagePlayoff && (
                <DetailSection title="Structure">
                  <DetailRow label="Group stage" value={groupStageKindLabels[tournament.groupStageKind]} />
                  <DetailRow label="Playoff" value={playoffKindLabels[tournament.playoffKind]} />
                  {shape.usesRoundRobinGroups ? (
                    <DetailRow label="Groups" value={String(tournament.groupCount)} />
                  ) : (
                    <DetailRow
                      label="Swiss rounds"
                      value={
                        tournament.swissRounds > 0
                          ? String(tournament.swissRounds)
                          : `${tournament.resolvedSwissRounds} (auto)`
                      }
                    />
                  )}
                  <DetailRow
                    label="Playoff size"
                    value={
                      tournament.playoffSize > 0
                        ? String(tournament.playoffSize)
                        : `${tournament.playoffCapacity} (auto)`
                    }
                  />
                </DetailSection>
              )}

              <DetailSection title="Match formats">
                {shape.showsSingleFormat && (
                  <DetailRow label="Match format" value={matchFormatLabels[tournament.defaultMatchFormat]} />
                )}
                {shape.isGroupStagePlayoff && (
                  <DetailRow label="Group stage" value={matchFormatLabels[tournament.groupStageMatchFormat]} />
                )}
                {shape.usesBracketFormats && (
                  <DetailRow
                    label={shape.showsAllBracketFormats ? 'Upper bracket' : 'Playoff'}
                    value={matchFormatLabels[tournament.upperBracketFormat]}
                  />
                )}
                {shape.showsAllBracketFormats && (
                  <DetailRow label="Lower bracket" value={matchFormatLabels[tournament.lowerBracketFormat]} />
                )}
                {!shape.isRoundRobin && (
                  <DetailRow
                    label={shape.showsAllBracketFormats ? 'Grand Final' : 'Final'}
                    value={matchFormatLabels[tournament.grandFinalFormat]}
                  />
                )}
              </DetailSection>

              {shape.showsRules && (
                <DetailSection title="Rules">
                  {shape.playoffIsSingleElimination && (
                    <DetailRow
                      label="Third place match"
                      value={tournament.thirdPlaceEnabled ? 'Enabled' : 'Disabled'}
                    />
                  )}
                  {shape.showsTiebreakerPolicy && (
                    <DetailRow label="Tie-breaker" value={tiebreakerPolicyLabels[tournament.tiebreakerPolicy]} />
                  )}
                </DetailSection>
              )}

              {/* The heading already names it, so the body is the note itself rather than a label/value pair. */}
              <Stack spacing={1}>
                <SectionHeading title="Notes" />
                <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }} color={tournament.notes?.trim() ? undefined : 'text.secondary'}>
                  {tournament.notes?.trim() ? tournament.notes : '—'}
                </Typography>
              </Stack>
            </Stack>
          </Collapse>
        </CardContent>
      </Card>

      {tournament.status === 'Planned' ? (
        <>
          <ParticipantsSection
            tournamentId={tournament.id}
            tournamentType={tournament.type}
            minPanels={tournament.minParticipants}
            onUnsavedChange={setRosterUnsaved}
          />
          {/*
            A Swiss group stage has no draw - round 1 is paired from the seed order, so it uses the
            same bracket preview every non-group type does.
          */}
          {tournament.type === 'GroupStagePlayoff' && tournament.groupStageKind === 'RoundRobin' ? (
            <GroupsPreview
              tournamentId={tournament.id}
              groupCount={tournament.groupCount}
              rosterUnsaved={rosterUnsaved}
            />
          ) : (
            <BracketPreview
              tournamentId={tournament.id}
              tournamentType={tournament.type}
              rosterUnsaved={rosterUnsaved}
            />
          )}
        </>
      ) : (
        <Card>
          <CardContent>
            <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 1, mb: 1 }}>
              <Typography variant="h6">Bracket</Typography>
              {tournament.status === 'Running' && (
                <Stack direction="row" spacing={1}>
                  {bracket?.needsTiebreakers && (
                    <Button
                      size="small"
                      variant="outlined"
                      color="warning"
                      startIcon={<BalanceIcon />}
                      onClick={() => setConfirmTiebreakersOpen(true)}
                    >
                      Resolve tie-breakers
                    </Button>
                  )}
                  {bracket?.groupStage?.canStartNextRound && (
                    <Button
                      size="small"
                      variant="outlined"
                      startIcon={<FastForwardIcon />}
                      onClick={() => setConfirmSwissRoundOpen(true)}
                    >
                      Start round {bracket.groupStage.roundsPlayed + 1}
                    </Button>
                  )}
                  {bracket?.canStartPlayoffs && (
                    <Button
                      size="small"
                      variant="outlined"
                      startIcon={<FastForwardIcon />}
                      onClick={() => setConfirmPlayoffsOpen(true)}
                    >
                      Start playoffs
                    </Button>
                  )}
                  <Button
                    size="small"
                    variant="outlined"
                    color="success"
                    startIcon={<EmojiEventsIcon />}
                    disabled={!bracket?.canFinish}
                    onClick={() => setConfirmFinishOpen(true)}
                  >
                    Finish tournament
                  </Button>
                </Stack>
              )}
            </Stack>

            {bracket ? (
              <BracketView bracket={bracket} tournamentId={tournament.id} focusStage={focusStage} />
            ) : bracketFailed ? (
              // Otherwise a failed load spins here for ever, with the action buttons above it still
              // offering to finish a tournament whose bracket never arrived.
              <Alert severity="error">{extractErrorMessage(bracketError, 'Could not load the bracket.')}</Alert>
            ) : (
              <CircularProgress size={24} />
            )}
          </CardContent>
        </Card>
      )}

      <Stack
        direction="row"
        spacing={1}
        sx={{ alignItems: 'center', flexWrap: 'wrap', color: 'text.secondary' }}
      >
        <Typography variant="caption">Public view:</Typography>
        {/*
          The URL is hidden on a phone, where it wraps to three lines of unreadable token and costs
          more space than the rest of the page footer. Copy and Open stay - they are how the link is
          actually shared, and dropping them would make a phone the one place you can't hand it out.
        */}
        <Typography variant="caption" sx={{ wordBreak: 'break-all', display: { xs: 'none', sm: 'block' } }}>
          {publicUrl}
        </Typography>
        <Tooltip title="Copy link">
          <IconButton size="small" onClick={copyPublicLink}>
            <ContentCopyIcon fontSize="inherit" />
          </IconButton>
        </Tooltip>
        <Tooltip title="Open">
          <IconButton size="small" component={RouterLink} to={`/public/${tournament.publicToken}`} target="_blank">
            <OpenInNewIcon fontSize="inherit" />
          </IconButton>
        </Tooltip>
      </Stack>

      <ConfirmDialog
        open={confirmOpen}
        title="Delete tournament"
        message={`Delete "${tournament.name}"? This action cannot be undone.`}
        confirmLabel="Delete"
        confirmColor="error"
        busy={deleteMutation.isPending}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={() => deleteMutation.mutate()}
      />

      <ConfirmDialog
        open={confirmFinishOpen}
        title="Finish tournament"
        message="This marks the tournament as Finished and locks the result - a finished tournament's matches can no longer be undone."
        confirmLabel="Finish"
        confirmColor="success"
        busy={finishMutation.isPending}
        onCancel={() => setConfirmFinishOpen(false)}
        onConfirm={() => finishMutation.mutate()}
      />

      <ConfirmDialog
        open={confirmPlayoffsOpen}
        title="Start playoffs"
        message="This seeds and generates the playoff bracket from the group standings. Group results are locked in once the playoff starts."
        confirmLabel="Start playoffs"
        busy={startPlayoffsMutation.isPending}
        onCancel={() => setConfirmPlayoffsOpen(false)}
        onConfirm={() => startPlayoffsMutation.mutate()}
      />

      <ConfirmDialog
        open={confirmSwissRoundOpen}
        title="Start the next Swiss round"
        message="This pairs the next round from the current standings - participants on a similar record meet, and nobody plays the same opponent twice. Results already entered stay editable until the round after this one is paired."
        confirmLabel="Pair round"
        busy={startSwissRoundMutation.isPending}
        onCancel={() => setConfirmSwissRoundOpen(false)}
        onConfirm={() => startSwissRoundMutation.mutate()}
      />

      <ConfirmDialog
        open={confirmTiebreakersOpen}
        title="Resolve tie-breakers"
        message="This generates the Bo1 tie-breaker matches needed to break a standings tie that changes an outcome. Play them out, then the standings and seeding settle."
        confirmLabel="Generate tie-breakers"
        confirmColor="warning"
        busy={startTiebreakersMutation.isPending}
        onCancel={() => setConfirmTiebreakersOpen(false)}
        onConfirm={() => startTiebreakersMutation.mutate()}
      />

      <Snackbar
        open={copied}
        autoHideDuration={2000}
        onClose={() => setCopied(false)}
        message="Public link copied"
      />
    </Stack>
  );
}

/** A titled group of label/value rows, mirroring the create form's sections. */
function DetailSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Stack spacing={1}>
      <SectionHeading title={title} />
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, columnGap: 4, rowGap: 1 }}>
        {children}
      </Box>
    </Stack>
  );
}

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <Stack direction="row" spacing={2}>
      <Typography variant="body2" color="text.secondary" sx={{ minWidth: 180 }}>
        {label}
      </Typography>
      <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
        {value}
      </Typography>
    </Stack>
  );
}
