import { createTheme } from '@mui/material/styles';

// Dark-by-default, bracket-focused palette (UI/UX guideline: clean and modern).
export const theme = createTheme({
  palette: {
    mode: 'dark',
    primary: { main: '#7c9cff' },
    secondary: { main: '#4dd0e1' },
    background: { default: '#0e1117', paper: '#161b22' },
    success: { main: '#3fb950' },
    warning: { main: '#d29922' },
  },
  shape: { borderRadius: 10 },
  typography: {
    fontFamily: '"Inter", "Segoe UI", system-ui, sans-serif',
    h4: { fontWeight: 700 },
    h5: { fontWeight: 700 },
    h6: { fontWeight: 600 },
  },
  components: {
    MuiCard: {
      styleOverrides: {
        root: { border: '1px solid rgba(255,255,255,0.08)' },
      },
    },
  },
});
