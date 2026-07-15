import { useState, useEffect } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import {
  TrophyIcon,
  CalendarIcon,
  ClockIcon,
  SignalIcon,
  ServerIcon,
} from '@heroicons/react/24/outline';
import {
  TrophyIcon as TrophySolidIcon,
  CalendarIcon as CalendarSolidIcon,
  ClockIcon as ClockSolidIcon,
  SignalIcon as SignalSolidIcon,
  ServerIcon as ServerSolidIcon,
} from '@heroicons/react/24/solid';
import { useActivityCounts } from '../api/hooks';

interface TabChild {
  label: string;
  path: string;
}

/**
 * Phone-only bottom tab bar covering every destination (owner-designed nav
 * end-state): Leagues, Calendar, Activity, IPTV, Status. There is no More
 * tab and no drawer on phones - tabs with sub-pages open a pill menu that
 * animates up from the bar instead of routing through a hub page, so no
 * back-navigation is ever needed. Settings lives behind the top bar's gear.
 * Childless tabs navigate immediately. Hidden at md+ where the sidebar exists.
 */
export default function MobileTabBar() {
  const location = useLocation();
  const navigate = useNavigate();
  const { data: activityCounts } = useActivityCounts();
  const [openMenu, setOpenMenu] = useState<string | null>(null);

  // Any navigation closes an open pill.
  useEffect(() => {
    setOpenMenu(null);
  }, [location.pathname]);

  const activityBadge = activityCounts
    ? (activityCounts.queueCount + (activityCounts.pendingImportCount ?? 0)) || undefined
    : undefined;

  const tabs: {
    label: string;
    path: string;
    match: string[];
    icon: React.ComponentType<{ className?: string }>;
    activeIcon: React.ComponentType<{ className?: string }>;
    badge?: number;
    children?: TabChild[];
  }[] = [
    {
      label: 'Library', path: '/leagues', match: ['/leagues', '/add-league', '/add-team', '/library-import', '/events'],
      icon: TrophyIcon, activeIcon: TrophySolidIcon,
      children: [
        { label: 'Leagues', path: '/leagues' },
        { label: 'Add League', path: '/add-league/search' },
        { label: 'Add Team', path: '/add-team/search' },
        { label: 'Import', path: '/library-import' },
      ],
    },
    { label: 'Calendar', path: '/calendar', match: ['/calendar'], icon: CalendarIcon, activeIcon: CalendarSolidIcon },
    { label: 'Activity', path: '/activity', match: ['/activity'], icon: ClockIcon, activeIcon: ClockSolidIcon, badge: activityBadge },
    {
      label: 'IPTV', path: '/iptv', match: ['/iptv'],
      icon: SignalIcon, activeIcon: SignalSolidIcon,
      children: [
        { label: 'Sources', path: '/iptv/sources' },
        { label: 'Channels', path: '/iptv/channels' },
        { label: 'TV Guide', path: '/iptv/guide' },
        { label: 'Recordings', path: '/iptv/recordings' },
        { label: 'DVR Settings', path: '/iptv/dvr-settings' },
      ],
    },
    {
      label: 'Status', path: '/system/status', match: ['/system'],
      icon: ServerIcon, activeIcon: ServerSolidIcon,
      children: [
        { label: 'Status', path: '/system/status' },
        { label: 'Health', path: '/system/health' },
        { label: 'Tasks', path: '/system/tasks' },
        { label: 'Stats', path: '/system/stats' },
        { label: 'Backup', path: '/system/backup' },
        { label: 'Updates', path: '/system/updates' },
        { label: 'Events', path: '/system/events' },
        { label: 'Log Files', path: '/system/logs' },
      ],
    },
  ];

  const openTab = tabs.find((t) => t.label === openMenu);

  return (
    <>
      {/* Invisible backdrop while a pill is open - tap anywhere else to close */}
      {openMenu && (
        <div className="fixed inset-0 z-40 md:hidden" onClick={() => setOpenMenu(null)} />
      )}

      <nav
        className="fixed bottom-0 left-0 right-0 z-50 md:hidden border-t border-red-900/30 bg-gradient-to-t from-black to-gray-900"
        style={{ paddingBottom: 'env(safe-area-inset-bottom)' }}
      >
        {/* Pill menu for the open section */}
        {openTab?.children && (
          <div className="absolute inset-x-3 bottom-full mb-2 animate-pill-up">
            <div className="max-h-[70dvh] overflow-y-auto rounded-2xl border border-red-900/40 bg-gradient-to-b from-gray-900 to-black shadow-2xl shadow-black/70">
              {openTab.children.map((child) => {
                const current = location.pathname === child.path
                  || (child.path !== '/leagues' && location.pathname.startsWith(child.path));
                return (
                  <button
                    key={child.path}
                    onClick={() => {
                      setOpenMenu(null);
                      navigate(child.path);
                    }}
                    className={`flex w-full items-center justify-between px-5 py-2.5 text-left text-sm font-medium border-b border-gray-800/60 last:border-b-0 ${
                      current ? 'text-red-500' : 'text-gray-200'
                    }`}
                  >
                    {child.label}
                    {current && <span className="h-1.5 w-1.5 rounded-full bg-red-500" />}
                  </button>
                );
              })}
            </div>
          </div>
        )}

        <div className="flex h-16">
          {tabs.map((tab) => {
            const active = tab.match.some((m) => location.pathname.startsWith(m));
            // While a pill is open, only the open tab shows emphasis - otherwise
            // the current section and the browsed section both light up red.
            const emphasized = openMenu ? openMenu === tab.label : active;
            const Icon = emphasized ? tab.activeIcon : tab.icon;
            const tint = emphasized ? 'text-red-500' : 'text-gray-400';
            const inner = (
              <>
                <span className="relative">
                  <Icon className="h-6 w-6" />
                  {tab.badge !== undefined && tab.badge > 0 && (
                    <span className="absolute -right-2.5 -top-1.5 min-w-[16px] rounded-full bg-red-600 px-1 text-center text-[9px] font-bold leading-4 text-white">
                      {tab.badge > 99 ? '99+' : tab.badge}
                    </span>
                  )}
                </span>
                <span className={`text-[10px] leading-none ${emphasized ? 'font-semibold' : 'font-medium'}`}>
                  {tab.label}
                </span>
              </>
            );
            return tab.children ? (
              <button
                key={tab.label}
                onClick={() => setOpenMenu(openMenu === tab.label ? null : tab.label)}
                className={`relative flex flex-1 flex-col items-center justify-center gap-1 ${tint}`}
              >
                {inner}
              </button>
            ) : (
              <Link
                key={tab.label}
                to={tab.path}
                onClick={() => setOpenMenu(null)}
                className={`relative flex flex-1 flex-col items-center justify-center gap-1 ${tint}`}
              >
                {inner}
              </Link>
            );
          })}
        </div>
      </nav>
    </>
  );
}
