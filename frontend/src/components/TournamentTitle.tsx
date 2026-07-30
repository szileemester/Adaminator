import { Typography } from '@mui/material';
import { TOURNAMENT_NAME_MAX_LENGTH } from '../api/types';
import { breakAnywhereSx } from '../theme';

/** Beyond this share of the limit a name is "long" and drops a size. Relative, so the two move together. */
const LONG_NAME_RATIO = 0.8;

/**
 * A tournament's name as a page heading, on the admin detail page and the public page.
 *
 * As well as breaking anywhere, the size steps down for a long name: wrapping alone makes one fit, but
 * at full heading size it fills a phone before the page has started. A normal name - the vast majority
 * - is untouched, so headings don't look arbitrarily inconsistent.
 */
export function TournamentTitle({ name }: { name: string }) {
  const long = name.length > TOURNAMENT_NAME_MAX_LENGTH * LONG_NAME_RATIO;
  return (
    <Typography
      variant="h4"
      sx={{
        fontSize: long ? { xs: '1.125rem', sm: '1.5rem' } : { xs: '1.5rem', sm: '2.125rem' },
        ...breakAnywhereSx,
      }}
    >
      {name}
    </Typography>
  );
}
