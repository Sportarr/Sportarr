import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import LeagueLogoWatermark from '../LeagueLogoWatermark';

const resolveLogoWatermarkPresentationMock = vi.fn();

vi.mock('../../utils/logoWatermark', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/logoWatermark')>();
  return {
    ...actual,
    resolveLogoWatermarkPresentation: (...args: unknown[]) => resolveLogoWatermarkPresentationMock(...args),
  };
});

describe('LeagueLogoWatermark', () => {
  const OriginalImage = window.Image;

  beforeEach(() => {
    resolveLogoWatermarkPresentationMock.mockReset();
    resolveLogoWatermarkPresentationMock.mockReturnValue({ emphasis: 'standard', scale: 1 });

    class MockImage {
      onload: null | (() => void) = null;
      onerror: null | (() => void) = null;
      decoding = '';
      naturalWidth = 320;
      naturalHeight = 100;

      set src(_value: string) {
        queueMicrotask(() => { this.onload?.(); });
      }
    }

    Object.defineProperty(window, 'Image', { configurable: true, writable: true, value: MockImage });
  });

  afterEach(() => {
    Object.defineProperty(window, 'Image', { configurable: true, writable: true, value: OriginalImage });
  });

  it('caps ultra-wide compact scale at 0.935 and routes the image through the proxy', async () => {
    resolveLogoWatermarkPresentationMock.mockReturnValue({ emphasis: 'ultraWide', scale: 1.58 });
    const logoUrl = 'https://example.com/logo.png';

    render(<LeagueLogoWatermark logoUrl={logoUrl} variant="compact" />);

    const img = screen.getByTestId(`calendar-event-logo-${logoUrl}`);
    await waitFor(() => {
      expect(img).toHaveStyle({ transform: 'scale(0.935)', transformOrigin: 'right center' });
    });
    expect(resolveLogoWatermarkPresentationMock).toHaveBeenCalledTimes(1);
    expect(img).toHaveAttribute('src', '/api/iptv/stream/url?url=https%3A%2F%2Fexample.com%2Flogo.png');
  });

  it('caps wide compact scale at a tighter bound than ultra-wide', async () => {
    resolveLogoWatermarkPresentationMock.mockReturnValue({ emphasis: 'wide', scale: 1.4 });
    const logoUrl = 'https://example.com/wide-logo.png';

    render(<LeagueLogoWatermark logoUrl={logoUrl} variant="compact" />);

    const img = screen.getByTestId(`calendar-event-logo-${logoUrl}`);
    await waitFor(() => {
      expect(img).toHaveStyle({ transform: 'scale(0.833)', transformOrigin: 'right center' });
    });
  });

  it('passes standard scale through unchanged when no cap applies', async () => {
    resolveLogoWatermarkPresentationMock.mockReturnValue({ emphasis: 'standard', scale: 0.9 });
    const logoUrl = 'https://example.com/square-logo.png';

    render(<LeagueLogoWatermark logoUrl={logoUrl} variant="compact" />);

    const img = screen.getByTestId(`calendar-event-logo-${logoUrl}`);
    await waitFor(() => {
      expect(img).toHaveStyle({ transform: 'scale(0.9)' });
    });
  });

  it('applies a different scale cap for the agenda variant', async () => {
    resolveLogoWatermarkPresentationMock.mockReturnValue({ emphasis: 'ultraWide', scale: 1 });
    const logoUrl = 'https://example.com/agenda-wordmark.png';

    render(<LeagueLogoWatermark logoUrl={logoUrl} variant="agenda" />);

    const img = screen.getByTestId(`calendar-event-logo-${logoUrl}`);
    await waitFor(() => {
      expect(img).toHaveStyle({ transform: 'scale(0.85)', transformOrigin: 'right center' });
    });
  });

  it('does not re-analyze the same image on remount', async () => {
    resolveLogoWatermarkPresentationMock.mockReturnValue({ emphasis: 'ultraWide', scale: 1.2 });
    const logoUrl = 'https://example.com/cached-logo.png';

    const { unmount } = render(<LeagueLogoWatermark logoUrl={logoUrl} variant="compact" />);
    await waitFor(() => expect(resolveLogoWatermarkPresentationMock).toHaveBeenCalledTimes(1));
    unmount();

    render(<LeagueLogoWatermark logoUrl={logoUrl} variant="agenda" />);
    await screen.findByTestId(`calendar-event-logo-${logoUrl}`);
    expect(resolveLogoWatermarkPresentationMock).toHaveBeenCalledTimes(1);
  });
});
