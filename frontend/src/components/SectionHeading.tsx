import { Box, Divider, Typography } from '@mui/material';

/**
 * A titled rule above a group of fields. Shared by the tournament form and the detail page's
 * Overview so the same settings carry the same headings in both places; each caller lays its own
 * body out, since one holds inputs and the other label/value rows.
 */
export function SectionHeading({ title }: { title: string }) {
  return (
    <Box>
      <Typography variant="subtitle2" color="text.secondary">
        {title}
      </Typography>
      <Divider sx={{ mt: 0.5 }} />
    </Box>
  );
}
