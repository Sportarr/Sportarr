import {
  FolderIcon,
  AdjustmentsHorizontalIcon,
  SparklesIcon,
  MagnifyingGlassIcon,
  QueueListIcon,
  ArrowDownTrayIcon,
  BellIcon,
  Cog6ToothIcon,
  PaintBrushIcon,
  TagIcon,
} from '@heroicons/react/24/outline';

/**
 * Every settings page, used by the phone top bar's gear pill (and anything
 * else that needs to enumerate settings destinations). /settings itself
 * redirects to Media Management; there is no hub page.
 */
export const SETTINGS_PAGES = [
  { to: '/settings/mediamanagement', icon: FolderIcon, title: 'Media Management', blurb: 'File naming, root folders, and import behavior' },
  { to: '/settings/profiles', icon: AdjustmentsHorizontalIcon, title: 'Profiles', blurb: 'Quality profiles that decide what to grab and upgrade' },
  { to: '/settings/quality', icon: SparklesIcon, title: 'Quality', blurb: 'Size limits, custom formats, and TRaSH Guides sync' },
  { to: '/settings/indexers', icon: MagnifyingGlassIcon, title: 'Indexers', blurb: 'Usenet indexers and torrent trackers to search' },
  { to: '/settings/importlists', icon: QueueListIcon, title: 'Import Lists', blurb: 'Discover and add events from external sources' },
  { to: '/settings/downloadclients', icon: ArrowDownTrayIcon, title: 'Download Clients', blurb: 'qBittorrent, SABnzbd, and friends' },
  { to: '/settings/notifications', icon: BellIcon, title: 'Notifications', blurb: 'Discord, Telegram, email, and webhooks' },
  { to: '/settings/general', icon: Cog6ToothIcon, title: 'General', blurb: 'Host, security, proxy, logging, and updates' },
  { to: '/settings/ui', icon: PaintBrushIcon, title: 'UI', blurb: 'Calendar, time zone, and display options' },
  { to: '/settings/tags', icon: TagIcon, title: 'Tags', blurb: 'Label leagues and wire tags to other settings' },
];
