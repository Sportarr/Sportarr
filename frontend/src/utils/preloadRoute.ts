/**
 * Warms a page's code before the user asks for it.
 *
 * Every page is loaded lazily, so a nav click has to fetch the page's chunk
 * before the router can show it. The heaviest pages are well over 100 kB,
 * which is long enough to feel. Starting that fetch when the pointer reaches
 * the link usually finishes it before the click lands.
 *
 * The paths here mirror the router. A page that moves breaks the build,
 * because the import specifier is checked like any other import.
 */

const ROUTE_IMPORTS: Record<string, () => Promise<unknown>> = {
  '/leagues': () => import('../pages/LeaguesPage'),
  '/add-league/search': () => import('../pages/LeagueSearchPage'),
  '/add-team/search': () => import('../pages/TeamsPage'),
  '/add-event/search': () => import('../pages/EventSearchPage'),
  '/library-import': () => import('../pages/LibraryImportPage'),
  '/calendar': () => import('../pages/CalendarPage'),
  '/activity': () => import('../pages/ActivityPage'),

  // The parent entries redirect to their first child, so warm what the user
  // actually lands on.
  '/iptv': () => import('../pages/settings/IptvSettings'),
  '/iptv/sources': () => import('../pages/settings/IptvSettings'),
  '/iptv/channels': () => import('../pages/settings/IptvChannelsSettings'),
  '/iptv/guide': () => import('../pages/iptv/TvGuidePage'),
  '/iptv/recordings': () => import('../pages/settings/DvrRecordingsSettings'),
  '/iptv/dvr-settings': () => import('../pages/settings/DvrSettingsPage'),

  '/settings': () => import('../pages/settings/MediaManagementSettings'),
  '/settings/mediamanagement': () => import('../pages/settings/MediaManagementSettings'),
  '/settings/profiles': () => import('../pages/settings/ProfilesSettings'),
  '/settings/quality': () => import('../pages/settings/QualityPage'),
  '/settings/indexers': () => import('../pages/settings/IndexersSettings'),
  '/settings/importlists': () => import('../pages/settings/ImportListsSettings'),
  '/settings/downloadclients': () => import('../pages/settings/DownloadClientsSettings'),
  '/settings/notifications': () => import('../pages/settings/NotificationsSettings'),
  '/settings/metadata': () => import('../pages/settings/MetadataProvidersSettings'),
  '/settings/general': () => import('../pages/settings/GeneralSettings'),
  '/settings/ui': () => import('../pages/settings/UISettings'),
  '/settings/tags': () => import('../pages/settings/TagsSettings'),
  '/settings/development': () => import('../pages/settings/DevelopmentSettings'),

  '/system': () => import('../pages/SystemPage'),
  '/system/status': () => import('../pages/SystemPage'),
  '/system/health': () => import('../pages/SystemHealthPage'),
  '/system/tasks': () => import('../pages/TasksPage'),
  '/system/stats': () => import('../pages/StatsPage'),
  '/system/backup': () => import('../pages/BackupPage'),
  '/system/updates': () => import('../pages/SystemUpdatesPage'),
  '/system/events': () => import('../pages/SystemEventsPage'),
  '/system/logs': () => import('../pages/LogFilesPage'),
};

const started = new Set<string>();

/**
 * Start loading the page behind a path. Safe to call repeatedly, and on a
 * path with no entry. A failed load is forgotten so a later attempt can
 * retry, and the error is swallowed because this is only ever a head start:
 * the router still loads the page itself on the real click.
 */
export function preloadRoute(path: string | undefined) {
  if (!path || started.has(path)) return;
  const load = ROUTE_IMPORTS[path];
  if (!load) return;
  started.add(path);
  load().catch(() => {
    started.delete(path);
  });
}

/** The paths this module can warm. Exported for tests. */
export function preloadablePaths(): string[] {
  return Object.keys(ROUTE_IMPORTS);
}
