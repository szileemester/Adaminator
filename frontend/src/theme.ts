import { createTheme } from '@mui/material/styles';

// Dark-by-default, bracket-focused palette (UI/UX guideline: clean and modern). Both schemes
// share the same accent colors for brand consistency; only background/paper differ.
export const theme = createTheme({
  // MUI defaults to colorSchemeSelector: 'media' whenever both a light and dark scheme are
  // defined, which ties the active scheme to the OS's prefers-color-scheme *only* - manual
  // setMode() calls (our toggle button) would then have no visual effect at all. 'class' makes
  // the toggle actually switch the rendered scheme, independent of the OS setting.
  cssVariables: { colorSchemeSelector: 'class' },
  defaultColorScheme: 'dark',
  colorSchemes: {
    dark: {
      palette: {
        primary: { main: '#7c9cff' },
        secondary: { main: '#4dd0e1' },
        background: { default: '#0e1117', paper: '#161b22' },
        success: { main: '#3fb950' },
        warning: { main: '#d29922' },
      },
    },
    light: {
      palette: {
        primary: { main: '#7c9cff' },
        secondary: { main: '#4dd0e1' },
        background: { default: '#f5f6f8', paper: '#ffffff' },
        success: { main: '#3fb950' },
        warning: { main: '#d29922' },
      },
    },
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
        root: ({ theme }) => ({
          border: '1px solid rgba(0,0,0,0.12)',
          ...theme.applyStyles('dark', { borderColor: 'rgba(255,255,255,0.08)' }),
        }),
      },
    },
  },
});
