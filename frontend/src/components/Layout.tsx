import { Link, Outlet, useLocation } from 'react-router-dom';
import { useSystemStatus } from '../api/hooks';
import {
  HomeIcon,
  FolderIcon,
  ClockIcon,
  Cog6ToothIcon,
  ServerIcon,
  ChevronDownIcon,
} from '@heroicons/react/24/outline';
import { useState } from 'react';

interface MenuItem {
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  path?: string;
  children?: { label: string; path: string }[];
}

export default function Layout() {
  const location = useLocation();
  const { data: systemStatus } = useSystemStatus();
  const [expandedMenus, setExpandedMenus] = useState<string[]>(['Events']);

  const toggleMenu = (label: string) => {
    setExpandedMenus((prev) =>
      prev.includes(label) ? prev.filter((m) => m !== label) : [...prev, label]
    );
  };

  const menuItems: MenuItem[] = [
    { label: 'Events', icon: FolderIcon, path: '/events' },
    { label: 'Calendar', icon: ClockIcon, path: '/calendar' },
    { label: 'Activity', icon: ClockIcon, path: '/activity' },
    {
      label: 'Settings',
      icon: Cog6ToothIcon,
      children: [
        { label: 'Media Management', path: '/settings/mediamanagement' },
        { label: 'Profiles', path: '/settings/profiles' },
        { label: 'Quality', path: '/settings/quality' },
        { label: 'Custom Formats', path: '/settings/customformats' },
        { label: 'Indexers', path: '/settings/indexers' },
        { label: 'Download Clients', path: '/settings/downloadclients' },
        { label: 'Notifications', path: '/settings/notifications' },
        { label: 'General', path: '/settings/general' },
        { label: 'UI', path: '/settings/ui' },
        { label: 'Tags', path: '/settings/tags' },
      ],
    },
    {
      label: 'System',
      icon: ServerIcon,
      children: [
        { label: 'Status', path: '/system/status' },
        { label: 'Tasks', path: '/system/tasks' },
        { label: 'Backup', path: '/system/backup' },
        { label: 'Updates', path: '/system/updates' },
        { label: 'Events', path: '/system/events' },
        { label: 'Log Files', path: '/system/logs' },
      ],
    },
  ];

  const isActive = (path?: string, children?: { path: string }[]) => {
    if (path) return location.pathname === path;
    if (children) return children.some((child) => location.pathname === child.path);
    return false;
  };

  return (
    <div className="flex h-screen bg-black text-gray-100">
      {/* Sidebar */}
      <aside className="w-64 bg-gradient-to-b from-gray-900 to-black border-r border-red-900/30 flex flex-col">
        {/* Logo */}
        <div className="p-4 border-b border-red-900/30">
          <Link to="/" className="flex items-center space-x-3">
            <img
              src="/logo-64.png"
              alt="Fightarr Logo"
              className="w-10 h-10 rounded-lg"
            />
            <div>
              <h1 className="text-xl font-bold text-white">Fightarr</h1>
              {systemStatus && (
                <p className="text-xs text-gray-400">v{systemStatus.version}</p>
              )}
            </div>
          </Link>
        </div>

        {/* Navigation */}
        <nav className="flex-1 overflow-y-auto py-4">
          {menuItems.map((item) => (
            <div key={item.label}>
              {item.children ? (
                // Menu with children (expandable)
                <div>
                  <button
                    onClick={() => toggleMenu(item.label)}
                    className={`w-full flex items-center justify-between px-4 py-2.5 text-sm font-medium transition-colors ${
                      isActive(item.path, item.children)
                        ? 'bg-red-900/30 text-white border-l-4 border-red-600'
                        : 'text-gray-300 hover:bg-red-900/10 hover:text-white'
                    }`}
                  >
                    <div className="flex items-center space-x-3">
                      <item.icon className="w-5 h-5" />
                      <span>{item.label}</span>
                    </div>
                    <ChevronDownIcon
                      className={`w-4 h-4 transition-transform ${
                        expandedMenus.includes(item.label) ? 'rotate-180' : ''
                      }`}
                    />
                  </button>
                  {expandedMenus.includes(item.label) && (
                    <div className="bg-black/30">
                      {item.children.map((child) => (
                        <Link
                          key={child.path}
                          to={child.path}
                          className={`block px-4 py-2 pl-12 text-sm transition-colors ${
                            location.pathname === child.path
                              ? 'bg-red-900/30 text-white border-l-4 border-red-600'
                              : 'text-gray-400 hover:bg-red-900/10 hover:text-white'
                          }`}
                        >
                          {child.label}
                        </Link>
                      ))}
                    </div>
                  )}
                </div>
              ) : (
                // Single menu item
                <Link
                  to={item.path!}
                  className={`flex items-center space-x-3 px-4 py-2.5 text-sm font-medium transition-colors ${
                    location.pathname === item.path
                      ? 'bg-red-900/30 text-white border-l-4 border-red-600'
                      : 'text-gray-300 hover:bg-red-900/10 hover:text-white'
                  }`}
                >
                  <item.icon className="w-5 h-5" />
                  <span>{item.label}</span>
                </Link>
              )}
            </div>
          ))}
        </nav>

        {/* Footer */}
        <div className="p-4 border-t border-red-900/30">
          <div className="text-xs text-gray-500">
            <p>© 2025 Fightarr</p>
            <p className="mt-1">Combat Sports Event Manager</p>
          </div>
        </div>
      </aside>

      {/* Main content */}
      <main className="flex-1 overflow-auto bg-gradient-to-br from-gray-950 via-black to-gray-950">
        <Outlet />
      </main>
    </div>
  );
}
