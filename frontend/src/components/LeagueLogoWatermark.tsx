export type LogoWatermarkVariant = 'compact' | 'agenda';

// Render the league logo the same way the Leagues page does: a plain
// object-contain <img> of the logo URL, which shows the whole logo crisply in
// its natural colour. Here it is anchored to the right of the event card at a
// subtle opacity so it reads as background branding behind the card text,
// rather than the cropped, greyscaled, proxied thumbnail the watermark used to
// produce. No proxy, no canvas analysis, no adaptive scaling - just the logo.
const VARIANT_IMAGE_CLASS: Record<LogoWatermarkVariant, string> = {
  compact: 'h-9 max-w-[44%]',
  agenda: 'h-12 max-w-[40%]',
};

export default function LeagueLogoWatermark({
  logoUrl,
  variant,
}: {
  logoUrl: string;
  variant: LogoWatermarkVariant;
}) {
  return (
    <>
      {/* Subtle right-side darkening so the logo never competes with the title text. */}
      <div
        className="pointer-events-none absolute inset-0 bg-gradient-to-r from-transparent via-transparent to-black/20"
        aria-hidden="true"
      />
      <img
        src={logoUrl}
        alt=""
        aria-hidden="true"
        loading="lazy"
        data-testid={`calendar-event-logo-${logoUrl}`}
        className={`pointer-events-none absolute right-1.5 top-1/2 -translate-y-1/2 ${VARIANT_IMAGE_CLASS[variant]} object-contain object-right opacity-60`}
      />
    </>
  );
}
