import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { useState, useEffect, useRef, lazy, Suspense } from 'react';
import { Toaster, toast } from 'sonner';
import Layout from './components/Layout';
import { ErrorBoundary } from './components/ErrorBoundary';
import { AuthProvider } from './contexts/AuthContext';
import ProtectedRoute from './components/ProtectedRoute';
const LeaguesPage = lazy(() => import('./pages/LeaguesPage'));
const LeagueDetailPage = lazy(() => import('./pages/LeagueDetailPage'));
const TeamsPage = lazy(() => import('./pages/TeamsPage'));
const EventSearchPage = lazy(() => import('./pages/EventSearchPage'));
const LeagueSearchPage = lazy(() => import('./pages/LeagueSearchPage'));
const CalendarPage = lazy(() => import('./pages/CalendarPage'));
const ActivityPage = lazy(() => import('./pages/ActivityPage'));
const LibraryImportPage = lazy(() => import('./pages/LibraryImportPage'));
const SystemPage = lazy(() => import('./pages/SystemPage'));
const SystemHealthPage = lazy(() => import('./pages/SystemHealthPage'));
const StatsPage = lazy(() => import('./pages/StatsPage'));
const BackupPage = lazy(() => import('./pages/BackupPage'));
const SystemEventsPage = lazy(() => import('./pages/SystemEventsPage'));
const SystemUpdatesPage = lazy(() => import('./pages/SystemUpdatesPage'));
const LogFilesPage = lazy(() => import('./pages/LogFilesPage'));
const TasksPage = lazy(() => import('./pages/TasksPage'));
const NotFoundPage = lazy(() => import('./pages/NotFoundPage'));
const LoginPage = lazy(() => import('./pages/LoginPage'));
const MediaManagementSettings = lazy(() => import('./pages/settings/MediaManagementSettings'));
const ProfilesSettings = lazy(() => import('./pages/settings/ProfilesSettings'));
const QualityPage = lazy(() => import('./pages/settings/QualityPage'));
const IndexersSettings = lazy(() => import('./pages/settings/IndexersSettings'));
const ImportListsSettings = lazy(() => import('./pages/settings/ImportListsSettings'));
const DownloadClientsSettings = lazy(() => import('./pages/settings/DownloadClientsSettings'));
const NotificationsSettings = lazy(() => import('./pages/settings/NotificationsSettings'));
const GeneralSettings = lazy(() => import('./pages/settings/GeneralSettings'));
const UISettings = lazy(() => import('./pages/settings/UISettings'));
const TagsSettings = lazy(() => import('./pages/settings/TagsSettings'));
const DevelopmentSettings = lazy(() => import('./pages/settings/DevelopmentSettings'));
const IptvSettings = lazy(() => import('./pages/settings/IptvSettings'));
const IptvChannelsSettings = lazy(() => import('./pages/settings/IptvChannelsSettings'));
const DvrRecordingsSettings = lazy(() => import('./pages/settings/DvrRecordingsSettings'));
const DvrSettingsPage = lazy(() => import('./pages/settings/DvrSettingsPage'));
const TvGuidePage = lazy(() => import('./pages/iptv/TvGuidePage'));
const WatchChannelPage = lazy(() => import('./pages/iptv/WatchChannelPage'));
import { API_CONTRACT_FAILURE_EVENT, type ApiContractFailureDetail } from './utils/apiContract';
import { apiGet } from './utils/api';
import { getRetryDelayWithBackoff, setGlobalBackoffCap } from './utils/queryBackoff';

// Hook to cleanup orphaned inert attributes from Headless UI modals
// This is a failsafe - the primary cleanup happens in modal afterLeave callbacks
function useInertCleanup() {
  useEffect(() => {
    // Headless UI dialogs mark the rest of the app `inert` (fully non-interactive)
    // while open. If a dialog's leave transition is interrupted - a fast double
    // tap, a navigation mid-animation, an unmount - the inert attribute can leak
    // and the whole page stops responding to taps. This is by far the worst on
    // touch devices, and was the cause of "can't click around on mobile".
    //
    // A dialog is only really open if it's actually RENDERED. A closed dialog can
    // linger in the DOM with role="dialog" but display:none; the old cleanup keyed
    // off presence (querySelector('[role="dialog"]')) so a lingering closed dialog
    // blocked cleanup and the shell stayed inert until the next full navigation.
    // Keying off visibility (getClientRects) fixes that - inert clears within one
    // tick of the dialog actually leaving the screen.
    const aDialogIsVisible = () =>
      Array.from(document.querySelectorAll('[role="dialog"]')).some(
        (d) => (d as HTMLElement).getClientRects().length > 0
      );

    const clearStuckInert = () => {
      if (aDialogIsVisible()) return; // a real dialog is open; leave the trap in place
      document.querySelectorAll('[inert]').forEach((el) => el.removeAttribute('inert'));
    };

    // Belt and suspenders: if a tap ever lands while nothing is actually open,
    // clear any stuck inert immediately so the very next interaction works.
    const onPointerDown = () => {
      if (!aDialogIsVisible() && document.querySelector('[inert]')) clearStuckInert();
    };

    const interval = setInterval(clearStuckInert, 300);
    document.addEventListener('pointerdown', onPointerDown, true);

    return () => {
      clearInterval(interval);
      document.removeEventListener('pointerdown', onPointerDown, true);
    };
  }, []);
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60 * 5, // 5 minutes
      retry: 1,
      retryDelay: (attemptIndex) => getRetryDelayWithBackoff(attemptIndex),
    },
  },
});

// Shown while a lazily-loaded route chunk is being fetched.
function RouteFallback() {
  return (
    <div className="flex h-[60vh] w-full items-center justify-center">
      <div className="h-8 w-8 animate-spin rounded-full border-b-2 border-red-600" />
    </div>
  );
}

function App() {
  // Global cleanup for orphaned inert attributes from Headless UI modals
  useInertCleanup();
  const lastApiContractToastRef = useRef<{ key: string; timestamp: number } | null>(null);

  useEffect(() => {
    const onApiContractFailure = (event: Event) => {
      const customEvent = event as CustomEvent<ApiContractFailureDetail>;
      const detail = customEvent.detail;
      const toastKey = `${detail.method}:${detail.url}:${detail.status}`;
      const now = Date.now();
      const lastToast = lastApiContractToastRef.current;

      if (lastToast && lastToast.key === toastKey && now - lastToast.timestamp < 5000) {
        return;
      }

      lastApiContractToastRef.current = { key: toastKey, timestamp: now };

      toast.error('API routing/configuration error', {
        description: `${detail.message} (${detail.method} ${detail.url})`,
      });
    };

    window.addEventListener(API_CONTRACT_FAILURE_EVENT, onApiContractFailure as EventListener);
    return () => {
      window.removeEventListener(API_CONTRACT_FAILURE_EVENT, onApiContractFailure as EventListener);
    };
  }, []);

  useEffect(() => {
    const loadBackoffCapFromSettings = async () => {
      try {
        const response = await apiGet('/api/settings');
        if (!response.ok) {
          return;
        }

        const data = await response.json();
        if (!data.uiSettings) {
          return;
        }

        const uiSettings = JSON.parse(data.uiSettings) as { queryBackoffCapMs?: number };
        if (typeof uiSettings.queryBackoffCapMs === 'number') {
          setGlobalBackoffCap(uiSettings.queryBackoffCapMs);
        }
      } catch {
        // Keep default backoff cap when settings cannot be loaded.
      }
    };

    loadBackoffCapFromSettings();
  }, []);

  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter basename={window.Sportarr?.urlBase || ''}>
          <Toaster position="top-right" theme="dark" richColors closeButton />
          <AuthProvider>
            <Suspense fallback={<RouteFallback />}>
            <Routes>
              {/* Login route (outside Layout and ProtectedRoute) */}
              <Route path="/login" element={<LoginPage />} />

              {/* All routes render inside Layout with ProtectedRoute wrapper */}
              <Route path="/" element={<ProtectedRoute><Layout /></ProtectedRoute>}>
            <Route index element={<Navigate to="/leagues" replace />} />
            <Route path="leagues" element={<LeaguesPage />} />
          <Route path="leagues/:id" element={<LeagueDetailPage />} />
            <Route path="add-league/search" element={<LeagueSearchPage />} />
            <Route path="add-team/search" element={<TeamsPage />} />

            {/* Events Menu */}
            <Route path="add-event/search" element={<EventSearchPage />} />
            <Route path="library-import" element={<LibraryImportPage />} />

            {/* Other Main Sections */}
            <Route path="calendar" element={<CalendarPage />} />
            <Route path="activity" element={<ActivityPage />} />
            <Route path="wanted" element={<Navigate to="/activity" replace />} />

            {/* IPTV Section */}
            <Route path="iptv" element={<Navigate to="/iptv/sources" replace />} />
            <Route path="iptv/sources" element={<IptvSettings />} />
            <Route path="iptv/channels" element={<IptvChannelsSettings />} />
            <Route path="iptv/guide" element={<TvGuidePage />} />
            <Route path="iptv/watch/:channelId" element={<WatchChannelPage />} />
            <Route path="iptv/schedule" element={<Navigate to="/iptv/guide?scheduledOnly=true" replace />} />
            <Route path="iptv/recordings" element={<DvrRecordingsSettings />} />
            <Route path="iptv/dvr-settings" element={<DvrSettingsPage />} />
            <Route path="iptv/coverage" element={<Navigate to="/iptv/channels?view=coverage" replace />} />

            {/* Settings - each page manages its own showAdvanced state */}
            <Route path="settings" element={<Navigate to="/settings/mediamanagement" replace />} />
            <Route path="settings/mediamanagement" element={<MediaManagementSettings />} />
            <Route path="settings/profiles" element={<ProfilesSettings />} />
            <Route path="settings/quality" element={<QualityPage />} />
            <Route path="settings/customformats" element={<Navigate to="/settings/quality?tab=customformats" replace />} />
            <Route path="settings/trashguides" element={<Navigate to="/settings/quality?tab=trashguides" replace />} />
            <Route path="settings/indexers" element={<IndexersSettings />} />
            <Route path="settings/importlists" element={<ImportListsSettings />} />
            <Route path="settings/downloadclients" element={<DownloadClientsSettings />} />
            <Route path="settings/notifications" element={<NotificationsSettings />} />
            <Route path="settings/general" element={<GeneralSettings />} />
            <Route path="settings/ui" element={<UISettings />} />
            <Route path="settings/tags" element={<TagsSettings />} />
            <Route path="settings/development" element={<DevelopmentSettings />} />

            {/* System */}
            <Route path="system" element={<Navigate to="/system/status" replace />} />
            <Route path="system/status" element={<SystemPage />} />
            <Route path="system/health" element={<SystemHealthPage />} />
            <Route path="system/tasks" element={<TasksPage />} />
            <Route path="system/stats" element={<StatsPage />} />
            <Route path="system/backup" element={<BackupPage />} />
            <Route path="system/updates" element={<SystemUpdatesPage />} />
            <Route path="system/events" element={<SystemEventsPage />} />
            <Route path="system/logs" element={<LogFilesPage />} />

            {/* 404 Not Found - catch-all for unknown routes */}
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
            </Suspense>
          </AuthProvider>
      </BrowserRouter>
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
    </ErrorBoundary>
  );
}

export default App;
