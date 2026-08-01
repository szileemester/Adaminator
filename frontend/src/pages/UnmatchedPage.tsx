import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import { getUnmatchedScoreboard, saveUnmatchedScoreboard } from '../api/unmatched';
import type { UnmatchedPick, UnmatchedScoreboard, UnmatchedTeam } from '../api/unmatched';
import { extractErrorMessage } from '../api/client';

/**
 * The house Unmatched scoreboard, drawn as a comic book cover: the two fixed teams, what each player
 * is currently playing, the running win totals and a crown over whoever took the last game.
 *
 * The page is unlisted rather than protected - anyone given the link can watch it, and anyone who
 * finds the click gesture on the VS badge can record a result. That is deliberate: the players are
 * not admins, and the gesture is there to keep the editor out of the way, not to keep anyone out.
 */
const CHARACTERS = Object.entries(
  import.meta.glob<string>('../assets/characters/*.webp', { eager: true, import: 'default', query: '?url' }),
).map(([path, url]) => ({
  url,
  // "../assets/characters/Ancient_Leshen.webp" -> "Ancient Leshen"
  name: path.split('/').pop()!.replace(/\.webp$/, '').replace(/_/g, ' '),
}));

const CHARACTER_NAMES = CHARACTERS.map((c) => c.name).sort((a, b) => a.localeCompare(b));
const ART_BY_CHARACTER = new Map(CHARACTERS.map((c) => [c.name, c.url]));

/** The one black everything on the page is outlined in - frame, borders, card backing, burst. */
const INK = '#0b0b0b';

/** The page's lettering voice, shared by every plate so they read as one typographic system. */
const COMIC_TEXT = { fontWeight: 900, fontStyle: 'italic', textTransform: 'uppercase' } as const;

/** The centre scoreline's two pieces. Constants, so they sit beside COMIC_TEXT rather than inside a render. */
const CROWN_SX = {
  fontSize: { xs: '1.6rem', sm: '2.1rem', md: '3rem' },
  color: '#ffd23f',
  filter: 'drop-shadow(2px 3px 0 rgba(0,0,0,0.65))',
} as const;

const SCORE_SX = {
  ...COMIC_TEXT,
  color: '#fff',
  lineHeight: 1,
  fontSize: { xs: '1.9rem', sm: '2.5rem', md: '3.4rem' },
  textShadow: '3px 3px 0 rgba(0,0,0,0.6)',
} as const;

/** Radiating comic rays over the flat team colour, like a cheap printed cover. */
const RAYS = 'repeating-conic-gradient(from 0deg at 50% 45%, rgba(255,255,255,0.11) 0deg 3deg, transparent 3deg 10deg)';

/**
 * The lightning bolt's centre line: [distance along the arena, position across it], in percent. The
 * bolt is not decoration laid over a join - it *is* the join. Both teams clip to its edges and it
 * fills the space between, so one list defines the separator and the two fields at once and they
 * cannot drift apart.
 */
const BOLT: readonly (readonly [number, number])[] = [
  [0, 57], [20, 45], [38, 58], [56, 44], [76, 57], [100, 45],
];

/** Half the bolt's thickness, and how far the teams sit back from it to leave an inked edge. */
const BOLT_HALF = 5.2;
const BOLT_KEYLINE = 1.7;
const BOLT_REACH = BOLT_HALF + BOLT_KEYLINE;

type Axis = 'vertical' | 'horizontal';
type Side = 'leading' | 'trailing';

/** One point in the arena, written for whichever axis the bolt currently runs on. */
const point = (axis: Axis, along: number, cross: number) =>
  axis === 'vertical' ? `${cross}% ${along}%` : `${along}% ${cross}%`;

const boltEdge = (axis: Axis, offset: number) =>
  BOLT.map(([along, centre]) => point(axis, along, centre + offset));

/** Everything on one side of the bolt, closed around that side's two far corners. */
function fieldClip(axis: Axis, side: Side): string {
  const leading = side === 'leading';
  const outer = leading ? 0 : 100;
  const corners = [point(axis, 100, outer), point(axis, 0, outer)];
  return `polygon(${[...boltEdge(axis, leading ? -BOLT_REACH : BOLT_REACH), ...corners].join(', ')})`;
}

/** The ribbon itself: down one edge and back up the other. */
const boltClip = (axis: Axis) =>
  `polygon(${[...boltEdge(axis, -BOLT_HALF), ...boltEdge(axis, BOLT_HALF).reverse()].join(', ')})`;

/**
 * Teams split left/right on a desktop and top/bottom on a phone. Every axis-dependent value keys off
 * this one breakpoint - the bolt's axis, the arena's orientation, the team bands and which dimension
 * bounds them are four faces of a single decision, and a page with only some of them switched is
 * badly broken rather than slightly off.
 */
const STACK_BP = 'md' as const;
const responsive = <T,>(stacked: T, sideBySide: T) => ({ xs: stacked, [STACK_BP]: sideBySide });

const FIELD_CLIP = {
  leading: responsive(fieldClip('horizontal', 'leading'), fieldClip('vertical', 'leading')),
  trailing: responsive(fieldClip('horizontal', 'trailing'), fieldClip('vertical', 'trailing')),
};
const BOLT_CLIP = responsive(boltClip('horizontal'), boltClip('vertical'));

/**
 * How much of the arena a team's content may occupy, derived from the bolt rather than guessed. The
 * bolt does not straddle the 50% line evenly, so the two sides have different clearance and the
 * tighter one governs - hand-setting this left one side 0.1% of room.
 */
const BOLT_CENTRES = BOLT.map(([, centre]) => centre);
const CONTENT_MARGIN = 2;
const CONTENT_EXTENT = `${(
  Math.min(Math.min(...BOLT_CENTRES) - BOLT_REACH, 100 - (Math.max(...BOLT_CENTRES) + BOLT_REACH)) - CONTENT_MARGIN
).toFixed(1)}%`;

/**
 * The two fixed sides and who plays for them. Everything team-shaped is read from here - the crown,
 * the banners, the editor's labels and its toggle - so a rename or a change to the API's wire value is
 * one edit. Only `side` and `victor` are annotated: the rest are inferred, and the two that matter are
 * the ones a typo would break silently rather than loudly.
 */
const TEAMS = [
  {
    side: 'leading',
    label: 'Fiúk',
    victor: 'Fiuk',
    players: ['Márk', 'Ádám'],
    field: '#12459b',
    banner: '#0d3179',
    ink: '#7ec8ff',
  },
  {
    side: 'trailing',
    label: 'Lányok',
    victor: 'Lanyok',
    players: ['Berni', 'Reni'],
    field: '#b81f28',
    banner: '#8d141c',
    ink: '#ffb3a7',
  },
  // Record<string, unknown> so the other fields stay inferred: without an index signature, `satisfies`
  // applies an excess-property check and every colour would have to be declared here too.
] as const satisfies readonly ({ side: Side; victor: UnmatchedTeam } & Record<string, unknown>)[];

type Team = (typeof TEAMS)[number];

/** Every player, in the order the teams list them. The scoreboard stores a character per name. */
const ALL_PLAYERS = TEAMS.flatMap((team) => team.players);

/** Clicks on the VS badge that open the editor, and how long a run of them may pause before resetting. */
const SECRET_CLICKS = 5;
const SECRET_TIMEOUT_MS = 1500;

export function UnmatchedPage() {
  const [editorOpen, setEditorOpen] = useState(false);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['unmatched'],
    queryFn: getUnmatchedScoreboard,
  });

  return (
    <Box sx={{ minHeight: '100vh', p: { xs: 2, md: 4 }, display: 'grid', placeItems: 'center' }}>
      {isLoading && <CircularProgress />}
      {isError && <Alert severity="error">{extractErrorMessage(error)}</Alert>}
      {data && (
        <>
          <Arena scoreboard={data} onSecret={() => setEditorOpen(true)} />
          {/*
            Mounted only while open, so each field initialises straight from the scoreboard and a
            cancelled edit is discarded by unmounting rather than by remembering to reset it.
          */}
          {editorOpen && <ScoreboardEditor scoreboard={data} onClose={() => setEditorOpen(false)} />}
        </>
      )}
    </Box>
  );
}

function Arena({ scoreboard, onSecret }: { scoreboard: UnmatchedScoreboard; onSecret: () => void }) {
  const revealEditor = useSecretGesture(onSecret);

  return (
    <Box
      sx={{
        position: 'relative',
        // Portrait on a phone (teams stacked), wide on a desktop (teams side by side). The phone
        // ratio is what sizes the cards: a team's band is a fixed share of the arena's height (see
        // CONTENT_EXTENT, which the bolt dictates), so a taller arena is the only way to grow them.
        aspectRatio: responsive('11 / 20', '16 / 9'),
        width: '100%',
        maxWidth: { [STACK_BP]: 1040 },
        // Both breakpoints stay inside the viewport. The phone ratio is deliberately tall so the cards
        // get room; on a short screen this caps it and the aspect ratio narrows the arena to suit,
        // which is better than making the page scroll. dvh, so mobile browser chrome is accounted for.
        maxHeight: { xs: 'calc(100dvh - 32px)', [STACK_BP]: 'calc(100vh - 64px)' },
        bgcolor: INK,
        border: `4px solid ${INK}`,
        borderRadius: 1,
        overflow: 'hidden',
        boxShadow: '0 10px 30px rgba(0,0,0,0.45)',
      }}
    >
      {TEAMS.map((team) => (
        <TeamField key={team.side} team={team} picks={scoreboard.picks} />
      ))}

      {/* Above both fields: the white bolt filling the channel they were clipped back from. */}
      <Box
        sx={{
          position: 'absolute',
          inset: 0,
          clipPath: BOLT_CLIP,
          background: 'linear-gradient(160deg, #ffffff 0%, #f2f2f2 55%, #d9d9d9 100%)',
        }}
      />

      {/*
        Badge and scoreline are centred as one stack rather than positioned independently: the score
        belongs to the clash, and pinning them separately would need a magic offset that drifts the
        moment either changes size.
      */}
      <Stack
        sx={{
          position: 'absolute',
          top: '50%',
          left: '50%',
          transform: 'translate(-50%, -50%)',
          alignItems: 'center',
          gap: { xs: 0.5, md: 1 },
        }}
      >
        <VersusBadge onClick={revealEditor} />
        <Scoreline scoreboard={scoreboard} />
      </Stack>
    </Box>
  );
}

/**
 * The running totals, with a crown over whichever side took the last game. Both crowns keep their
 * space whether shown or not, so the numbers stay centred instead of shuffling sideways with the
 * result - and a draw simply shows neither.
 */
function Scoreline({ scoreboard }: { scoreboard: UnmatchedScoreboard }) {
  const [fiuk, lanyok] = TEAMS;
  // A crown per team, driven by TEAMS.victor rather than a literal - the comparison is the one place a
  // wrong wire value would be silent, since a mismatch just reads as "this side didn't win".
  const crown = (team: Team) => (
    <EmojiEventsIcon
      sx={{ ...CROWN_SX, visibility: scoreboard.lastVictor === team.victor ? 'visible' : 'hidden' }}
    />
  );

  return (
    // On its own ink plate: the scoreline is wider than the bolt, so it crosses white, blue and red,
    // and no single text colour reads on all three.
    <Stack
      direction="row"
      spacing={responsive(0.75, 1.5)}
      sx={{
        alignItems: 'center',
        px: responsive(1.25, 2.5),
        py: responsive(0.25, 0.5),
        bgcolor: INK,
        border: '3px solid rgba(255,255,255,0.85)',
        borderRadius: 1,
        boxShadow: '4px 5px 0 rgba(0,0,0,0.5)',
      }}
    >
      {crown(fiuk)}
      <Typography sx={SCORE_SX}>{scoreboard.fiukWins}</Typography>
      <Typography sx={{ ...SCORE_SX, opacity: 0.55 }}>–</Typography>
      <Typography sx={SCORE_SX}>{scoreboard.lanyokWins}</Typography>
      {crown(lanyok)}
    </Stack>
  );
}

/** One team's colour field: its banner, then a card per player. */
function TeamField({ team, picks }: { team: Team; picks: UnmatchedPick[] }) {
  const leading = team.side === 'leading';

  return (
    <Box
      sx={{
        position: 'absolute',
        inset: 0,
        clipPath: FIELD_CLIP[team.side],
        backgroundColor: team.field,
        backgroundImage: RAYS,
      }}
    >
      <Box
        sx={{
          position: 'absolute',
          // A band across the top or bottom on a phone, a column down one side on a desktop.
          inset: responsive(
            leading ? '0 0 auto 0' : 'auto 0 0 0',
            leading ? '0 auto 0 0' : '0 0 0 auto',
          ),
          height: responsive(CONTENT_EXTENT, 'auto'),
          width: responsive('auto', CONTENT_EXTENT),
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          // Tight on a phone: every millimetre the band spends on padding comes off the cards.
          gap: responsive(0.4, 1),
          p: responsive(0.6, 2),
        }}
      >
        <TeamBanner team={team} />
        <Box
          sx={{
            flex: 1,
            minHeight: 0,
            width: '100%',
            display: 'flex',
            // Team-mates sit side by side inside a phone's band, stacked inside a desktop column.
            flexDirection: responsive('row', 'column'),
            // Cross-axis is height in a row and width in a column: stretch so a card is bounded by
            // the band's height, since `center` lets it size to content and spill past both edges.
            alignItems: responsive('stretch', 'center'),
            justifyContent: 'center',
            gap: responsive(0.75, 1.5),
          }}
        >
          {team.players.map((player) => (
            <PlayerCard
              key={player}
              player={player}
              character={picks.find((p) => p.playerName === player)?.character ?? null}
              ink={team.ink}
            />
          ))}
        </Box>
      </Box>
    </Box>
  );
}

/** Just the team's name - the totals and the crown live on the centre scoreline. */
function TeamBanner({ team }: { team: Team }) {
  return (
    <Typography
      noWrap
      sx={{
        ...COMIC_TEXT,
        flexShrink: 0,
        maxWidth: '100%',
        px: responsive(1, 2),
        py: responsive(0.1, 0.4),
        bgcolor: team.banner,
        color: '#fff',
        border: `3px solid ${INK}`,
        borderRadius: 0.5,
        transform: 'skewX(-8deg)',
        letterSpacing: '0.08em',
        fontSize: { xs: '0.85rem', sm: '1rem', md: '1.15rem' },
        textShadow: '2px 2px 0 rgba(0,0,0,0.55)',
        boxShadow: '3px 3px 0 rgba(0,0,0,0.45)',
      }}
    >
      {team.label}
    </Typography>
  );
}

/**
 * One player's card: the art of whatever they are playing, their name, and the character underneath.
 * Square, because the source art is - the whole character is shown rather than cropped to fill.
 */
function PlayerCard({
  player,
  character,
  ink,
}: {
  player: string;
  character: string | null;
  ink: string;
}) {
  const art = character ? ART_BY_CHARACTER.get(character) : undefined;
  // A stored name with no art means the image was renamed or removed since it was picked. Saying so
  // beats printing the name as if nothing were wrong - the editor no longer offers that value, so the
  // only way out is a re-pick, and nothing else would signal that.
  const artMissing = character !== null && art === undefined;

  return (
    // width:100% so the frame's maxWidth resolves against the band. Without it an unset card shrinks
    // to its "?" placeholder and stops being square, while a card holding art stays square by luck.
    <Stack
      sx={{ flex: 1, minWidth: 0, minHeight: 0, width: '100%', alignItems: 'center', justifyContent: 'center', gap: 0.25 }}
    >
      <Box
        sx={{
          flex: 1,
          minHeight: 0,
          aspectRatio: '1',
          maxWidth: '100%',
          border: `3px solid ${INK}`,
          borderRadius: 0.5,
          overflow: 'hidden',
          bgcolor: INK,
          boxShadow: '4px 4px 0 rgba(0,0,0,0.5)',
          transform: 'rotate(-1.2deg)',
          display: 'grid',
          placeItems: 'center',
        }}
      >
        {art && character ? (
          // Eager and high priority: these are the whole page and all of them are above the fold.
          <Box
            component="img"
            src={art}
            alt={character}
            fetchPriority="high"
            sx={{
              width: '100%',
              height: '100%',
              objectFit: 'contain',
              display: 'block',
              filter: 'saturate(1.15) contrast(1.06)',
            }}
          />
        ) : (
          <Typography sx={{ ...COMIC_TEXT, color: 'rgba(255,255,255,0.35)', fontSize: '0.7rem' }}>?</Typography>
        )}
      </Box>
      <Typography
        noWrap
        sx={{
          ...COMIC_TEXT,
          flexShrink: 0,
          maxWidth: '100%',
          color: '#fff',
          letterSpacing: '0.05em',
          fontSize: { xs: '0.85rem', sm: '0.95rem', md: '1rem' },
          textShadow: `0 0 6px ${ink}, 2px 2px 0 rgba(0,0,0,0.75)`,
        }}
      >
        {player}
      </Typography>
      <Typography
        noWrap
        sx={{
          flexShrink: 0,
          maxWidth: '100%',
          color: artMissing ? 'rgba(255,180,180,0.9)' : 'rgba(255,255,255,0.82)',
          fontStyle: 'italic',
          textDecoration: artMissing ? 'line-through' : 'none',
          fontSize: { xs: '0.72rem', sm: '0.76rem', md: '0.8rem' },
          textShadow: '1px 1px 0 rgba(0,0,0,0.7)',
        }}
      >
        {character ?? 'nincs megadva'}
      </Typography>
    </Stack>
  );
}

/** Per-spike length multipliers, so the burst reads as drawn rather than as a UI icon. */
const SPIKES = [1, 0.84, 1.06, 0.79, 1.02, 0.88, 1.1, 0.81, 1.04, 0.86, 1.08, 0.83];
const BURST_CORE = 0.31;

const burstPoint = (turn: number, radius: number) => {
  const angle = turn * Math.PI * 2 - Math.PI / 2;
  return `${(50 + Math.cos(angle) * radius * 100).toFixed(1)}% ${(50 + Math.sin(angle) * radius * 100).toFixed(1)}%`;
};

const BURST_CLIP = `polygon(${SPIKES.flatMap((length, i) => [
  burstPoint(i / SPIKES.length, 0.5 * length),
  burstPoint((i + 0.5) / SPIKES.length, BURST_CORE),
]).join(', ')})`;

/**
 * Fires once a run of clicks lands in quick succession. Kept out of whatever element carries it: the
 * gesture is a way in, not a property of the thing drawn, so changing it (or moving it elsewhere)
 * shouldn't mean editing artwork.
 */
function useSecretGesture(onReveal: () => void) {
  const clicks = useRef(0);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => () => clearTimeout(timer.current), []);

  return () => {
    clearTimeout(timer.current);
    clicks.current += 1;
    if (clicks.current >= SECRET_CLICKS) {
      clicks.current = 0;
      onReveal();
      return;
    }
    // A slow, idle click shouldn't count towards the next run.
    timer.current = setTimeout(() => {
      clicks.current = 0;
    }, SECRET_TIMEOUT_MS);
  };
}

/**
 * The "VS" where the bolt crosses the middle. Two concentric bursts rather than one, which gives the
 * shape a drawn ink line all the way round its spikes - a border can't follow a clip-path, and a
 * drop-shadow only offsets one way.
 */
function VersusBadge({ onClick }: { onClick: () => void }) {
  return (
    <Box
      onClick={onClick}
      sx={{
        // Laid out by the centred stack, not pinned itself - two things both absolutely centred would
        // sit on top of each other rather than above and below.
        position: 'relative',
        flexShrink: 0,
        transform: 'rotate(-8deg)',
        width: { xs: 112, sm: 150, md: 214 },
        aspectRatio: '1',
        filter: 'drop-shadow(6px 7px 0 rgba(0,0,0,0.5))',
        // No pointer cursor: the gesture is meant to be unadvertised.
        userSelect: 'none',
      }}
    >
      <Box sx={{ position: 'absolute', inset: 0, clipPath: BURST_CLIP, bgcolor: INK }} />
      <Box
        sx={{
          position: 'absolute',
          inset: '7%',
          clipPath: BURST_CLIP,
          background: 'radial-gradient(circle at 40% 32%, #ffe98a 0%, #ffd23f 28%, #ff8c1a 62%, #ef3b12 100%)',
        }}
      />
      <Box sx={{ position: 'absolute', inset: 0, display: 'grid', placeItems: 'center' }}>
        <Typography
          component="span"
          sx={{
            ...COMIC_TEXT,
            color: '#12060b',
            lineHeight: 1,
            fontSize: { xs: '2.2rem', sm: '3rem', md: '4rem' },
            letterSpacing: '-0.06em',
            transform: 'rotate(2deg)',
            textShadow: '0 -2px 0 rgba(255,255,255,0.5), 3px 4px 0 rgba(0,0,0,0.35)',
          }}
        >
          VS
        </Typography>
      </Box>
    </Box>
  );
}

/** The hidden editor. Everything on the scoreboard is set here; nothing accumulates on its own. */
function ScoreboardEditor({
  scoreboard,
  onClose,
}: {
  scoreboard: UnmatchedScoreboard;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  // Initialised straight from the scoreboard - this component only exists while the dialog is open, so
  // there is no stale state to reset and no effect to do the resetting.
  const [fiukWins, setFiukWins] = useState(String(scoreboard.fiukWins));
  const [lanyokWins, setLanyokWins] = useState(String(scoreboard.lanyokWins));
  // 'draw' rather than a cleared selection: a draw is a real outcome to pick, not the absence of one.
  // It stores as null, which is also the never-recorded state - both correctly show no crown.
  const [lastVictor, setLastVictor] = useState<UnmatchedTeam | 'draw'>(scoreboard.lastVictor ?? 'draw');
  const [picks, setPicks] = useState<Record<string, string>>(
    Object.fromEntries(scoreboard.picks.map((p) => [p.playerName, p.character])),
  );

  const save = useMutation({
    mutationFn: () =>
      saveUnmatchedScoreboard({
        fiukWins: Number(fiukWins) || 0,
        lanyokWins: Number(lanyokWins) || 0,
        lastVictor: lastVictor === 'draw' ? null : lastVictor,
        // Only players who have been given a character are sent; the rest show as unset.
        picks: ALL_PLAYERS.filter((player) => picks[player]).map((player) => ({
          playerName: player,
          character: picks[player],
        })),
      }),
    onSuccess: (saved) => {
      queryClient.setQueryData(['unmatched'], saved);
      onClose();
    },
  });

  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>Eredmény</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {save.error && <Alert severity="error">{extractErrorMessage(save.error)}</Alert>}

          <Stack direction="row" spacing={2}>
            <TextField
              label={TEAMS[0].label}
              type="number"
              size="small"
              value={fiukWins}
              onChange={(e) => setFiukWins(e.target.value)}
              slotProps={{ htmlInput: { min: 0, inputMode: 'numeric' } }}
              fullWidth
            />
            <TextField
              label={TEAMS[1].label}
              type="number"
              size="small"
              value={lanyokWins}
              onChange={(e) => setLanyokWins(e.target.value)}
              slotProps={{ htmlInput: { min: 0, inputMode: 'numeric' } }}
              fullWidth
            />
          </Stack>

          <Stack spacing={0.5}>
            <Typography variant="caption" color="text.secondary">
              Ki nyerte az utolsó meccset?
            </Typography>
            <ToggleButtonGroup
              size="small"
              exclusive
              value={lastVictor}
              // Ignores a null: clicking the active button would otherwise clear the group, leaving
              // no outcome selected at all. One of the three is always on.
              onChange={(_, value: UnmatchedTeam | 'draw' | null) => value && setLastVictor(value)}
              fullWidth
            >
              <ToggleButton value={TEAMS[0].victor}>{TEAMS[0].label}</ToggleButton>
              <ToggleButton value="draw">Döntetlen</ToggleButton>
              <ToggleButton value={TEAMS[1].victor}>{TEAMS[1].label}</ToggleButton>
            </ToggleButtonGroup>
          </Stack>

          {ALL_PLAYERS.map((player) => (
            <TextField
              key={player}
              select
              size="small"
              label={player}
              value={picks[player] ?? ''}
              onChange={(e) => setPicks((current) => ({ ...current, [player]: e.target.value }))}
              fullWidth
            >
              <MenuItem value="">
                <em>nincs megadva</em>
              </MenuItem>
              {CHARACTER_NAMES.map((name) => (
                <MenuItem key={name} value={name}>
                  {name}
                </MenuItem>
              ))}
            </TextField>
          ))}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Mégse</Button>
        <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>
          Mentés
        </Button>
      </DialogActions>
    </Dialog>
  );
}
