import { useState } from 'react';
import { Box, Button, Menu, MenuItem, Tooltip, Typography } from '@mui/material';
import AddReactionIcon from '@mui/icons-material/AddReaction';

/**
 * The emojis a participant can pick from. Deliberately a small curated set rendered with the OS emoji
 * font rather than a picker library - the bundle is already past Vite's size warning, and a roster of
 * at most 32 needs variety, not exhaustiveness.
 */
const PARTICIPANT_EMOJIS: readonly string[] = [
  '🦊', '🐻', '🐯', '🦁', '🐺', '🐱', '🐶', '🐸',
  '🐵', '🦉', '🦅', '🦈', '🐙', '🦖', '🐲', '🦄',
  '⚽', '🏀', '🏈', '⚾', '🎾', '🏐', '🏓', '🏸',
  '🥊', '♟️', '🎯', '🎳', '🏹', '🎣', '🛹', '🏆',
  '🔥', '⚡', '⭐', '💎', '🚀', '🎮', '🎲', '🃏',
  '👑', '🎩', '🦾', '🍀', '🌊', '🌵', '🍕', '🍩',
];

/** Shared so the locked badge and the interactive button stay the same size in a form row. */
const SLOT_SX = {
  minWidth: 56,
  height: 40,
  px: 1,
  flexShrink: 0,
  lineHeight: 1,
  borderColor: 'divider',
} as const;

/**
 * Picks one emoji from the curated set. Because an emoji is write-once, a participant who already has
 * one gets a disabled button instead - the API rejects a change either way, the UI just shouldn't
 * invite one.
 */
export function EmojiPicker({
  value,
  onChange,
  locked = false,
  disabled = false,
}: {
  value: string | null;
  onChange: (emoji: string | null) => void;
  /** True once the emoji is permanent (already saved on the server). */
  locked?: boolean;
  disabled?: boolean;
}) {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);
  const open = anchorEl !== null;

  const select = (emoji: string | null) => {
    onChange(emoji);
    setAnchorEl(null);
  };

  const tooltip = locked
    ? "An emoji can't be changed once it has been set"
    : value
      ? 'Change emoji (not yet saved)'
      : 'Pick an emoji (optional)';

  return (
    <>
      <Tooltip title={tooltip}>
        {/* A disabled MUI button swallows pointer events, so the tooltip needs a real wrapper element. */}
        <span>
          <Button
            variant="outlined"
            color="inherit"
            onClick={(e) => setAnchorEl(e.currentTarget)}
            disabled={disabled || locked}
            aria-label={value ? 'Change emoji' : 'Pick an emoji'}
            sx={{ ...SLOT_SX, fontSize: value ? '1.25rem' : undefined, color: value ? 'inherit' : 'text.secondary' }}
          >
            {value ?? <AddReactionIcon fontSize="small" />}
          </Button>
        </span>
      </Tooltip>

      <Menu anchorEl={anchorEl} open={open} onClose={() => setAnchorEl(null)}>
        <Box sx={{ px: 1, pb: 0.5 }}>
          <Typography variant="caption" color="text.secondary">
            Chosen once - it can't be changed later.
          </Typography>
        </Box>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(8, 1fr)', px: 0.5, pb: 0.5, maxWidth: 320 }}>
          {PARTICIPANT_EMOJIS.map((emoji) => (
            <MenuItem
              key={emoji}
              selected={emoji === value}
              onClick={() => select(emoji)}
              aria-label={`Choose ${emoji}`}
              sx={{ minWidth: 0, justifyContent: 'center', fontSize: '1.25rem', lineHeight: 1, px: 0, py: 0.5, borderRadius: 1 }}
            >
              {emoji}
            </MenuItem>
          ))}
        </Box>
        {value && <MenuItem onClick={() => select(null)}>Clear</MenuItem>}
      </Menu>
    </>
  );
}
