import axios from 'axios';
import { emitApiContractFailure, isHtmlResponseContentType, htmlResponseMessage } from '../utils/apiContract';
import { createRequestUrl } from '../utils/request';

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
  // Use urlBase + apiRoot for reverse proxy support
  // e.g., urlBase="/sportarr", apiRoot="" -> baseURL="/sportarr/api"
  baseURL: typeof window !== 'undefined'
    ? `${window.Sportarr?.urlBase || ''}${window.Sportarr?.apiRoot || '/api'}`
    : '/api',
  headers: {
    'Content-Type': 'application/json',
  },
});

const API_KEY_PRIME_ERROR_MESSAGE = 'Failed to fetch API key from initialize endpoint after authentication.';

let apiKeyPrimePromise: Promise<string> | null = null;

async function fetchApiKeyFromInitialize(): Promise<string> {
  const initializeUrl = createRequestUrl('/initialize.json');
  const response = await fetch(initializeUrl, {
    credentials: 'include',
    headers: {
      Accept: 'application/json',
    },
  });

  const contentType = response.headers.get('content-type') || '';
  if (isHtmlResponseContentType(contentType)) {
    const message = htmlResponseMessage(response.status);
    emitApiContractFailure({
      url: initializeUrl,
      method: 'GET',
      status: response.status,
      contentType,
      message,
    });
    throw new Error(message);
  }

  if (!response.ok) {
    throw new Error(`${API_KEY_PRIME_ERROR_MESSAGE} (${response.status})`);
  }

  const data = await response.json();
  const apiKey = String(data?.apiKey || '');

  if (typeof window !== 'undefined') {
    window.Sportarr = {
      ...(window.Sportarr || { apiRoot: '', apiKey: '', urlBase: '', version: 'unknown' }),
      ...data,
      apiKey,
    };
  }

  return apiKey;
}

async function ensureApiKey(): Promise<string> {
  const existingKey = typeof window !== 'undefined' ? (window.Sportarr?.apiKey || '') : '';
  if (existingKey) {
    return existingKey;
  }

  if (!apiKeyPrimePromise) {
    apiKeyPrimePromise = fetchApiKeyFromInitialize().finally(() => {
      apiKeyPrimePromise = null;
    });
  }

  return apiKeyPrimePromise;
}

export async function primeApiKey(): Promise<void> {
  await ensureApiKey();
}

export function clearPrimedApiKey(): void {
  apiKeyPrimePromise = null;
  if (typeof window !== 'undefined' && window.Sportarr) {
    window.Sportarr.apiKey = '';
  }
}

// Add API key to all requests
apiClient.interceptors.request.use(async (config) => {
  const apiKey = await ensureApiKey();
  if (apiKey) {
    config.headers['X-Api-Key'] = apiKey;
  }

  if (!config.headers.Accept) {
    config.headers.Accept = 'application/json';
  }

  return config;
});

apiClient.interceptors.response.use(
  (response) => {
    const contentType = String(response.headers['content-type'] || '');
    const isHtml = isHtmlResponseContentType(contentType);
    const isProblemJson404 = contentType.toLowerCase().includes('application/problem+json') && response.status === 404;

    if (!isHtml && !isProblemJson404) {
      return response;
    }

    const message = isHtml
      ? htmlResponseMessage(response.status)
      : 'API endpoint was not found. Verify UrlBase and reverse proxy rewrite rules.';

    emitApiContractFailure({
      url: String(response.config?.url || ''),
      method: String(response.config?.method || 'get').toUpperCase(),
      status: response.status,
      contentType,
      message,
    });

    return Promise.reject(new Error(message));
  },
  (error) => {
    const contentType = String(error?.response?.headers?.['content-type'] || '');
    const isHtml = isHtmlResponseContentType(contentType);
    const isProblemJson404 = contentType.toLowerCase().includes('application/problem+json')
      && Number(error?.response?.status || 0) === 404;

    if (isHtml || isProblemJson404) {
      const errorStatus = Number(error?.response?.status || 0);
      const errorMessage = isHtml
        ? htmlResponseMessage(errorStatus)
        : 'API endpoint was not found. Verify UrlBase and reverse proxy rewrite rules.';

      emitApiContractFailure({
        url: String(error?.config?.url || ''),
        method: String(error?.config?.method || 'get').toUpperCase(),
        status: errorStatus,
        contentType,
        message: errorMessage,
      });
      error.message = errorMessage;
    }

    return Promise.reject(error);
  }
);

export default apiClient;
