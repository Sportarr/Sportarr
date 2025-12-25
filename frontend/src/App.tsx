import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { useState, useEffect } from 'react';
import { Toaster } from 'sonner';
import Layout from './components/Layout';
import { ErrorBoundary } from './components/ErrorBoundary';
import { AuthProvider } from './contexts/AuthContext';
import ProtectedRoute from './components/ProtectedRoute';
import PlaceholderPage from './components/PlaceholderPage';
import EventsPage from './pages/EventsPage';
import LeaguesPage from './pages/LeaguesPage';
import LeagueDetailPage from './pages/LeagueDetailPage';
import TheSportsDBEventSearchPage from './pages/TheSportsDBEventSearchPage';
import TheSportsDBLeagueSearchPage from './pages/TheSportsDBLeagueSearchPage';
import CalendarPage from './pages/CalendarPage';
import ActivityPage from './pages/ActivityPage';
import WantedPage from './pages/WantedPage';
import LibraryImportPage from './pages/LibraryImportPage';
import SystemPage from './pages/SystemPage';
import SystemHealthPage from './pages/SystemHealthPage';
import BackupPage from './pages/BackupPage';
import SystemEventsPage from './pages/SystemEventsPage';
import SystemUpdatesPage from './pages/SystemUpdatesPage';
import LogFilesPage from './pages/LogFilesPage';
import TasksPage from './pages/TasksPage';
import NotFoundPage from './pages/NotFoundPage';
import LoginPage from './pages/LoginPage';
import MediaManagementSettings from './pages/settings/MediaManagementSettings';
import ProfilesSettings from './pages/settings/ProfilesSettings';
import QualitySettings from './pages/settings/QualitySettings';
import CustomFormatsSettings from './pages/settings/CustomFormatsSettings';
import TrashGuidesSettings from './pages/settings/TrashGuidesSettings';
import IndexersSettings from './pages/settings/IndexersSettings';
import ImportListsSettings from './pages/settings/ImportListsSettings';
import DownloadClientsSettings from './pages/settings/DownloadClientsSettings';
import NotificationsSettings from './pages/settings/NotificationsSettings';
import GeneralSettings from './pages/settings/GeneralSettings';
import UISettings from './pages/settings/UISettings';
import TagsSettings from './pages/settings/TagsSettings';
import IptvSettings from './pages/settings/IptvSettings';
import IptvChannelsSettings from './pages/settings/IptvChannelsSettings';
import DvrRecordingsSettings from './pages/settings/DvrRecordingsSettings';
import DvrSchedulePage from './pages/iptv/DvrSchedulePage';

// Hook to cleanup orphaned inert attributes from Headless UI modals
// This is a failsafe - the primary cleanup happens in modal afterLeave callbacks
function useInertCleanup() {
  useEffect(() => {
    // Track elements with inert attribute and when they were added
    const inertTimestamps = new Map<Element, number>();

    const observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        if (mutation.type === 'attributes' && mutation.attributeName === 'inert') {
          const target = mutation.target as Element;
          if (target.hasAttribute('inert')) {
            // Track when this element got inert
            inertTimestamps.set(target, Date.now());
          } else {
            // Inert was removed, stop tracking
            inertTimestamps.delete(target);
          }
        }
      }
    });

    // Check periodically for stale inert attributes (older than 500ms without a modal visible)
    const cleanupInterval = setInterval(() => {
      const now = Date.now();
      const hasVisibleModal = document.querySelector('[role="dialog"]') !== null;

      // Only cleanup if no modal is visible
      if (!hasVisibleModal) {
        document.querySelectorAll('[inert]').forEach((el) => {
          const timestamp = inertTimestamps.get(el);
          // Remove if tracked for more than 500ms, or if not tracked (legacy)
          if (!timestamp || now - timestamp > 500) {
            el.removeAttribute('inert');
            inertTimestamps.delete(el);
          }
        });
      }
    }, 200);

    // Start observing the document for inert attribute changes
    observer.observe(document.body, {
      attributes: true,
      attributeFilter: ['inert'],
      subtree: true,
    });

    return () => {
      observer.disconnect();
      clearInterval(cleanupInterval);
    };
  }, []);
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60 * 5, // 5 minutes
      retry: 1,
    },
  },
});

function App() {
  // Global cleanup for orphaned inert attributes from Headless UI modals
  useInertCleanup();

  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter basename={window.Sportarr?.urlBase || ''}>
          <Toaster position="top-right" theme="dark" richColors closeButton />
          <AuthProvider>
            <Routes>
              {/* Login route (outside Layout and ProtectedRoute) */}
              <Route path="/login" element={<LoginPage />} />

              {/* All routes render inside Layout with ProtectedRoute wrapper */}
              <Route path="/" element={<ProtectedRoute><Layout /></ProtectedRoute>}>
            <Route index element={<Navigate to="/leagues" replace />} />
            <Route path="leagues" element={<LeaguesPage />} />
            <Route path="leagues/:id" element={<LeagueDetailPage />} />
            <Route path="add-league/search" element={<TheSportsDBLeagueSearchPage />} />

            {/* Events Menu */}
            <Route path="add-event/search" element={<TheSportsDBEventSearchPage />} />
            <Route path="library-import" element={<LibraryImportPage />} />

            {/* Other Main Sections */}
            <Route path="calendar" element={<CalendarPage />} />
            <Route path="activity" element={<ActivityPage />} />
            <Route path="wanted" element={<WantedPage />} />

            {/* IPTV Section */}
            <Route path="iptv" element={<Navigate to="/iptv/sources" replace />} />
            <Route path="iptv/sources" element={<IptvSettings />} />
            <Route path="iptv/channels" element={<IptvChannelsSettings />} />
            <Route path="iptv/schedule" element={<DvrSchedulePage />} />
            <Route path="iptv/recordings" element={<DvrRecordingsSettings />} />

            {/* Settings - each page manages its own showAdvanced state */}
            <Route path="settings" element={<Navigate to="/settings/mediamanagement" replace />} />
            <Route path="settings/mediamanagement" element={<MediaManagementSettings />} />
            <Route path="settings/profiles" element={<ProfilesSettings />} />
            <Route path="settings/quality" element={<QualitySettings />} />
            <Route path="settings/customformats" element={<CustomFormatsSettings />} />
            <Route path="settings/trashguides" element={<TrashGuidesSettings />} />
            <Route path="settings/indexers" element={<IndexersSettings />} />
            <Route path="settings/importlists" element={<ImportListsSettings />} />
            <Route path="settings/downloadclients" element={<DownloadClientsSettings />} />
            <Route path="settings/notifications" element={<NotificationsSettings />} />
            <Route path="settings/general" element={<GeneralSettings />} />
            <Route path="settings/ui" element={<UISettings />} />
            <Route path="settings/tags" element={<TagsSettings />} />

            {/* System */}
            <Route path="system" element={<Navigate to="/system/status" replace />} />
            <Route path="system/status" element={<SystemPage />} />
            <Route path="system/health" element={<SystemHealthPage />} />
            <Route path="system/tasks" element={<TasksPage />} />
            <Route path="system/backup" element={<BackupPage />} />
            <Route path="system/updates" element={<SystemUpdatesPage />} />
            <Route path="system/events" element={<SystemEventsPage />} />
            <Route path="system/logs" element={<LogFilesPage />} />

            {/* 404 Not Found - catch-all for unknown routes */}
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
          </AuthProvider>
      </BrowserRouter>
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
    </ErrorBoundary>
  );
}

export default App;
