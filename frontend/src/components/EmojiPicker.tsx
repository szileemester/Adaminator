import { useState } from 'react';
import { Box, Button, Menu, MenuItem, Tooltip } from '@mui/material';
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
  '🐼', '🐨', '🐧', '🦋', '🐝', '🦂', '🐍', '🦎',
  '🍔', '🍟', '🌮', '🍦', '🍎', '🍇', '🥑', '🍉',
  '👽', '🤖', '🎃', '👻', '🧙', '🗿', '🛸', '🎪',
  '⚔️', '🛡️', '🪄', '🔮', '🏰', '💀', '🧝', '🐉',
];

/**
 * A small MUI outlined input is 40px tall, and the picker sits beside one. Exported so a caller
 * laying out a row can line other controls up with it rather than re-pinning the same number.
 */
export const PICKER_HEIGHT = 40;

/** Shared so the button keeps one size in a form row whether or not an emoji has been chosen. */
const SLOT_SX = {
  minWidth: 56,
  height: PICKER_HEIGHT,
  px: 1,
  flexShrink: 0,
  lineHeight: 1,
  borderColor: 'divider',
} as const;

/** Picks one emoji from the curated set, or clears the one already chosen. */
export function EmojiPicker({
  value,
  onChange,
  disabled = false,
}: {
  value: string | null;
  onChange: (emoji: string | null) => void;
  disabled?: boolean;
}) {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);
  const open = anchorEl !== null;

  const select = (emoji: string | null) => {
    onChange(emoji);
    setAnchorEl(null);
  };

  return (
    <>
      <Tooltip title={value ? 'Change emoji' : 'Pick an emoji (optional)'}>
        {/* A disabled MUI button swallows pointer events, so the tooltip needs a real wrapper element. */}
        <span>
          <Button
            variant="outlined"
            color="inherit"
            onClick={(e) => setAnchorEl(e.currentTarget)}
            disabled={disabled}
            aria-label={value ? 'Change emoji' : 'Pick an emoji'}
            sx={{ ...SLOT_SX, fontSize: value ? '1.25rem' : undefined, color: value ? 'inherit' : 'text.secondary' }}
          >
            {value ?? <AddReactionIcon fontSize="small" />}
          </Button>
        </span>
      </Tooltip>

      {/*
        Built only while open. A closed Menu renders nothing, but its children are still constructed
        on every render - 80 items and 80 sx objects. The roster editor puts one picker in every panel
        and re-renders them all on each keystroke, so that is thousands of throwaway objects per
        character typed at a full roster.
      */}
      <Menu anchorEl={anchorEl} open={open} onClose={() => setAnchorEl(null)}>
        {open && (
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(8, 1fr)', px: 0.5, py: 0.5, maxWidth: 320 }}>
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
        )}
        {open && value && <MenuItem onClick={() => select(null)}>Clear</MenuItem>}
      </Menu>
    </>
  );
}
