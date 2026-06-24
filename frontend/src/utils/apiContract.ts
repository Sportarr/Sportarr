export const API_CONTRACT_FAILURE_EVENT = 'sportarr:api-contract-failure';

export interface ApiContractFailureDetail {
  url: string;
  method: string;
  status: number;
  contentType: string;
  message: string;
}

export function emitApiContractFailure(detail: ApiContractFailureDetail): void {
  if (typeof window === 'undefined') {
    return;
  }

  window.dispatchEvent(new CustomEvent<ApiContractFailureDetail>(API_CONTRACT_FAILURE_EVENT, { detail }));
}

export function isHtmlResponseContentType(contentType: string | null | undefined): boolean {
  return !!contentType && contentType.toLowerCase().includes('text/html');
}

/**
 * Classify an HTML API response into a user-facing message based on HTTP status.
 *
 * 5xx  → server-side error (HTML error page from the app or a gateway)
 * 4xx  → proxy/routing issued its own HTML error page instead of the app's JSON
 * 2xx  → SPA index.html served instead of JSON (classic UrlBase misconfiguration)
 * other → generic unexpected HTML
 */
export function htmlResponseMessage(status: number): string {
  if (status >= 500) {
    return `Server error (HTTP ${status}). Check the Sportarr server logs for details.`;
  }
  if (status >= 400) {
    return `Proxy or gateway returned an HTML error page (HTTP ${status}). Verify reverse proxy configuration.`;
  }
  if (status >= 200 && status < 300) {
    return 'The server returned an HTML page instead of JSON. Check UrlBase and reverse proxy path rewriting.';
  }
  return `Unexpected HTML response (HTTP ${status}).`;
}
