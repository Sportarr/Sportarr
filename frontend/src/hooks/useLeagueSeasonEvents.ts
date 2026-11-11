import { useCallback, useRef, useState } from 'react';
import { apiGet } from '../utils/api';

export interface LeagueOption {
  id: number;
  name: string;
  sport: string;
  logoUrl?: string;
}

export interface LibraryEventOption {
  id: number;
  externalId?: string;
  title: string;
  sport: string;
  eventDate: string;
  broadcastDate?: string | null;
  season?: string;
  seasonNumber?: number;
  episodeNumber?: number;
  venue?: string;
  leagueName?: string;
  homeTeam?: string;
  awayTeam?: string;
  hasFile: boolean;
  usesMultiPart: boolean;
  /**
   * False when the event keeps one whole file. Multi-part episodes are off,
   * or the event is set to Full Event.
   */
  keepsParts?: boolean;
  files: Array<{
    id: number;
    partName?: string;
    partNumber?: number;
    quality?: string;
  }>;
}

export interface PartDefinition {
  name: string;
  partNumber: number;
}

/** The parts the event type uses, from the server. The list includes Full Event. */
export async function fetchEventParts(eventId: number): Promise<PartDefinition[]> {
  const response = await apiGet(`/api/event/${eventId}/parts`);
  if (!response.ok) throw new Error(`Could not load the parts of event ${eventId}`);
  const data = await response.json();
  return Array.isArray(data.parts) ? data.parts : [];
}

/**
 * League, season, event and part lists for the Library Import pickers.
 * Only the newest answer for each list is kept. Changing the league, season
 * or event while a request runs let the older answer arrive last, so a
 * picker offered items of the previous selection.
 */
export function useLeagueSeasonEvents() {
  const [leagues, setLeagues] = useState<LeagueOption[]>([]);
  const [seasons, setSeasons] = useState<string[]>([]);
  const [events, setEvents] = useState<LibraryEventOption[]>([]);
  const [parts, setParts] = useState<PartDefinition[]>([]);
  // The event the current parts belong to. Null for a sport list or no list.
  const [partsEventId, setPartsEventId] = useState<number | null>(null);

  const [loadingLeagues, setLoadingLeagues] = useState(true);
  const [loadingSeasons, setLoadingSeasons] = useState(false);
  const [loadingEvents, setLoadingEvents] = useState(false);
  const [loadingParts, setLoadingParts] = useState(false);

  const seasonSeq = useRef(0);
  const eventSeq = useRef(0);
  const partSeq = useRef(0);

  const loadLeagues = useCallback(async () => {
    setLoadingLeagues(true);
    try {
      const response = await apiGet('/api/leagues');
      if (response.ok) {
        const data = await response.json();
        setLeagues(data);
      }
    } catch (err) {
      console.error('Failed to load leagues:', err);
    } finally {
      setLoadingLeagues(false);
    }
  }, []);

  const loadSeasons = useCallback(async (leagueId: number) => {
    const seq = ++seasonSeq.current;
    setLoadingSeasons(true);
    try {
      const response = await apiGet(`/api/library/leagues/${leagueId}/seasons`);
      if (seq !== seasonSeq.current) return;
      if (response.ok) {
        const data = await response.json();
        if (seq !== seasonSeq.current) return;
        setSeasons(data.seasons || []);
      }
    } catch (err) {
      console.error('Failed to load seasons:', err);
    } finally {
      if (seq === seasonSeq.current) setLoadingSeasons(false);
    }
  }, []);

  const loadEvents = useCallback(async (
    leagueId: number,
    season: string | null,
    search: string = '',
    limit?: number,
  ) => {
    const seq = ++eventSeq.current;
    setLoadingEvents(true);
    try {
      const params = new URLSearchParams();
      if (season) params.append('season', season);
      if (search) params.append('search', search);
      if (limit) params.append('limit', String(limit));

      const queryString = params.toString();
      const url = `/api/library/leagues/${leagueId}/events${queryString ? `?${queryString}` : ''}`;
      const response = await apiGet(url);
      if (seq !== eventSeq.current) return;
      if (response.ok) {
        const data = await response.json();
        if (seq !== eventSeq.current) return;
        setEvents(data.events || []);
      }
    } catch (err) {
      console.error('Failed to load events:', err);
    } finally {
      if (seq === eventSeq.current) setLoadingEvents(false);
    }
  }, []);

  const loadParts = useCallback(async (sport: string) => {
    const seq = ++partSeq.current;
    setLoadingParts(true);
    try {
      const response = await apiGet(`/api/library/parts/${encodeURIComponent(sport)}`);
      if (seq !== partSeq.current) return;
      if (response.ok) {
        const data = await response.json();
        if (seq !== partSeq.current) return;
        setParts(data.parts || []);
      }
    } catch (err) {
      console.error('Failed to load parts:', err);
    } finally {
      if (seq === partSeq.current) setLoadingParts(false);
    }
  }, []);

  /**
   * Load the parts of one event. The part numbers depend on the event type,
   * so a Fight Night numbers its Main Card differently from a numbered event.
   * When the event parts cannot load, the sport list is used.
   */
  const loadEventParts = useCallback(async (eventId: number, sport: string) => {
    const seq = ++partSeq.current;
    setLoadingParts(true);
    let list: PartDefinition[] = [];
    try {
      list = await fetchEventParts(eventId);
    } catch (err) {
      console.error('Failed to load event parts:', err);
      try {
        const response = await apiGet(`/api/library/parts/${encodeURIComponent(sport)}`);
        if (response.ok) list = (await response.json()).parts || [];
      } catch (fallbackErr) {
        console.error('Failed to load parts:', fallbackErr);
      }
    }
    if (seq !== partSeq.current) return;
    setParts(list);
    setPartsEventId(eventId);
    setLoadingParts(false);
  }, []);

  /** Clear the parts and drop the answer of a parts request that is still running. */
  const clearParts = useCallback(() => {
    partSeq.current += 1;
    setParts([]);
    setPartsEventId(null);
    setLoadingParts(false);
  }, []);

  /** Clear the seasons and drop the answer of a seasons request that is still running. */
  const clearSeasons = useCallback(() => {
    seasonSeq.current += 1;
    setSeasons([]);
    setLoadingSeasons(false);
  }, []);

  /** Drop the answer of any events request that is still running. */
  const cancelEvents = useCallback(() => {
    eventSeq.current += 1;
    setLoadingEvents(false);
  }, []);

  return {
    leagues,
    seasons,
    events,
    parts,
    loadingLeagues,
    loadingSeasons,
    loadingEvents,
    loadingParts,
    loadLeagues,
    loadSeasons,
    loadEvents,
    loadParts,
    loadEventParts,
    clearParts,
    partsEventId,
    clearSeasons,
    cancelEvents,
    setSeasons,
    setEvents,
    setParts,
  };
}
