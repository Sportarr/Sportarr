import { keepPreviousData, useQuery } from '@tanstack/react-query';
import apiClient from './client';

export type SearchOutcome = 'grabbed' | 'noResults' | 'nothingPassed' | 'notGrabbed' | 'failed';

export interface SearchReasonCount {
  reason: string;
  count: number;
  /** What the reason means and what you can change. */
  help?: string | null;
}

export interface SearchIndexerRow {
  name: string;
  status: string;
  results: number;
}

export interface SearchReleaseRow {
  title: string;
  indexer: string;
  quality?: string | null;
  size: number;
  matchScore: number;
  customFormatScore: number;
  status: 'grabbed' | 'passed' | 'rejected';
  reasons: string[];
  part?: string | null;
  isPack: boolean;
  /** A plain title for each entry in reasons, in the same order. Null when no plain title exists. */
  explanations?: (string | null)[] | null;
}

export interface SearchHistoryRecord {
  id: number;
  eventId: number;
  part?: string | null;
  searchedAt: string;
  isManual: boolean;
  outcome: SearchOutcome;
  message: string;
  releasesFound: number;
  releasesPassed: number;
  selectedRelease?: string | null;
  qualityProfile?: string | null;
  summary: string;
  /** How the search ended, in one plain sentence. */
  headline: string;
  /** What it means or what to do next. */
  help?: string | null;
  reasons: SearchReasonCount[];
  indexers: SearchIndexerRow[];
  releases?: SearchReleaseRow[] | null;
}

export const searchHistoryKeys = {
  event: (eventId: number) => ['search-history', 'event', eventId] as const,
  latest: (eventIds: number[], refreshKey: string) => ['search-history', 'latest', eventIds.join(','), refreshKey] as const,
};

/** All stored searches for one event, newest first. */
export function useEventSearchHistory(eventId: number, enabled = true) {
  return useQuery({
    queryKey: searchHistoryKeys.event(eventId),
    queryFn: async () => {
      const response = await apiClient.get<SearchHistoryRecord[]>(`/events/${eventId}/search-history`);
      return response.data;
    },
    enabled: enabled && eventId > 0,
    // The window opens rarely and must match the line that opened it.
    staleTime: 0,
  });
}

/**
 * The newest search for each event, keyed by event id. Pass the newest
 * finished-search time from the search queue as refreshKey, so the list
 * reloads when a queued search finishes.
 */
export function useLatestSearches(eventIds: number[], enabled = true, refreshKey = '') {
  // Newest events first, so a very long list keeps the events that matter most.
  const ids = Array.from(new Set(eventIds.filter((id) => id > 0))).sort((a, b) => b - a);
  return useQuery({
    queryKey: searchHistoryKeys.latest(ids, refreshKey),
    queryFn: async () => {
      // The ids go in the body. A long list does not fit in a URL.
      const response = await apiClient.post<Record<string, SearchHistoryRecord>>('/search-history/latest', {
        eventIds: ids,
      });
      return response.data;
    },
    enabled: enabled && ids.length > 0,
    staleTime: 60_000,
    placeholderData: keepPreviousData,
  });
}

/** The time of the newest finished search in a search queue status. */
export function newestCompletion(queue?: { recentlyCompleted?: { completedAt?: string | null }[] }): string {
  return (queue?.recentlyCompleted ?? []).reduce(
    (newest, item) => (item.completedAt && item.completedAt > newest ? item.completedAt : newest), '');
}

const outcomeText: Record<SearchOutcome, string> = {
  grabbed: 'grabbed',
  noResults: 'nothing found',
  nothingPassed: 'all rejected',
  notGrabbed: 'not grabbed',
  failed: 'error',
};

const lowerFirst = (text: string) => text.charAt(0).toLowerCase() + text.slice(1);

/** The search message without the internal "Error: " prefix. */
export function searchMessage(record: SearchHistoryRecord): string {
  return record.message.replace(/^Error:\s*/, '').trim();
}

/**
 * A short plain line, for example "every release was rejected. The most
 * common reason was quality not allowed (14 of 15)".
 */
export function describeSearch(record: SearchHistoryRecord): string {
  if (record.outcome === 'nothingPassed') {
    const top = record.reasons[0];
    return top
      ? `every release was rejected. The most common reason was ${top.reason.toLowerCase()} (${top.count} of ${record.releasesFound})`
      : 'every release was rejected';
  }
  const headline = record.headline || searchMessage(record) || outcomeText[record.outcome] || record.outcome;
  return lowerFirst(headline.replace(/\.$/, ''));
}

/** The outcome alone with a capital letter, for example "All rejected". */
export function describeOutcome(record: SearchHistoryRecord): string {
  const text = outcomeText[record.outcome] ?? record.outcome;
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** True when the newest search did not end with a grab. */
export function needsAttention(record: SearchHistoryRecord | undefined): record is SearchHistoryRecord {
  return record !== undefined && record.outcome !== 'grabbed';
}
