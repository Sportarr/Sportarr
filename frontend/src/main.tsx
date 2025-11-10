import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import App from './App.tsx';

// Initialize window.Sportarr from backend
async function init() {
  try {
    const initializeUrl = `${window.Sportarr?.urlBase || ''}/initialize.json?t=${Date.now()}`;
    const response = await fetch(initializeUrl);
    if (!response.ok) {
      throw new Error(`Failed to fetch initialize.json: ${response.status}`);
    }
    window.Sportarr = await response.json();
    console.log('[INIT] Loaded config from backend:', window.Sportarr);
  } catch (error) {
    console.error('Failed to initialize Sportarr config:', error);
    // Fallback defaults - empty string for apiRoot to avoid double /api/
    window.Sportarr = {
      apiRoot: '',
      apiKey: '',
      urlBase: '',
      version: 'unknown',
    };
  }

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>
  );
}

init();
