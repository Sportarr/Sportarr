import axios from 'axios';

// Get API configuration from window.Sportarr (set by backend)
declare global {
  interface Window {
    Sportarr: {
      apiRoot: string;
      apiKey: string;
      urlBase: string;
      version: string;
    };
  }
}

const apiClient = axios.create({
  baseURL: typeof window !== 'undefined' ? (window.Sportarr?.apiRoot ?? '/api') : '/api',
  headers: {
    'Content-Type': 'application/json',
  },
});

// Add API key to all requests
apiClient.interceptors.request.use((config) => {
  if (typeof window !== 'undefined' && window.Sportarr?.apiKey) {
    config.headers['X-Api-Key'] = window.Sportarr.apiKey;
  }
  return config;
});

export default apiClient;
