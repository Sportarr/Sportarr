import { useEffect, useRef, useState } from 'react';

type IconComponent = React.ComponentType<{ className?: string }>;

/**
 * A navigation icon that answers the click.
 *
 * The outline drawing gives way to the solid one when its destination is the
 * current page, so the shape reads as filled in rather than merely tinted.
 * The swap crossfades, and the moment it becomes active the icon gives one
 * short press, which is the part that tells someone their tap landed.
 *
 * Both drawings are stacked, so nothing reflows while they trade places.
 * A reader who has asked for less motion gets the swap without the press.
 */
export default function NavIcon({
  icon: Icon,
  activeIcon: ActiveIcon,
  active,
  className = 'h-5 w-5',
  chip = false,
}: {
  icon: IconComponent;
  activeIcon: IconComponent;
  active: boolean;
  className?: string;
  /** Fill the icon's square with the accent and knock the icon out of it. */
  chip?: boolean;
}) {
  const [pressed, setPressed] = useState(false);
  const wasActive = useRef(active);

  useEffect(() => {
    if (active && !wasActive.current) {
      setPressed(true);
      const timer = window.setTimeout(() => setPressed(false), 320);
      return () => window.clearTimeout(timer);
    }
    wasActive.current = active;
    return undefined;
  }, [active]);

  useEffect(() => {
    wasActive.current = active;
  }, [active]);

  const art = (
    <span className={`relative inline-flex shrink-0 ${className}`}>
      <Icon
        className={`absolute inset-0 h-full w-full transition-[opacity,transform] duration-200 ease-out ${
          active ? 'scale-90 opacity-0' : 'scale-100 opacity-100'
        }`}
      />
      <ActiveIcon
        className={`absolute inset-0 h-full w-full transition-[opacity,transform] duration-200 ease-out ${
          active ? 'scale-100 opacity-100' : 'scale-110 opacity-0'
        }`}
      />
    </span>
  );

  if (!chip) {
    return (
      <span className={pressed ? 'motion-safe:animate-nav-press inline-flex' : 'inline-flex'}>
        {art}
      </span>
    );
  }

  return (
    <span
      className={`inline-flex shrink-0 items-center justify-center rounded-lg p-1.5 transition-colors duration-200 ${
        active ? 'bg-red-600 text-white' : 'bg-transparent'
      } ${pressed ? 'motion-safe:animate-nav-press' : ''}`}
    >
      {art}
    </span>
  );
}
