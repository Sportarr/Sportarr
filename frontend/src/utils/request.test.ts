import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { createRequestUrl, getImageUrl } from './request';

describe('Request URL Builder (UrlBase Support)', () => {
  let originalWindow: any;

  beforeEach(() => {
    // Store original window.Sportarr
    originalWindow = (window as any).Sportarr;
  });

  afterEach(() => {
    // Restore original window.Sportarr
    if (originalWindow) {
      (window as any).Sportarr = originalWindow;
    } else {
      delete (window as any).Sportarr;
    }
  });

  describe('createRequestUrl', () => {
    it('should return path as-is when UrlBase is empty', () => {
      (window as any).Sportarr = { urlBase: '' };
      expect(createRequestUrl('/api/leagues')).toBe('/api/leagues');
      expect(createRequestUrl('/initialize.json')).toBe('/initialize.json');
    });

    it('should prepend UrlBase when configured', () => {
      (window as any).Sportarr = { urlBase: '/sportarr' };
      expect(createRequestUrl('/api/leagues')).toBe('/sportarr/api/leagues');
      expect(createRequestUrl('/initialize.json')).toBe('/sportarr/initialize.json');
    });

    it('should handle paths without leading slash', () => {
      (window as any).Sportarr = { urlBase: '/my-subpath' };
      expect(createRequestUrl('api/test')).toBe('/my-subpath/api/test');
      expect(createRequestUrl('logo.png')).toBe('/my-subpath/logo.png');
    });

    it('should gracefully handle missing window.Sportarr', () => {
      delete (window as any).Sportarr;
      expect(createRequestUrl('/api/leagues')).toBe('/api/leagues');
    });

    it('should work with complex paths', () => {
      (window as any).Sportarr = { urlBase: '/sportarr' };
      expect(createRequestUrl('/api/system/backup/upload')).toBe('/sportarr/api/system/backup/upload');
      expect(createRequestUrl('/api/leagues/123')).toBe('/sportarr/api/leagues/123');
    });
  });

  describe('getImageUrl', () => {
    it('should prepend / and UrlBase for image filenames', () => {
      (window as any).Sportarr = { urlBase: '/sportarr' };
      expect(getImageUrl('logo-64.png')).toBe('/sportarr/logo-64.png');
      expect(getImageUrl('error.png')).toBe('/sportarr/error.png');
      expect(getImageUrl('404.png')).toBe('/sportarr/404.png');
    });

    it('should return root path for images when UrlBase is empty', () => {
      (window as any).Sportarr = { urlBase: '' };
      expect(getImageUrl('logo-64.png')).toBe('/logo-64.png');
    });

    it('should handle missing window.Sportarr', () => {
      delete (window as any).Sportarr;
      expect(getImageUrl('error.png')).toBe('/error.png');
    });
  });
});
