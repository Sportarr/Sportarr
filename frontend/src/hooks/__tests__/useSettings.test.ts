import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { useSettings } from '../useSettings';

// Mock fetch globally
const mockFetch = vi.fn();
global.fetch = mockFetch;

describe('useSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should initialize with default value', () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        id: 1,
        hostSettings: '{}',
        lastModified: '2024-01-01T00:00:00Z',
      }),
    });

    const { result } = renderHook(() =>
      useSettings('hostSettings', { port: 1867 })
    );

    expect(result.current[0]).toEqual({ port: 1867 });
    expect(result.current[2]).toBe(true); // loading
  });

  it('should fetch settings on mount', async () => {
    const mockHostSettings = {
      bindAddress: '*',
      port: 1867,
      instanceName: 'Fightarr',
    };

    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        id: 1,
        hostSettings: JSON.stringify(mockHostSettings),
        lastModified: '2024-01-01T00:00:00Z',
      }),
    });

    const { result } = renderHook(() =>
      useSettings('hostSettings', { port: 7878 })
    );

    await waitFor(() => {
      expect(result.current[2]).toBe(false); // loading finished
    });

    expect(mockFetch).toHaveBeenCalledWith('/api/settings');
    expect(result.current[0]).toEqual(mockHostSettings);
  });

  it('should handle fetch error gracefully', async () => {
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

    mockFetch.mockRejectedValueOnce(new Error('Network error'));

    const defaultValue = { port: 7878 };
    const { result } = renderHook(() =>
      useSettings('hostSettings', defaultValue)
    );

    await waitFor(() => {
      expect(result.current[2]).toBe(false); // loading finished
    });

    expect(result.current[0]).toEqual(defaultValue); // Should keep default value
    expect(consoleSpy).toHaveBeenCalled();

    consoleSpy.mockRestore();
  });

  it('should save settings correctly', async () => {
    const currentSettings = {
      id: 1,
      hostSettings: JSON.stringify({ port: 1867 }),
      uiSettings: '{}',
      lastModified: '2024-01-01T00:00:00Z',
    };

    const newHostSettings = {
      port: 8080,
      bindAddress: '0.0.0.0',
    };

    // Mock initial fetch
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => currentSettings,
    });

    const { result } = renderHook(() =>
      useSettings('hostSettings', { port: 1867 })
    );

    await waitFor(() => {
      expect(result.current[2]).toBe(false);
    });

    // Mock fetch for save operation (fetch current settings)
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => currentSettings,
    });

    // Mock save response
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ ...currentSettings, hostSettings: JSON.stringify(newHostSettings) }),
    });

    // Call save function
    await result.current[1](newHostSettings);

    // Check that save was called with correct data
    expect(mockFetch).toHaveBeenCalledWith('/api/settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: expect.stringContaining(JSON.stringify(newHostSettings)),
    });

    // Check that local state was updated
    expect(result.current[0]).toEqual(newHostSettings);
  });

  it('should handle save error', async () => {
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

    // Mock initial fetch
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        id: 1,
        hostSettings: JSON.stringify({ port: 1867 }),
        lastModified: '2024-01-01T00:00:00Z',
      }),
    });

    const { result } = renderHook(() =>
      useSettings('hostSettings', { port: 1867 })
    );

    await waitFor(() => {
      expect(result.current[2]).toBe(false);
    });

    // Mock fetch for save (fails)
    mockFetch.mockRejectedValueOnce(new Error('Save failed'));

    // Attempt to save should throw
    await expect(result.current[1]({ port: 8080 })).rejects.toThrow();

    expect(consoleSpy).toHaveBeenCalled();

    consoleSpy.mockRestore();
  });

  it('should handle malformed JSON gracefully', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        id: 1,
        hostSettings: 'invalid json{',
        lastModified: '2024-01-01T00:00:00Z',
      }),
    });

    const defaultValue = { port: 7878 };
    const { result } = renderHook(() =>
      useSettings('hostSettings', defaultValue)
    );

    await waitFor(() => {
      expect(result.current[2]).toBe(false);
    });

    // Should keep default value when JSON parsing fails
    expect(result.current[0]).toEqual(defaultValue);
  });

  it('should handle 404 response', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 404,
      json: async () => ({}),
    });

    const defaultValue = { port: 7878 };
    const { result } = renderHook(() =>
      useSettings('hostSettings', defaultValue)
    );

    await waitFor(() => {
      expect(result.current[2]).toBe(false);
    });

    expect(result.current[0]).toEqual(defaultValue);
  });

  it('should update loading state correctly', async () => {
    mockFetch.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          setTimeout(() => {
            resolve({
              ok: true,
              json: async () => ({
                id: 1,
                hostSettings: '{"port":1867}',
                lastModified: '2024-01-01T00:00:00Z',
              }),
            });
          }, 100);
        })
    );

    const { result } = renderHook(() =>
      useSettings('hostSettings', { port: 7878 })
    );

    // Initially loading
    expect(result.current[2]).toBe(true);

    // After fetch completes
    await waitFor(() => {
      expect(result.current[2]).toBe(false);
    });
  });

  it('should preserve other settings when saving', async () => {
    const currentSettings = {
      id: 1,
      hostSettings: JSON.stringify({ port: 1867 }),
      uiSettings: JSON.stringify({ theme: 'dark' }),
      securitySettings: JSON.stringify({ apiKey: 'secret' }),
      lastModified: '2024-01-01T00:00:00Z',
    };

    // Mock initial fetch
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => currentSettings,
    });

    const { result } = renderHook(() =>
      useSettings('hostSettings', { port: 1867 })
    );

    await waitFor(() => {
      expect(result.current[2]).toBe(false);
    });

    // Mock fetch for save
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => currentSettings,
    });

    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => currentSettings,
    });

    await result.current[1]({ port: 8080 });

    // Check that other settings were preserved
    const saveCall = mockFetch.mock.calls.find(
      (call) => call[1]?.method === 'PUT'
    );

    expect(saveCall).toBeDefined();

    const savedData = JSON.parse(saveCall![1].body);
    expect(savedData.uiSettings).toBe(currentSettings.uiSettings);
    expect(savedData.securitySettings).toBe(currentSettings.securitySettings);
  });

  it('should handle empty settings key', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        id: 1,
        hostSettings: '',
        lastModified: '2024-01-01T00:00:00Z',
      }),
    });

    const defaultValue = { port: 7878 };
    const { result } = renderHook(() =>
      useSettings('hostSettings', defaultValue)
    );

    await waitFor(() => {
      expect(result.current[2]).toBe(false);
    });

    expect(result.current[0]).toEqual(defaultValue);
  });
});
