import { Stack, Typography } from '@mui/material';
import type { SxProps, Theme } from '@mui/material';

/**
 * A participant's emoji (when they picked one) followed by their name - the single rendering used
 * everywhere a participant appears, so the bracket, the group tables, the standings and the
 * leaderboard stay consistent.
 *
 * The `emoji` is rendered as text rather than an icon so it keeps the OS emoji font's colors, and
 * the name keeps `noWrap` so long names ellipsize instead of wrapping inside a bracket card.
 */
export function ParticipantLabel({
  name,
  emoji,
  sx,
}: {
  name: string;
  emoji: string | null | undefined;
  sx?: SxProps<Theme>;
}) {
  return (
    // Spacing stays tight everywhere so the 168px bracket cards, the narrowest site, need no special case.
    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', minWidth: 0 }}>
      {emoji && (
        <Typography component="span" aria-hidden sx={{ fontSize: '1rem', lineHeight: 1, flexShrink: 0 }}>
          {emoji}
        </Typography>
      )}
      <Typography variant="body2" noWrap sx={sx}>
        {name}
      </Typography>
    </Stack>
  );
}
