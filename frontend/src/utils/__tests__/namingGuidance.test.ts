import { describe, expect, it } from 'vitest';
import { renderNamingExample } from '../namingGuidance';

describe('naming preview', () => {
  it('renders the source filename example without a literal token', () => {
    expect(renderNamingExample('{Original Filename}')).toBe(
      'MMA.League.2026.Event.100.S2026E12.1080p.WEB-GROUP.mkv');
  });

  it('renders both air-date token forms used by presets', () => {
    const preview = renderNamingExample('{Air Date Year} - {Air Date} - {Event Title}');

    expect(preview).toBe('2026 - 2026-11-16 - Event 100 Main Event.mkv');
  });

  it('omits multipart tokens when multipart naming is disabled', () => {
    expect(renderNamingExample('{Season}{Episode}{Part} - {Event Title}', { includePart: false }))
      .toBe('s2026e12 - Event 100 Main Event.mkv');
  });
});
