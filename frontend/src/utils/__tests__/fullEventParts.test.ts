import { describe, expect, it } from 'vitest';
import {
  FULL_EVENT_PART, fileCountLabel, isFullEventOnly, monitoredPartsValue, partsAfterLeavingFullEvent, partsValueLeavingFullEvent, togglePartChoice,
} from '../leagueSportRules';

describe('Full Event monitored parts', () => {
  it.each(['Full Event', ' full event ', 'FULL EVENT'])('reads %j as Full Event', value => {
    expect(isFullEventOnly(value)).toBe(true);
  });

  it.each([null, undefined, '', 'Main Card', 'Full Event,Main Card'])('does not read %j as Full Event', value => {
    expect(isFullEventOnly(value)).toBe(false);
  });

  it('goes back to the league parts when the league uses parts', () => {
    expect(partsValueLeavingFullEvent('Main Card,Prelims', 'Fighting')).toBeNull();
    expect(partsValueLeavingFullEvent(null, 'Fighting')).toBeNull();
  });

  it('lists the event type parts when the league is Full Event', () => {
    expect(partsValueLeavingFullEvent('Full Event', 'Combat', ['Countdown', 'Main Show'])).toBe('Countdown,Main Show');
  });

  it('falls back to the sport parts when the event names none', () => {
    expect(partsValueLeavingFullEvent('Full Event', 'Fighting')).toBe('Early Prelims,Prelims,Main Card,Post Show');
    expect(partsValueLeavingFullEvent('Full Event', 'Fighting', [])).toBe('Early Prelims,Prelims,Main Card,Post Show');
  });
});

describe('League picker after Full Event', () => {
  const all = ['Early Prelims', 'Prelims', 'Main Card', 'Post Show'];

  it('brings back the earlier part choices', () => {
    expect(partsAfterLeavingFullEvent('Prelims,Main Card', all)).toEqual(['Prelims', 'Main Card']);
  });

  it('ticks every part for a kept "all parts" value or nothing kept', () => {
    expect(partsAfterLeavingFullEvent('*', all)).toEqual(all);
    expect(partsAfterLeavingFullEvent(null, all)).toEqual(all);
  });

  it('keeps an earlier choice of no parts', () => {
    expect(partsAfterLeavingFullEvent('', all)).toEqual([]);
  });
});

describe('League picker round trip', () => {
  const all = ['Early Prelims', 'Prelims', 'Main Card', 'Post Show'];

  it('brings back "Prelims,Main Card" after Full Event is ticked and unticked', () => {
    const start = new Set(['Prelims', 'Main Card']);
    const fullEvent = togglePartChoice(start, FULL_EVENT_PART, all, null);
    expect(monitoredPartsValue(fullEvent, all)).toBe('Full Event');

    // The backend kept the earlier value when Full Event was saved.
    const restored = togglePartChoice(fullEvent, FULL_EVENT_PART, all, 'Prelims,Main Card');
    expect(monitoredPartsValue(restored, all)).toBe('Prelims,Main Card');
  });

  it('clears Full Event when a part is ticked', () => {
    const next = togglePartChoice(new Set([FULL_EVENT_PART]), 'Prelims', all, null);
    expect(monitoredPartsValue(next, all)).toBe('Prelims');
  });

  it('saves all parts as null and no parts as empty', () => {
    expect(monitoredPartsValue(new Set(all), all)).toBeNull();
    expect(monitoredPartsValue(new Set(), all)).toBe('');
  });
});

describe('File count label', () => {
  it('counts visible files and names the hidden ones', () => {
    expect(fileCountLabel([{}, { isParked: true }, { isParked: true }, { isParked: true }])).toBe('1 File · 3 hidden');
    expect(fileCountLabel([{}, {}, { isParked: true }])).toBe('2 Files · 1 hidden');
  });

  it('keeps the old labels without hidden files', () => {
    expect(fileCountLabel([{}])).toBe('Downloaded');
    expect(fileCountLabel([{}, {}])).toBe('2 Files');
  });
});

