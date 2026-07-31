import {
  AppBar,
  Box,
  Container,
  IconButton,
  Menu,
  MenuItem,
  Toolbar,
  Typography,
} from '@mui/material';
import { useColorScheme } from '@mui/material/styles';
import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import Brightness4Icon from '@mui/icons-material/Brightness4';
import Brightness7Icon from '@mui/icons-material/Brightness7';
import MenuIcon from '@mui/icons-material/Menu';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import { useState, type ReactNode } from 'react';
import { useAuth } from '../auth/AuthContext';

export function AppLayout({ children }: { children: ReactNode }) {
  const { isAuthenticated, logout } = useAuth();
  const navigate = useNavigate();
  const { mode, setMode } = useColorScheme();
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
  const closeMenu = () => setMenuAnchor(null);

  const handleLogout = () => {
    closeMenu();
    logout();
    navigate('/login', { replace: true });
  };

  return (
    <Box sx={{ minHeight: '100vh' }}>
      <AppBar
        position="sticky"
        color="transparent"
        elevation={0}
        sx={(theme) => ({
          top: 0,
          borderBottom: '1px solid rgba(0,0,0,0.12)',
          bgcolor: 'rgba(255,255,255,0.85)',
          backdropFilter: 'blur(8px)',
          ...theme.applyStyles('dark', {
            borderBottom: '1px solid rgba(255,255,255,0.08)',
            bgcolor: 'rgba(14,17,23,0.85)',
          }),
        })}
      >
        <Toolbar>
          <EmojiEventsIcon sx={{ mr: 1, color: 'primary.main' }} />
          <Typography
            variant="h6"
            component={RouterLink}
            to="/"
            sx={{ flexGrow: 1, color: 'inherit', textDecoration: 'none', fontWeight: 700 }}
          >
            Adaminator
          </Typography>
          <IconButton
            color="inherit"
            onClick={() => setMode(mode === 'dark' ? 'light' : 'dark')}
            aria-label="Toggle light/dark theme"
          >
            {mode === 'dark' ? <Brightness7Icon /> : <Brightness4Icon />}
          </IconButton>
          {isAuthenticated && (
            <>
              <IconButton
                color="inherit"
                edge="end"
                onClick={(e) => setMenuAnchor(e.currentTarget)}
                aria-label="Open menu"
                aria-haspopup="menu"
                aria-expanded={menuAnchor !== null}
              >
                <MenuIcon />
              </IconButton>
              <Menu
                anchorEl={menuAnchor}
                open={menuAnchor !== null}
                onClose={closeMenu}
                anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
                transformOrigin={{ vertical: 'top', horizontal: 'right' }}
              >
                <MenuItem onClick={handleLogout}>Log out</MenuItem>
              </Menu>
            </>
          )}
        </Toolbar>
      </AppBar>
      <Container maxWidth="xl" sx={{ py: 4, px: { xs: 2, sm: 3 } }}>
        {children}
      </Container>
    </Box>
  );
}
