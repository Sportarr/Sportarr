import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import App from './App.tsx';

// Initialize window.Fightarr from backend
async function init() {
  try {
    const initializeUrl = `${window.Fightarr?.urlBase || ''}/initialize.json?t=${Date.now()}`;
    const response = await fetch(initializeUrl);
    window.Fightarr = await response.json();
  } catch (error) {
    console.error('Failed to initialize Fightarr config:', error);
    // Fallback defaults
    window.Fightarr = {
      apiRoot: '/api/v1',
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
