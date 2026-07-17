import { AppBar, Box, Button, Container, Toolbar, Typography } from '@mui/material';
import EmojiEventsIcon from '@mui/icons-material/EmojiEvents';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useAuth } from '../auth/AuthContext';

export function AppLayout({ children }: { children: ReactNode }) {
  const { isAuthenticated, logout } = useAuth();
  const navigate = useNavigate();

  const handleLogout = () => {
    logout();
    navigate('/login', { replace: true });
  };

  return (
    <Box sx={{ minHeight: '100vh' }}>
      <AppBar
        position="sticky"
        color="transparent"
        elevation={0}
        sx={{
          top: 0,
          borderBottom: '1px solid rgba(255,255,255,0.08)',
          bgcolor: 'rgba(14,17,23,0.85)',
          backdropFilter: 'blur(8px)',
        }}
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
          {isAuthenticated && (
            <Button color="inherit" onClick={handleLogout}>
              Log out
            </Button>
          )}
        </Toolbar>
      </AppBar>
      <Container maxWidth="lg" sx={{ py: 4 }}>
        {children}
      </Container>
    </Box>
  );
}
