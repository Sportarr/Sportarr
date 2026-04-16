export interface LogoCoverageMetrics {
  aspectRatio: number;
  coverage: number;
  visibleDensity: number;
}

export interface LogoWatermarkPresentation {
  scale: number;
  emphasis: 'standard' | 'wide' | 'ultraWide';
}

const clamp = (value: number, min: number, max: number) => {
  return Math.min(max, Math.max(min, value));
};

export const calculateLogoWatermarkPresentation = ({ aspectRatio, coverage, visibleDensity }: LogoCoverageMetrics): LogoWatermarkPresentation => {
  const normalizedAspect = clamp((aspectRatio - 1) / 2, 0, 1);
  const normalizedCoverage = clamp(coverage, 0.08, 0.7);
  const whitespaceFactor = 1 - normalizedCoverage;
  const normalizedVisibleDensity = clamp(visibleDensity, 0.12, 0.9);
  const sparseShapeFactor = 1 - normalizedVisibleDensity;
  const emphasis = aspectRatio >= 3.4 || (aspectRatio >= 2.8 && normalizedVisibleDensity <= 0.42)
    ? 'ultraWide'
    : aspectRatio >= 2.1 || (aspectRatio >= 1.65 && normalizedVisibleDensity <= 0.5)
    ? 'wide'
    : 'standard';

  const rawScale = clamp(
    0.5 + whitespaceFactor * 0.26 + normalizedAspect * 0.58 + sparseShapeFactor * 0.48,
    0.72,
    emphasis === 'ultraWide' ? 1.92 : emphasis === 'wide' ? 1.75 : 1.58,
  );

  // Wide/ultra-wide logos (wordmarks) are inherently more visually prominent than
  // crests/badges, so their scale is dampened by 15% to keep watermarks subtle.
  const scale = emphasis === 'standard' ? rawScale : Number((rawScale * 0.85).toFixed(3));

  return { scale, emphasis };
};

export const getLogoWatermarkImageUrl = (logoUrl: string) => {
  if (
    !logoUrl
    || logoUrl.startsWith('data:')
    || logoUrl.startsWith('blob:')
    || logoUrl.startsWith('/')
  ) {
    return logoUrl;
  }

  try {
    const parsedUrl = new URL(logoUrl);
    if (parsedUrl.protocol === 'http:' || parsedUrl.protocol === 'https:') {
      return `/api/iptv/stream/url?url=${encodeURIComponent(parsedUrl.toString())}`;
    }
  } catch {
    return '';
  }

  return ''; // unsupported protocol — render no image
};

const getFallbackLogoWatermarkPresentation = (image: HTMLImageElement) => {
  const aspectRatio = image.naturalWidth > 0 && image.naturalHeight > 0
    ? image.naturalWidth / image.naturalHeight
    : 1;
  const normalizedAspect = clamp((aspectRatio - 1) / 2, 0, 1);

  return calculateLogoWatermarkPresentation({
    aspectRatio,
    coverage: 0.34,
    visibleDensity: 0.48 - normalizedAspect * 0.22,
  });
};

export const resolveLogoWatermarkPresentation = (image: HTMLImageElement) => {
  const sampleMaxDimension = 96;
  const width = image.naturalWidth || sampleMaxDimension;
  const height = image.naturalHeight || sampleMaxDimension;
  const scale = Math.min(1, sampleMaxDimension / Math.max(width, height));
  const canvasWidth = Math.max(1, Math.round(width * scale));
  const canvasHeight = Math.max(1, Math.round(height * scale));
  const canvas = document.createElement('canvas');

  canvas.width = canvasWidth;
  canvas.height = canvasHeight;

  const context = canvas.getContext('2d', { willReadFrequently: true });
  if (!context) {
    return getFallbackLogoWatermarkPresentation(image);
  }

  try {
    context.clearRect(0, 0, canvasWidth, canvasHeight);
    context.drawImage(image, 0, 0, canvasWidth, canvasHeight);

    const { data } = context.getImageData(0, 0, canvasWidth, canvasHeight);
    const alphaThreshold = 24;
    let nonTransparentPixels = 0;
    let minX = canvasWidth;
    let minY = canvasHeight;
    let maxX = -1;
    let maxY = -1;

    for (let index = 3; index < data.length; index += 4) {
      if (data[index] < alphaThreshold) {
        continue;
      }

      const pixelIndex = (index - 3) / 4;
      const x = pixelIndex % canvasWidth;
      const y = Math.floor(pixelIndex / canvasWidth);

      nonTransparentPixels += 1;
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);
    }

    if (nonTransparentPixels === 0 || maxX < minX || maxY < minY) {
      return getFallbackLogoWatermarkPresentation(image);
    }

    const visibleWidth = maxX - minX + 1;
    const visibleHeight = maxY - minY + 1;
    return calculateLogoWatermarkPresentation({
      aspectRatio: visibleWidth / visibleHeight,
      coverage: nonTransparentPixels / (canvasWidth * canvasHeight),
      visibleDensity: nonTransparentPixels / (visibleWidth * visibleHeight),
    });
  } catch {
    return getFallbackLogoWatermarkPresentation(image);
  }
};
