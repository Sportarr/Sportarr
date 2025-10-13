import axios from 'axios';

// Get API configuration from window.Fightarr (set by backend)
declare global {
  interface Window {
    Fightarr: {
      apiRoot: string;
      apiKey: string;
      urlBase: string;
      version: string;
    };
  }
}

const apiClient = axios.create({
  baseURL: typeof window !== 'undefined' ? window.Fightarr?.apiRoot : '/api/v1',
  headers: {
    'Content-Type': 'application/json',
  },
});

// Add API key to all requests
apiClient.interceptors.request.use((config) => {
  if (typeof window !== 'undefined' && window.Fightarr?.apiKey) {
    config.headers['X-Api-Key'] = window.Fightarr.apiKey;
  }
  return config;
});

export default apiClient;
