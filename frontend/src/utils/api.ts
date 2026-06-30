// API utility for making authenticated requests to Sportarr backend

import { createRequestUrl } from './request';
import { emitApiContractFailure, isHtmlResponseContentType, htmlResponseMessage } from './apiContract';

function ensureApiJsonContract(response: Response, method: string, url: string): void {
  const contentType = response.headers.get('content-type');
  const isHtml = isHtmlResponseContentType(contentType);
  const isProblemJson = !!contentType && contentType.toLowerCase().includes('application/problem+json');

  if (!isHtml && !(isProblemJson && response.status === 404)) {
    return;
  }

  const message = isHtml
    ? htmlResponseMessage(response.status)
    : 'API endpoint was not found. Verify UrlBase and reverse proxy rewrite rules.';

  emitApiContractFailure({
    url,
    method,
    status: response.status,
    contentType: contentType || 'unknown',
    message,
  });

  throw new Error(message);
}

/**
 * Fetch the API key from the initialize endpoint
 */
export async function getApiKey(): Promise<string> {
  if (cachedApiKey) {
    return cachedApiKey;
  }

  try {
    const initializeUrl = createRequestUrl('/initialize.json');
    const response = await fetch(initializeUrl, {
      headers: { Accept: 'application/json' },
    });
    ensureApiJsonContract(response, 'GET', initializeUrl);
    if (!response.ok) {
      throw new Error('Failed to fetch initialize data');
    }
    const data = await response.json();
    cachedApiKey = data.apiKey;
    if (!cachedApiKey) {
      throw new Error('API key not found in initialize data');
    }
    return cachedApiKey;
  } catch (error) {
    console.error('Failed to get API key:', error);
    throw error;
  }
}

let cachedApiKey: string | null = null;

/**
 * Make an authenticated API request with the API key header
 */
export async function apiRequest(url: string, options: RequestInit = {}): Promise<Response> {
  const apiKey = await getApiKey();
  const method = options.method ?? 'GET';

  const headers = new Headers(options.headers);
  headers.set('X-Api-Key', apiKey);
  if (!headers.has('Accept')) {
    headers.set('Accept', 'application/json');
  }

  const fullUrl = createRequestUrl(url);
  const response = await fetch(fullUrl, {
    ...options,
    headers,
    credentials: 'include',
  });

  ensureApiJsonContract(response, method, fullUrl);
  return response;
}

/**
 * Make an authenticated GET request
 */
export async function apiGet(url: string): Promise<Response> {
  return apiRequest(url, { method: 'GET' });
}

/**
 * Make an authenticated POST request
 */
export async function apiPost(url: string, body: any): Promise<Response> {
  return apiRequest(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

/**
 * Make an authenticated PUT request
 */
export async function apiPut(url: string, body: any): Promise<Response> {
  return apiRequest(url, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

/**
 * Make an authenticated DELETE request
 */
export async function apiDelete(url: string): Promise<Response> {
  return apiRequest(url, { method: 'DELETE' });
}

/**
 * Make an authenticated DELETE request with a JSON body
 */
export async function apiDeleteWithBody(url: string, body: any): Promise<Response> {
  return apiRequest(url, {
    method: 'DELETE',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}
