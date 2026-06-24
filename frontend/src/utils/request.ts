/**
 * Unified request URL builder for reverse proxy subpath support.
 * 
 * Constructs full request URLs using window.Sportarr.urlBase (set by backend),
 * ensuring all API calls and assets are routed correctly when deployed behind
 * a reverse proxy at a subpath (e.g., /sportarr).
 * 
 * Usage:
 *   fetch(createRequestUrl('/api/auth/check'))
 *   <img src={getImageUrl('logo-64.png')} />
 */

/**
 * Create a full request URL with UrlBase prefix if configured.
 * 
 * @param path - The request path (e.g., '/api/auth/check', 'initialize.json', '/logo-64.png')
 * @returns Full URL with UrlBase prefix (e.g., '/sportarr/api/auth/check')
 * 
 * @example
 *   createRequestUrl('/api/leagues') // Without UrlBase: '/api/leagues'
 *   createRequestUrl('/api/leagues') // With UrlBase='/sportarr': '/sportarr/api/leagues'
 */
export function createRequestUrl(path: string): string {
  // Normalize path to always start with /
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  
  // Get UrlBase from window.Sportarr (set by backend before React loads)
  // Defaults to empty string if not set (graceful degradation to root path)
  const urlBase = typeof window !== 'undefined' ? (window.Sportarr?.urlBase || '') : '';
  
  return `${urlBase}${normalizedPath}`;
}

/**
 * Create a full URL for image assets with UrlBase prefix if configured.
 * Semantic wrapper around createRequestUrl for clarity when dealing with assets.
 * 
 * @param filename - The image filename (e.g., 'logo-64.png', 'error.png')
 * @returns Full URL to the image (e.g., '/sportarr/logo-64.png')
 * 
 * @example
 *   getImageUrl('logo-64.png') // Without UrlBase: '/logo-64.png'
 *   getImageUrl('error.png')   // With UrlBase='/sportarr': '/sportarr/error.png'
 */
export function getImageUrl(filename: string): string {
  return createRequestUrl(`/${filename}`);
}
