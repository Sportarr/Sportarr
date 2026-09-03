import { useState, useRef, useEffect, useCallback } from 'react';
import type { ReactNode } from 'react';
import { createPortal } from 'react-dom';

interface PortalTooltipProps {
  children: ReactNode;
  content: ReactNode;
  className?: string;
  preferTop?: boolean; // Prefer showing above the trigger
}

/**
 * A tooltip component that renders in a portal to escape overflow containers.
 * This prevents tooltips from being clipped by parent containers with overflow-hidden/auto.
 */
export function PortalTooltip({ children, content, className = '', preferTop = false }: PortalTooltipProps) {
  const [isVisible, setIsVisible] = useState(false);
  // A touch screen sends a mouse-leave of its own right after a tap, which
  // closed the tooltip the tap had just opened. The pointer handlers are
  // therefore only for pointers that really hover.
  const canHover = typeof window !== 'undefined' && window.matchMedia?.('(hover: hover)').matches;
  const [position, setPosition] = useState({ top: 0, left: 0, showAbove: false });
  const triggerRef = useRef<HTMLDivElement>(null);
  const tooltipRef = useRef<HTMLDivElement>(null);

  const updatePosition = useCallback(() => {
    if (!triggerRef.current || !tooltipRef.current) return;

    const triggerRect = triggerRef.current.getBoundingClientRect();
    const tooltipRect = tooltipRef.current.getBoundingClientRect();
    const viewportHeight = window.innerHeight;
    const viewportWidth = window.innerWidth;

    // Check if there's enough space below
    const spaceBelow = viewportHeight - triggerRect.bottom;
    const spaceAbove = triggerRect.top;
    const tooltipHeight = tooltipRect.height || 100; // Estimate if not yet rendered

    // Determine if we should show above or below
    let showAbove = preferTop;
    if (preferTop) {
      // If preferring top but not enough space, show below
      if (spaceAbove < tooltipHeight + 8) {
        showAbove = false;
      }
    } else {
      // If preferring bottom but not enough space, show above
      if (spaceBelow < tooltipHeight + 8 && spaceAbove > spaceBelow) {
        showAbove = true;
      }
    }

    // Calculate vertical position
    let top: number;
    if (showAbove) {
      top = triggerRect.top - tooltipHeight - 4;
    } else {
      top = triggerRect.bottom + 4;
    }

    // Calculate horizontal position (right-aligned to trigger, but stay in viewport)
    let left = triggerRect.right - (tooltipRect.width || 200);

    // Ensure tooltip doesn't go off left edge
    if (left < 8) {
      left = 8;
    }

    // Ensure tooltip doesn't go off right edge
    if (left + (tooltipRect.width || 200) > viewportWidth - 8) {
      left = viewportWidth - (tooltipRect.width || 200) - 8;
    }

    setPosition({ top, left, showAbove });
  }, [preferTop]);

  useEffect(() => {
    if (isVisible) {
      // Initial position update
      updatePosition();

      // Update position on scroll/resize
      const handleUpdate = () => updatePosition();
      window.addEventListener('scroll', handleUpdate, true);
      window.addEventListener('resize', handleUpdate);

      return () => {
        window.removeEventListener('scroll', handleUpdate, true);
        window.removeEventListener('resize', handleUpdate);
      };
    }
  }, [isVisible, updatePosition]);

  // Use a small delay to allow tooltip to render before positioning
  useEffect(() => {
    if (isVisible) {
      const timer = setTimeout(updatePosition, 10);
      return () => clearTimeout(timer);
    }
  }, [isVisible, updatePosition]);

  // A touch screen has no hover, so a tap opens the tooltip and a tap
  // anywhere else closes it. The tap must not reach the control behind the
  // trigger: these often sit inside a label, where a tap would flip the very
  // checkbox the tooltip explains.
  useEffect(() => {
    if (!isVisible) return;
    const close = (event: Event) => {
      if (triggerRef.current?.contains(event.target as Node)) return;
      setIsVisible(false);
    };
    document.addEventListener('pointerdown', close);
    return () => document.removeEventListener('pointerdown', close);
  }, [isVisible]);

  return (
    <>
      {/* A tap focuses the trigger before it clicks it. Opening on every
          focus therefore opened the tooltip and the click that followed
          closed it again, so focus opens it only for the keyboard. */}
      <div
        ref={triggerRef}
        className="inline-flex"
        tabIndex={0}
        role="button"
        aria-expanded={isVisible}
        onMouseEnter={canHover ? () => setIsVisible(true) : undefined}
        onMouseLeave={canHover ? () => setIsVisible(false) : undefined}
        onFocus={(event) => { if (event.target.matches(':focus-visible')) setIsVisible(true); }}
        onBlur={() => setIsVisible(false)}
        onKeyDown={(event) => { if (event.key === 'Escape') setIsVisible(false); }}
        onClick={(event) => {
          event.preventDefault();
          event.stopPropagation();
          setIsVisible(visible => !visible);
        }}
      >
        {children}
      </div>
      {isVisible && createPortal(
        <div
          ref={tooltipRef}
          className={`fixed z-[100] p-1.5 bg-gray-900 border border-gray-700 rounded-lg shadow-xl ${className}`}
          style={{
            top: position.top,
            left: position.left,
            opacity: position.top === 0 ? 0 : 1, // Hide until positioned
          }}
        >
          {content}
        </div>,
        document.body
      )}
    </>
  );
}
