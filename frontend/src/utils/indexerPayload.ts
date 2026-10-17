import type { Indexer as ApiIndexer, IndexerField } from '../types';

export interface IndexerFormData {
  id?: number;
  name?: string;
  implementation?: string;
  enabled?: boolean;
  enableRss?: boolean;
  enableAutomaticSearch?: boolean;
  enableInteractiveSearch?: boolean;
  priority?: number;
  baseUrl?: string;
  apiPath?: string;
  apiKey?: string;
  categories?: number[];
  animeCategories?: number[];
  minimumSeeders?: number;
  seedRatio?: number;
  seedTime?: number;
  queryLimit?: number;
  grabLimit?: number;
  requestDelayMs?: number;
  seasonPackSeedTime?: number;
  earlyReleaseLimit?: number;
  additionalParameters?: string;
  multiLanguages?: string[];
  rejectBlocklistedTorrentHashes?: boolean;
  disableSslCertificateValidation?: boolean;
  downloadClientId?: number;
  tags?: number[];
  cookie?: string;
  allowZeroSize?: boolean;
  failDownloads?: number[];
}

export type ApiIndexerPayload = Omit<ApiIndexer, 'id'> & { id?: number };

export function toApiIndexer(indexer: IndexerFormData): ApiIndexerPayload {
  const fields: IndexerField[] = [
    {
      name: 'baseUrl',
      value: indexer.implementation === 'BroadcasTheNet'
        ? 'https://api.broadcasthe.net'
        : (indexer.baseUrl || ''),
    },
    {
      name: 'apiPath',
      value: indexer.implementation === 'BroadcasTheNet' ? '' : (indexer.apiPath || '/api'),
    },
    { name: 'apiKey', value: indexer.apiKey || '' },
    { name: 'categories', value: indexer.categories?.join(',') || '' },
    { name: 'minimumSeeders', value: String(indexer.minimumSeeders ?? 1) },
  ];

  if (indexer.animeCategories?.length) {
    fields.push({ name: 'animeCategories', value: indexer.animeCategories.join(',') });
  }
  if (indexer.seedRatio !== undefined) {
    fields.push({ name: 'seedRatio', value: String(indexer.seedRatio) });
  }
  if (indexer.seedTime !== undefined) {
    fields.push({ name: 'seedTime', value: String(indexer.seedTime) });
  }
  // Empty limits tell the API to clear saved limits.
  fields.push({
    name: 'queryLimit',
    value: indexer.queryLimit !== undefined ? String(indexer.queryLimit) : '',
  });
  fields.push({
    name: 'grabLimit',
    value: indexer.grabLimit !== undefined ? String(indexer.grabLimit) : '',
  });
  fields.push({ name: 'requestDelayMs', value: String(indexer.requestDelayMs ?? 0) });
  if (indexer.seasonPackSeedTime !== undefined) {
    fields.push({ name: 'seasonPackSeedTime', value: String(indexer.seasonPackSeedTime) });
  }
  // An empty early-release limit clears the saved limit.
  fields.push({
    name: 'earlyReleaseLimit',
    value: indexer.earlyReleaseLimit !== undefined ? String(indexer.earlyReleaseLimit) : '',
  });
  if (indexer.additionalParameters) {
    fields.push({ name: 'additionalParameters', value: indexer.additionalParameters });
  }
  if (indexer.multiLanguages?.length) {
    fields.push({ name: 'multiLanguages', value: indexer.multiLanguages.join(',') });
  }
  if (indexer.rejectBlocklistedTorrentHashes !== undefined) {
    fields.push({
      name: 'rejectBlocklistedTorrentHashes',
      value: String(indexer.rejectBlocklistedTorrentHashes),
    });
  }
  fields.push({
    name: 'disableSslCertificateValidation',
    value: String(indexer.disableSslCertificateValidation ?? false),
  });
  if (indexer.downloadClientId !== undefined) {
    fields.push({ name: 'downloadClientId', value: String(indexer.downloadClientId) });
  }
  // RSS fields must preserve explicit empty and false values.
  if (indexer.cookie !== undefined && indexer.cookie !== null) {
    fields.push({ name: 'cookie', value: indexer.cookie });
  }
  if (indexer.allowZeroSize !== undefined) {
    fields.push({ name: 'allowZeroSize', value: String(indexer.allowZeroSize) });
  }
  // An empty list tells the API to clear saved failure rules.
  fields.push({ name: 'failDownloads', value: (indexer.failDownloads ?? []).join(',') });

  return {
    id: indexer.id,
    name: indexer.name || '',
    implementation: indexer.implementation || 'Torznab',
    enable: indexer.enabled ?? true,
    enableRss: indexer.enableRss ?? true,
    enableAutomaticSearch: indexer.enableAutomaticSearch ?? true,
    enableInteractiveSearch: indexer.enableInteractiveSearch ?? true,
    priority: indexer.priority || 25,
    fields,
    tags: indexer.tags || [],
  };
}
