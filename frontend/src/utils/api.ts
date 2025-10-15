// API utility for making authenticated requests to Fightarr backend

let cachedApiKey: string | null = null;

/**
 * Fetch the API key from the initialize endpoint
 */
export async function getApiKey(): Promise<string> {
  if (cachedApiKey) {
    return cachedApiKey;
  }

  try {
    const response = await fetch('/initialize.json');
    if (!response.ok) {
      throw new Error('Failed to fetch initialize data');
    }
    const data = await response.json();
    cachedApiKey = data.apiKey;
    return cachedApiKey;
  } catch (error) {
    console.error('Failed to get API key:', error);
    throw error;
  }
}

/**
 * Make an authenticated API request with the API key header
 */
export async function apiRequest(url: string, options: RequestInit = {}): Promise<Response> {
  const apiKey = await getApiKey();

  const headers = new Headers(options.headers);
  headers.set('X-Api-Key', apiKey);

  return fetch(url, {
    ...options,
    headers,
  });
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
