import { Typography } from '@mui/material';

/**
 * Explains why seeding and starting are unavailable. Shared by both pre-start previews so they give
 * the same reason, and named after the condition rather than the button it happens to sit under.
 */
export function RosterUnsavedNotice() {
  return (
    <Typography variant="body2" color="warning.main">
      The participant list has unsaved changes. Press "Participants are set" first.
    </Typography>
  );
}
