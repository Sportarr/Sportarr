import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { useState } from 'react';
import Layout from './components/Layout';
import { ErrorBoundary } from './components/ErrorBoundary';
import { AuthProvider } from './contexts/AuthContext';
import ProtectedRoute from './components/ProtectedRoute';
import PlaceholderPage from './components/PlaceholderPage';
import EventsPage from './pages/EventsPage';
import AddEventPage from './pages/AddEventPage';
import EventSearchPage from './pages/EventSearchPage';
import CalendarPage from './pages/CalendarPage';
import ActivityPage from './pages/ActivityPage';
import WantedPage from './pages/WantedPage';
import SystemPage from './pages/SystemPage';
import SystemHealthPage from './pages/SystemHealthPage';
import BackupPage from './pages/BackupPage';
import SystemEventsPage from './pages/SystemEventsPage';
import LogFilesPage from './pages/LogFilesPage';
import TasksPage from './pages/TasksPage';
import NotFoundPage from './pages/NotFoundPage';
import LoginPage from './pages/LoginPage';
import InitialSetupPage from './pages/InitialSetupPage';
import MediaManagementSettings from './pages/settings/MediaManagementSettings';
import ProfilesSettings from './pages/settings/ProfilesSettings';
import QualitySettings from './pages/settings/QualitySettings';
import CustomFormatsSettings from './pages/settings/CustomFormatsSettings';
import IndexersSettings from './pages/settings/IndexersSettings';
import DownloadClientsSettings from './pages/settings/DownloadClientsSettings';
import NotificationsSettings from './pages/settings/NotificationsSettings';
import GeneralSettings from './pages/settings/GeneralSettings';
import UISettings from './pages/settings/UISettings';
import TagsSettings from './pages/settings/TagsSettings';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60 * 5, // 5 minutes
      retry: 1,
    },
  },
});

function App() {
  const [showAdvanced, setShowAdvanced] = useState(false);

  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter basename={window.Fightarr?.urlBase || ''}>
          <AuthProvider>
            <Routes>
              {/* Setup and Login routes (outside Layout and ProtectedRoute) */}
              <Route path="/setup" element={<InitialSetupPage />} />
              <Route path="/login" element={<LoginPage />} />

              {/* All routes render inside Layout with ProtectedRoute wrapper */}
              <Route path="/" element={<ProtectedRoute><Layout /></ProtectedRoute>}>
            <Route index element={<Navigate to="/events" replace />} />
            <Route path="events" element={<EventsPage />} />

            {/* Events Menu */}
            <Route path="add-event" element={<AddEventPage />} />
            <Route path="add-event/search" element={<EventSearchPage />} />
            <Route path="library-import" element={<PlaceholderPage title="Library Import" description="Import existing events from your file system" />} />
            <Route path="mass-editor" element={<PlaceholderPage title="Mass Editor" description="Edit multiple events at once" />} />

            {/* Other Main Sections */}
            <Route path="calendar" element={<CalendarPage />} />
            <Route path="activity" element={<ActivityPage />} />
            <Route path="wanted" element={<WantedPage />} />

            {/* Settings */}
            <Route path="settings" element={<Navigate to="/settings/mediamanagement" replace />} />
            <Route path="settings/mediamanagement" element={<MediaManagementSettings showAdvanced={showAdvanced} />} />
            <Route path="settings/profiles" element={<ProfilesSettings showAdvanced={showAdvanced} />} />
            <Route path="settings/quality" element={<QualitySettings showAdvanced={showAdvanced} />} />
            <Route path="settings/customformats" element={<CustomFormatsSettings showAdvanced={showAdvanced} />} />
            <Route path="settings/indexers" element={<IndexersSettings showAdvanced={showAdvanced} />} />
            <Route path="settings/downloadclients" element={<DownloadClientsSettings showAdvanced={showAdvanced} />} />
            <Route path="settings/notifications" element={<NotificationsSettings showAdvanced={showAdvanced} />} />
            <Route path="settings/general" element={<GeneralSettings showAdvanced={showAdvanced} />} />
            <Route path="settings/ui" element={<UISettings showAdvanced={showAdvanced} />} />
            <Route path="settings/tags" element={<TagsSettings showAdvanced={showAdvanced} />} />

            {/* System */}
            <Route path="system" element={<Navigate to="/system/status" replace />} />
            <Route path="system/status" element={<SystemPage />} />
            <Route path="system/health" element={<SystemHealthPage />} />
            <Route path="system/tasks" element={<TasksPage />} />
            <Route path="system/backup" element={<BackupPage />} />
            <Route path="system/updates" element={<PlaceholderPage title="Updates" description="Check for application updates" />} />
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
