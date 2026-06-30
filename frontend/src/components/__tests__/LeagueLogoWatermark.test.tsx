import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import LeagueLogoWatermark from '../LeagueLogoWatermark';

describe('LeagueLogoWatermark', () => {
  it('renders the league logo directly (no proxy) with object-contain', () => {
    const logoUrl = 'https://example.com/logo.png';

    render(<LeagueLogoWatermark logoUrl={logoUrl} variant="compact" />);

    const img = screen.getByTestId(`calendar-event-logo-${logoUrl}`);
    // Same approach as the Leagues page: the raw logo URL, shown whole.
    expect(img).toHaveAttribute('src', logoUrl);
    expect(img.className).toContain('object-contain');
    expect(img.className).toContain('h-9');
  });

  it('uses a larger logo for the agenda variant', () => {
    const logoUrl = 'https://example.com/agenda-logo.png';

    render(<LeagueLogoWatermark logoUrl={logoUrl} variant="agenda" />);

    const img = screen.getByTestId(`calendar-event-logo-${logoUrl}`);
    expect(img).toHaveAttribute('src', logoUrl);
    expect(img.className).toContain('h-12');
  });
});
