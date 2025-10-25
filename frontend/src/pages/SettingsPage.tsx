import { useState } from 'react';
import { NavLink, Routes, Route, Navigate } from 'react-router-dom';
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
} from '@heroicons/react/24/outline';

// Setting pages (to be created)
import MediaManagementSettings from './settings/MediaManagementSettings';
import ProfilesSettings from './settings/ProfilesSettings';
import QualitySettings from './settings/QualitySettings';
import CustomFormatsSettings from './settings/CustomFormatsSettings';
import IndexersSettings from './settings/IndexersSettings';
import ImportListsSettings from './settings/ImportListsSettings';
import DownloadClientsSettings from './settings/DownloadClientsSettings';
import NotificationsSettings from './settings/NotificationsSettings';
import MetadataSettings from './settings/MetadataSettings';
import GeneralSettings from './settings/GeneralSettings';
import UISettings from './settings/UISettings';
import TagsSettings from './settings/TagsSettings';

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
    description: 'Custom format conditions and scoring (Trash Guides compatible)',
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
    description: 'Configure download clients (qBittorrent, Transmission, etc.)',
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
  const [showAdvanced, setShowAdvanced] = useState(false);

  return (
    <div className="flex h-screen overflow-hidden">
      {/* Sidebar Navigation */}
      <div className="w-64 bg-gradient-to-br from-gray-900 to-black border-r border-red-900/30 overflow-y-auto flex-shrink-0">
        <div className="p-6">
          <div className="flex items-center mb-6">
            <Cog6ToothIcon className="w-8 h-8 text-red-600 mr-3" />
            <h1 className="text-2xl font-bold text-white">Settings</h1>
          </div>

          {/* Advanced Toggle */}
          <div className="mb-6">
            <label className="flex items-center space-x-3 cursor-pointer">
              <input
                type="checkbox"
                checked={showAdvanced}
                onChange={(e) => setShowAdvanced(e.target.checked)}
                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
              />
              <span className="text-sm text-gray-300">Show Advanced</span>
            </label>
          </div>

          {/* Navigation Links */}
          <nav className="space-y-1">
            {settingsNavigation.map((item) => (
              <NavLink
                key={item.path}
                to={item.path}
                className={({ isActive }) =>
                  `flex items-center px-4 py-3 rounded-lg transition-all group ${
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
      <div className="flex-1 overflow-y-auto">
        <div className="p-8">
          <Routes>
            <Route path="/" element={<Navigate to="/settings/mediamanagement" replace />} />
            <Route path="/mediamanagement" element={<MediaManagementSettings showAdvanced={showAdvanced} />} />
            <Route path="/profiles" element={<ProfilesSettings showAdvanced={showAdvanced} />} />
            <Route path="/quality" element={<QualitySettings showAdvanced={showAdvanced} />} />
            <Route path="/customformats" element={<CustomFormatsSettings showAdvanced={showAdvanced} />} />
            <Route path="/indexers" element={<IndexersSettings showAdvanced={showAdvanced} />} />
            <Route path="/importlists" element={<ImportListsSettings showAdvanced={showAdvanced} />} />
            <Route path="/downloadclients" element={<DownloadClientsSettings showAdvanced={showAdvanced} />} />
            <Route path="/connect" element={<NotificationsSettings showAdvanced={showAdvanced} />} />
            <Route path="/metadata" element={<MetadataSettings showAdvanced={showAdvanced} />} />
            <Route path="/general" element={<GeneralSettings showAdvanced={showAdvanced} />} />
            <Route path="/ui" element={<UISettings showAdvanced={showAdvanced} />} />
            <Route path="/tags" element={<TagsSettings showAdvanced={showAdvanced} />} />
          </Routes>
        </div>
      </div>
    </div>
  );
}
