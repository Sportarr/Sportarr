import { NavLink, Routes, Route, Navigate } from 'react-router-dom';
import { useState } from 'react';
import {
  Cog6ToothIcon,
  FolderIcon,
  AdjustmentsHorizontalIcon,
  Square3Stack3DIcon,
  PuzzlePieceIcon,
  MagnifyingGlassIcon,
  ListBulletIcon,
  ArrowDownTrayIcon,
  BellIcon,
  FilmIcon,
  ServerIcon,
  PaintBrushIcon,
  TagIcon,
  CloudArrowDownIcon,
  Bars3Icon,
  XMarkIcon,
  SignalIcon,
  VideoCameraIcon,
} from '@heroicons/react/24/outline';

// Setting pages (to be created)
import MediaManagementSettings from './settings/MediaManagementSettings';
import ProfilesSettings from './settings/ProfilesSettings';
import QualitySettings from './settings/QualitySettings';
import CustomFormatsSettings from './settings/CustomFormatsSettings';
import TrashGuidesSettings from './settings/TrashGuidesSettings';
import IndexersSettings from './settings/IndexersSettings';
import ImportListsSettings from './settings/ImportListsSettings';
import DownloadClientsSettings from './settings/DownloadClientsSettings';
import NotificationsSettings from './settings/NotificationsSettings';
import MetadataSettings from './settings/MetadataSettings';
import GeneralSettings from './settings/GeneralSettings';
import UISettings from './settings/UISettings';
import TagsSettings from './settings/TagsSettings';
import IptvSettings from './settings/IptvSettings';
import IptvChannelsSettings from './settings/IptvChannelsSettings';
import DvrRecordingsSettings from './settings/DvrRecordingsSettings';

interface SettingsNavItem {
  name: string;
  path: string;
  icon: React.ComponentType<{ className?: string }>;
  description: string;
}

const settingsNavigation: SettingsNavItem[] = [
  {
    name: 'Media Management',
    path: '/settings/mediamanagement',
    icon: FolderIcon,
    description: 'File naming, root folders, and file management',
  },
  {
    name: 'Profiles',
    path: '/settings/profiles',
    icon: AdjustmentsHorizontalIcon,
    description: 'Quality and language profiles',
  },
  {
    name: 'Quality',
    path: '/settings/quality',
    icon: Square3Stack3DIcon,
    description: 'Quality definitions and sizes',
  },
  {
    name: 'Custom Formats',
    path: '/settings/customformats',
    icon: PuzzlePieceIcon,
    description: 'Custom format conditions and scoring',
  },
  {
    name: 'TRaSH Guides',
    path: '/settings/trashguides',
    icon: CloudArrowDownIcon,
    description: 'Sync custom formats and scores from TRaSH Guides',
  },
  {
    name: 'Indexers',
    path: '/settings/indexers',
    icon: MagnifyingGlassIcon,
    description: 'Usenet indexers and torrent trackers',
  },
  {
    name: 'Import Lists',
    path: '/settings/importlists',
    icon: ListBulletIcon,
    description: 'Automated event discovery from external sources',
  },
  {
    name: 'Download Clients',
    path: '/settings/downloadclients',
    icon: ArrowDownTrayIcon,
    description: 'Configure download clients (qBittorrent, SABnzbd, Decypharr, etc.)',
  },
  {
    name: 'IPTV Sources',
    path: '/settings/iptv',
    icon: SignalIcon,
    description: 'Configure IPTV sources for DVR recording of sports events',
  },
  {
    name: 'IPTV Channels',
    path: '/settings/iptv-channels',
    icon: SignalIcon,
    description: 'Manage channels and map them to leagues for DVR recording',
  },
  {
    name: 'DVR Recordings',
    path: '/settings/dvr',
    icon: VideoCameraIcon,
    description: 'View and manage scheduled and completed DVR recordings',
  },
  {
    name: 'Connect',
    path: '/settings/connect',
    icon: BellIcon,
    description: 'Notifications and connections to other services',
  },
  {
    name: 'Metadata',
    path: '/settings/metadata',
    icon: FilmIcon,
    description: 'NFO files and images for media servers (Kodi, Plex, Emby)',
  },
  {
    name: 'General',
    path: '/settings/general',
    icon: ServerIcon,
    description: 'General application settings',
  },
  {
    name: 'UI',
    path: '/settings/ui',
    icon: PaintBrushIcon,
    description: 'User interface preferences',
  },
  {
    name: 'Tags',
    path: '/settings/tags',
    icon: TagIcon,
    description: 'Manage tags for events, profiles, and indexers',
  },
];

export default function SettingsPage() {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  return (
    <div className="flex h-full overflow-hidden">
      {/* Mobile Settings Header */}
      <div className="fixed top-14 left-0 right-0 z-30 md:hidden bg-gradient-to-r from-gray-900 to-black border-b border-red-900/30">
        <div className="flex items-center justify-between p-3">
          <div className="flex items-center">
            <Cog6ToothIcon className="w-6 h-6 text-red-600 mr-2" />
            <h1 className="text-lg font-bold text-white">Settings</h1>
          </div>
          <button
            onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
            className="p-2 text-gray-300 hover:text-white hover:bg-red-900/30 rounded-lg transition-colors"
          >
            {mobileMenuOpen ? (
              <XMarkIcon className="w-6 h-6" />
            ) : (
              <Bars3Icon className="w-6 h-6" />
            )}
          </button>
        </div>
      </div>

      {/* Mobile Menu Overlay */}
      {mobileMenuOpen && (
        <div
          className="fixed inset-0 bg-black/80 z-20 md:hidden"
          onClick={() => setMobileMenuOpen(false)}
        />
      )}

      {/* Sidebar Navigation - Hidden on mobile unless menu is open */}
      <div className={`
        fixed md:relative inset-y-0 left-0 z-20
        w-64 bg-gradient-to-br from-gray-900 to-black border-r border-red-900/30 overflow-y-auto flex-shrink-0
        transform transition-transform duration-300 ease-in-out
        ${mobileMenuOpen ? 'translate-x-0' : '-translate-x-full'}
        md:translate-x-0
        pt-28 md:pt-0
      `}>
        <div className="p-4 md:p-6">
          <div className="hidden md:flex items-center mb-6">
            <Cog6ToothIcon className="w-8 h-8 text-red-600 mr-3" />
            <h1 className="text-2xl font-bold text-white">Settings</h1>
          </div>

          {/* Navigation Links */}
          <nav className="space-y-1">
            {settingsNavigation.map((item) => (
              <NavLink
                key={item.path}
                to={item.path}
                onClick={() => setMobileMenuOpen(false)}
                className={({ isActive }) =>
                  `flex items-center px-3 md:px-4 py-2.5 md:py-3 rounded-lg transition-all group ${
                    isActive
                      ? 'bg-red-600 text-white'
                      : 'text-gray-400 hover:bg-gray-800 hover:text-white'
                  }`
                }
              >
                <item.icon className="w-5 h-5 mr-3 flex-shrink-0" />
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium truncate">{item.name}</p>
                </div>
              </NavLink>
            ))}
          </nav>
        </div>
      </div>

      {/* Main Content Area */}
      <div className="flex-1 overflow-y-auto pt-14 md:pt-0">
        <div className="p-4 md:p-8">
          <Routes>
            <Route path="/" element={<Navigate to="/settings/mediamanagement" replace />} />
            <Route path="/mediamanagement" element={<MediaManagementSettings />} />
            <Route path="/profiles" element={<ProfilesSettings />} />
            <Route path="/quality" element={<QualitySettings />} />
            <Route path="/customformats" element={<CustomFormatsSettings />} />
            <Route path="/trashguides" element={<TrashGuidesSettings />} />
            <Route path="/indexers" element={<IndexersSettings />} />
            <Route path="/importlists" element={<ImportListsSettings />} />
            <Route path="/downloadclients" element={<DownloadClientsSettings />} />
            <Route path="/iptv" element={<IptvSettings />} />
            <Route path="/iptv-channels" element={<IptvChannelsSettings />} />
            <Route path="/dvr" element={<DvrRecordingsSettings />} />
            <Route path="/connect" element={<NotificationsSettings />} />
            <Route path="/metadata" element={<MetadataSettings />} />
            <Route path="/general" element={<GeneralSettings />} />
            <Route path="/ui" element={<UISettings />} />
            <Route path="/tags" element={<TagsSettings />} />
          </Routes>
        </div>
      </div>
    </div>
  );
}
