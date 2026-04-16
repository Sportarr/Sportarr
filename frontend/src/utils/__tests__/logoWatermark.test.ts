import { afterEach, describe, expect, it, vi } from 'vitest';
import { calculateLogoWatermarkPresentation, getLogoWatermarkImageUrl, resolveLogoWatermarkPresentation } from '../logoWatermark';

describe('calculateLogoWatermarkPresentation', () => {
  it('boosts wide sparse wordmarks more than dense solid logos', () => {
    const ultraWideWordmark = calculateLogoWatermarkPresentation({
      aspectRatio: 4.6,
      coverage: 0.12,
      visibleDensity: 0.22,
    });

    const solidBadge = calculateLogoWatermarkPresentation({
      aspectRatio: 1,
      coverage: 0.58,
      visibleDensity: 0.82,
    });

    expect(ultraWideWordmark.emphasis).toBe('ultraWide');
    expect(ultraWideWordmark.scale).toBeGreaterThan(1.2);
    expect(solidBadge.emphasis).toBe('standard');
    expect(solidBadge.scale).toBeLessThan(0.85);
    expect(ultraWideWordmark.scale).toBeGreaterThan(solidBadge.scale);
  });
});

describe('getLogoWatermarkImageUrl', () => {
  it('routes remote watermark images through the same-origin proxy', () => {
    expect(getLogoWatermarkImageUrl('https://example.com/league-logo.png')).toBe(
      '/api/iptv/stream/url?url=https%3A%2F%2Fexample.com%2Fleague-logo.png',
    );
  });

  it('leaves local and inline watermark images untouched', () => {
    expect(getLogoWatermarkImageUrl('/images/league-logo.png')).toBe('/images/league-logo.png');
    expect(getLogoWatermarkImageUrl('data:image/png;base64,abc123')).toBe('data:image/png;base64,abc123');
  });
});

describe('resolveLogoWatermarkPresentation', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('returns zoom metrics without generating a cropped image asset', () => {
    const originalCreateElement = document.createElement.bind(document);
    const drawImage = vi.fn();
    const getImageData = vi.fn(() => ({
      data: new Uint8ClampedArray([
        0, 0, 0, 255,
        0, 0, 0, 255,
        0, 0, 0, 0,
        0, 0, 0, 0,
      ]),
    }));

    const context = {
      clearRect: vi.fn(),
      drawImage,
      getImageData,
    };

    vi.spyOn(document, 'createElement').mockImplementation(((tagName: string) => {
      if (tagName === 'canvas') {
        return {
          width: 0,
          height: 0,
          getContext: () => context,
          toDataURL: () => 'data:image/png;base64,trimmed-logo',
        } as unknown as HTMLCanvasElement;
      }

      return originalCreateElement(tagName);
    }) as typeof document.createElement);

    const image = {
      naturalWidth: 2,
      naturalHeight: 2,
    } as HTMLImageElement;

    const presentation = resolveLogoWatermarkPresentation(image);

    expect(presentation).toMatchObject({
      emphasis: 'standard',
    });
    expect(presentation.scale).toBeGreaterThan(0.9);
    expect(presentation).not.toHaveProperty('croppedImageUrl');
    expect(drawImage).toHaveBeenCalledTimes(1);
    expect(getImageData).toHaveBeenCalledTimes(1);
  });
});
