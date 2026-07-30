import { useState } from 'react';
import { Box, Stack, Typography } from '@mui/material';

/**
 * A 2v2 "versus" splash for Unmatched characters, in the style of a comic book cover.
 *
 * Every character image in `src/assets/characters` is picked up automatically, so dropping a new
 * `.webp` in there is the whole job of adding a fighter - there is no list to keep in step. Vite
 * resolves the glob at build time and emits each one as a hashed asset.
 */
const CHARACTERS = Object.entries(
  import.meta.glob<string>('../assets/characters/*.webp', { eager: true, import: 'default', query: '?url' }),
).map(([path, url]) => ({
  url,
  // "../assets/characters/Ancient_Leshen.webp" -> "Ancient Leshen"
  name: path.split('/').pop()!.replace(/\.webp$/, '').replace(/_/g, ' '),
}));

interface Fighter {
  url: string;
  name: string;
}

/** Fighters per team. The layout is size-agnostic - the cards flex - so this is the only place it is stated. */
const TEAM_SIZE = 2;
const FIGHTERS_NEEDED = TEAM_SIZE * 2;

/** Distinct characters for both teams, drawn in order: the leading team first, then the trailing one. */
function drawFighters(): Fighter[] {
  const pool = [...CHARACTERS];
  const drawn: Fighter[] = [];
  // splice on an exhausted pool returns nothing, so a short folder simply yields a short draw.
  for (let i = 0; i < FIGHTERS_NEEDED; i++) {
    drawn.push(...pool.splice(Math.floor(Math.random() * pool.length), 1));
  }
  return drawn;
}

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

/** How far a team's clipped edge can reach in from the bolt's centre line. */
const BOLT_REACH = BOLT_HALF + BOLT_KEYLINE;

type Axis = 'vertical' | 'horizontal';
type Side = 'leading' | 'trailing';

/** One point in the arena, written for whichever axis the bolt currently runs on. */
const point = (axis: Axis, along: number, cross: number) =>
  axis === 'vertical' ? `${cross}% ${along}%` : `${along}% ${cross}%`;

/** Points along one edge of the bolt, offset across the centre line. */
const boltEdge = (axis: Axis, offset: number) =>
  BOLT.map(([along, centre]) => point(axis, along, centre + offset));

/**
 * Everything on one side of the bolt, closed around that side's two far corners. The corners go
 * through the same `point` formatter as the seam, so the axis convention is stated once - written out
 * as literals they were a second copy of it, free to disagree.
 */
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
 * tighter one governs: with the numbers below the trailing side allowed a hand-set 35% just 0.1%
 * of room, and nudging any single BOLT point outward would have silently sliced that team's banner.
 */
const BOLT_CENTRES = BOLT.map(([, centre]) => centre);
const CONTENT_MARGIN = 2;
const CONTENT_EXTENT = `${(
  Math.min(Math.min(...BOLT_CENTRES) - BOLT_REACH, 100 - (Math.max(...BOLT_CENTRES) + BOLT_REACH)) - CONTENT_MARGIN
).toFixed(1)}%`;

/** Radiating comic rays over the flat team colour, like a cheap printed cover. */
const RAYS = 'repeating-conic-gradient(from 0deg at 50% 45%, rgba(255,255,255,0.11) 0deg 3deg, transparent 3deg 10deg)';

/** The one black everything on the page is outlined in - frame, borders, card backing, burst. */
const INK = '#0b0b0b';

/**
 * Height of the app's chrome above and below the page: AppLayout's toolbar plus its Container's
 * vertical padding. Named because it is knowledge borrowed from `components/AppLayout.tsx` - change
 * the toolbar density or that padding and this has to follow, or the arena over- or under-fills.
 */
const APP_CHROME_PX = 160;

/**
 * The page's lettering voice, shared by the team banners and the fighter names so the two read as one
 * typographic system. Size, spacing and shadow stay with each caller; those are what differ.
 */
const COMIC_TEXT = {
  fontWeight: 900,
  fontStyle: 'italic',
  textTransform: 'uppercase',
} as const;

/** Which side of the bolt a team occupies is a property of the team, not a second prop free to disagree. */
const TEAMS = {
  blue: { side: 'leading', label: 'Team Blue', field: '#12459b', banner: '#0d3179', ink: '#7ec8ff' },
  red: { side: 'trailing', label: 'Team Red', field: '#b81f28', banner: '#8d141c', ink: '#ffb3a7' },
} as const satisfies Record<string, { side: Side } & Record<string, string>>;

type TeamKey = keyof typeof TEAMS;

export function UnmatchedPage() {
  // Drawn once per mount, and there is no reroll control - the match-up is the whole page, so a
  // refresh is the way to get another one.
  const [fighters] = useState<Fighter[]>(drawFighters);

  if (fighters.length < FIGHTERS_NEEDED) {
    return (
      <Stack spacing={2}>
        <Typography variant="h4">Unmatched</Typography>
        <Typography color="text.secondary">
          Needs at least {FIGHTERS_NEEDED} character images in <code>src/assets/characters</code>; found{' '}
          {fighters.length}.
        </Typography>
      </Stack>
    );
  }

  return (
    <Box
      sx={{
        position: 'relative',
        // Portrait on a phone (teams stacked), wide on a desktop (teams side by side).
        // Taller on a phone: two stacked teams each need a banner, two cards and two names.
        aspectRatio: responsive('2 / 3', '16 / 9'),
        // On a wide monitor the arena would otherwise run the full container width and dwarf the
        // screen, so it is capped and centred, and kept inside the viewport height as well.
        width: '100%',
        maxWidth: { [STACK_BP]: 1040 },
        maxHeight: { [STACK_BP]: `calc(100vh - ${APP_CHROME_PX}px)` },
        mx: 'auto',
        bgcolor: INK,
        border: `4px solid ${INK}`,
        borderRadius: 1,
        overflow: 'hidden',
        boxShadow: '0 10px 30px rgba(0,0,0,0.45)',
      }}
    >
      <TeamField team="blue" fighters={fighters.slice(0, TEAM_SIZE)} />
      <TeamField team="red" fighters={fighters.slice(TEAM_SIZE)} />

      {/* Above both fields: the white bolt filling the channel they were clipped back from. */}
      <Box
        sx={{
          position: 'absolute',
          inset: 0,
          clipPath: BOLT_CLIP,
          background: 'linear-gradient(160deg, #ffffff 0%, #f2f2f2 55%, #d9d9d9 100%)',
        }}
      />
      <VersusBadge />
    </Box>
  );
}

/** One team's colour field, clipped to its side of the bolt, holding a banner and its cards. */
function TeamField({ team, fighters }: { team: TeamKey; fighters: Fighter[] }) {
  const colors = TEAMS[team];
  const leading = colors.side === 'leading';
  return (
    <Box
      sx={{
        position: 'absolute',
        inset: 0,
        clipPath: FIELD_CLIP[colors.side],
        backgroundColor: colors.field,
        backgroundImage: RAYS,
      }}
    >
      <Box
        sx={{
          position: 'absolute',
          // One shorthand per breakpoint rather than four edges each carrying an 'auto' placeholder:
          // a band across the top or bottom on a phone, a column down one side on a desktop.
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
          gap: responsive(1, 1.5),
          p: responsive(1, 2),
        }}
      >
        <TeamBanner label={colors.label} banner={colors.banner} />
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
            gap: responsive(1, 1.5),
          }}
        >
          {fighters.map((fighter) => (
            <FighterCard key={fighter.name} fighter={fighter} ink={colors.ink} />
          ))}
        </Box>
      </Box>
    </Box>
  );
}

function TeamBanner({ label, banner }: { label: string; banner: string }) {
  return (
    <Typography
      sx={{
        ...COMIC_TEXT,
        // Never squeezed out when the cards want the space - the banner is what names the team.
        flexShrink: 0,
        px: responsive(1, 2),
        py: responsive(0.1, 0.4),
        bgcolor: banner,
        color: '#fff',
        border: `3px solid ${INK}`,
        borderRadius: 0.5,
        transform: 'skewX(-8deg)',
        letterSpacing: '0.08em',
        whiteSpace: 'nowrap',
        fontSize: { xs: '0.7rem', sm: '0.9rem', md: '1.15rem' },
        textShadow: '2px 2px 0 rgba(0,0,0,0.55)',
        boxShadow: '3px 3px 0 rgba(0,0,0,0.45)',
      }}
    >
      {label}
    </Typography>
  );
}

/**
 * One fighter as a framed card. Square, because the source art is - the whole character is shown at
 * a smaller size rather than cropped to fill an oblong. Height comes from the flex share and width
 * follows the aspect ratio, with `maxWidth` pulling both back if the row is the tighter axis.
 */
function FighterCard({ fighter, ink }: { fighter: Fighter; ink: string }) {
  return (
    <Stack sx={{ flex: 1, minWidth: 0, minHeight: 0, alignItems: 'center', justifyContent: 'center', gap: 0.5 }}>
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
        }}
      >
        {/*
          Eagerly fetched at high priority: these four images are the entire page and all of them are
          above the fold, so deferring them would only delay the one thing worth painting.
        */}
        <Box
          component="img"
          src={fighter.url}
          alt={fighter.name}
          fetchPriority="high"
          sx={{
            width: '100%',
            height: '100%',
            objectFit: 'contain',
            display: 'block',
            filter: 'saturate(1.15) contrast(1.06)',
          }}
        />
      </Box>
      <Typography
        noWrap
        sx={{
          ...COMIC_TEXT,
          flexShrink: 0,
          maxWidth: '100%',
          color: '#fff',
          letterSpacing: '0.05em',
          fontSize: { xs: '0.6rem', sm: '0.78rem', md: '0.95rem' },
          textShadow: `0 0 6px ${ink}, 2px 2px 0 rgba(0,0,0,0.75)`,
        }}
      >
        {fighter.name}
      </Typography>
    </Stack>
  );
}

/**
 * Per-spike length multipliers. Hand-drawn bursts are never regular, and a perfectly even star reads
 * as a UI icon rather than ink - these knock each spike out of step just enough to look drawn.
 */
const SPIKES = [1, 0.84, 1.06, 0.79, 1.02, 0.88, 1.1, 0.81, 1.04, 0.86, 1.08, 0.83];

/** How big the burst's solid core is. It has to stay wider than the "VS" that sits on it. */
const BURST_CORE = 0.31;

/** A point on the burst, `turn` in revolutions clockwise from twelve o'clock and `radius` in halves of the box. */
const burstPoint = (turn: number, radius: number) => {
  const angle = turn * Math.PI * 2 - Math.PI / 2;
  return `${(50 + Math.cos(angle) * radius * 100).toFixed(1)}% ${(50 + Math.sin(angle) * radius * 100).toFixed(1)}%`;
};

/** An irregular comic burst: each spike, then the notch that follows it, all the way round. */
const BURST_CLIP = `polygon(${SPIKES.flatMap((length, i) => [
  burstPoint(i / SPIKES.length, 0.5 * length),
  burstPoint((i + 0.5) / SPIKES.length, BURST_CORE),
]).join(', ')})`;

/**
 * The "VS" where the bolt crosses the middle. Two concentric bursts rather than one: a black one at
 * full size and the coloured one inset, which gives the shape a drawn ink line all the way round its
 * spikes - a border can't follow a clip-path, and a drop-shadow only offsets to one side.
 */
function VersusBadge() {
  return (
    <Box
      sx={{
        position: 'absolute',
        top: '50%',
        left: '50%',
        transform: 'translate(-50%, -50%) rotate(-8deg)',
        width: { xs: 112, sm: 150, md: 214 },
        aspectRatio: '1',
        filter: 'drop-shadow(6px 7px 0 rgba(0,0,0,0.5))',
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
            fontWeight: 900,
            fontStyle: 'italic',
            color: '#12060b',
            lineHeight: 1,
            fontSize: { xs: '2.2rem', sm: '3rem', md: '4rem' },
            letterSpacing: '-0.06em',
            transform: 'rotate(2deg)',
            // A light rim above and a hard shadow below: the letters read as stamped into the burst.
            textShadow: '0 -2px 0 rgba(255,255,255,0.5), 3px 4px 0 rgba(0,0,0,0.35)',
          }}
        >
          VS
        </Typography>
      </Box>
    </Box>
  );
}
