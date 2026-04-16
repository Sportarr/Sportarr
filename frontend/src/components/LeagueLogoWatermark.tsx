import { useEffect, useState } from 'react';
import { getLogoWatermarkImageUrl, resolveLogoWatermarkPresentation, type LogoWatermarkPresentation } from '../utils/logoWatermark';

export type LogoWatermarkVariant = 'compact' | 'agenda';
type LogoWatermarkEmphasis = LogoWatermarkPresentation['emphasis'];

interface LogoWatermarkVariantStyle {
  containerClass: string;
  imageClass: string;
  objectFitClass: string;
  scaleCap?: number;
  transformOrigin: 'center' | 'right center';
}

const logoWatermarkScaleCache = new Map<string, LogoWatermarkPresentation>();

const WATERMARK_STYLES: Record<LogoWatermarkVariant, Record<LogoWatermarkEmphasis, LogoWatermarkVariantStyle>> = {
  compact: {
    standard: {
      containerClass: 'w-28 pr-[13px] justify-end',
      imageClass: 'h-11 w-11 opacity-[0.25]',
      objectFitClass: 'object-contain',
      transformOrigin: 'center',
    },
    wide: {
      containerClass: 'w-28 pr-[11px] justify-end',
      imageClass: 'h-10 w-24 opacity-[0.25]',
      objectFitClass: 'object-cover object-right',
      scaleCap: 0.833,
      transformOrigin: 'right center',
    },
    ultraWide: {
      containerClass: 'w-[54%] pr-[11px] justify-end',
      imageClass: 'h-10 w-full max-w-[11.75rem] opacity-[0.25]',
      objectFitClass: 'object-cover object-right',
      scaleCap: 0.935,
      transformOrigin: 'right center',
    },
  },
  agenda: {
    standard: {
      containerClass: 'w-36 pr-[11px] justify-end',
      imageClass: 'h-[4.6rem] w-[4.6rem] opacity-[0.3]',
      objectFitClass: 'object-contain',
      transformOrigin: 'center',
    },
    wide: {
      containerClass: 'w-36 pr-[11px] justify-end',
      imageClass: 'h-[3.15rem] w-[6.9rem] opacity-[0.3]',
      objectFitClass: 'object-cover object-right',
      scaleCap: 0.782,
      transformOrigin: 'right center',
    },
    ultraWide: {
      containerClass: 'w-40 pr-[11px] justify-end',
      imageClass: 'h-[3.45rem] w-[9.2rem] opacity-[0.3]',
      objectFitClass: 'object-cover object-right',
      scaleCap: 0.85,
      transformOrigin: 'right center',
    },
  },
};

const getRenderedScale = (presentation: LogoWatermarkPresentation, variant: LogoWatermarkVariant) => {
  const { scaleCap } = WATERMARK_STYLES[variant][presentation.emphasis];
  return scaleCap !== undefined ? Math.min(presentation.scale, scaleCap) : presentation.scale;
};

export default function LeagueLogoWatermark({
  logoUrl,
  variant,
}: {
  logoUrl: string;
  variant: LogoWatermarkVariant;
}) {
  const imageUrl = getLogoWatermarkImageUrl(logoUrl);
  const [presentation, setPresentation] = useState<LogoWatermarkPresentation>(() => (
    logoWatermarkScaleCache.get(logoUrl) ?? { emphasis: 'standard', scale: 1 }
  ));

  useEffect(() => {
    const cachedScale = logoWatermarkScaleCache.get(logoUrl);
    if (cachedScale) {
      setPresentation(cachedScale);
      return;
    }

    let cancelled = false;
    const image = new window.Image();
    image.decoding = 'async';
    // Deliberately avoid CORS mode here. Many remote logo hosts don't send
    // ACAO headers, so a CORS image probe can fail entirely and leave the
    // watermark at scale 1. We still attempt pixel measurement on load, and
    // fall back to natural aspect-ratio sizing if the canvas read is tainted.

    const finalize = (nextPresentation: LogoWatermarkPresentation) => {
      logoWatermarkScaleCache.set(logoUrl, nextPresentation);
      if (!cancelled) {
        setPresentation(nextPresentation);
      }
    };

    image.onload = () => {
      finalize(resolveLogoWatermarkPresentation(image));
    };

    image.onerror = () => {
      finalize({ emphasis: 'standard', scale: 1 });
    };

    image.src = imageUrl;

    return () => {
      cancelled = true;
    };
  }, [imageUrl, logoUrl]);

  const isAgenda = variant === 'agenda';
  const style = WATERMARK_STYLES[variant][presentation.emphasis];
  const renderedScale = getRenderedScale(presentation, variant);

  return (
    <>
      <div className={`pointer-events-none absolute inset-0 ${isAgenda ? 'bg-gradient-to-r from-transparent via-transparent to-black/25' : 'bg-gradient-to-r from-transparent via-transparent to-black/20'}`} aria-hidden="true" />
      <div data-testid={`calendar-event-logo-frame-${logoUrl}`} className={`pointer-events-none absolute inset-y-0 right-0 flex items-center ${style.containerClass}`} aria-hidden="true">
        <img
          src={imageUrl}
          alt=""
          data-testid={`calendar-event-logo-${logoUrl}`}
          className={`${style.imageClass} ${style.objectFitClass} saturate-0 brightness-150 contrast-125`}
          loading="lazy"
          style={{
            transform: `scale(${renderedScale})`,
            transformOrigin: style.transformOrigin,
          }}
        />
      </div>
    </>
  );
}
