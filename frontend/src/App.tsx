import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { useState } from 'react';
import Layout from './components/Layout';
import { ErrorBoundary } from './components/ErrorBoundary';
import PlaceholderPage from './components/PlaceholderPage';
import EventsPage from './pages/EventsPage';
import AddEventPage from './pages/AddEventPage';
import CalendarPage from './pages/CalendarPage';
import SystemPage from './pages/SystemPage';
import NotFoundPage from './pages/NotFoundPage';
import LoginPage from './pages/LoginPage';
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
          <Routes>
            {/* Login route (outside Layout) */}
            <Route path="/login" element={<LoginPage />} />

            {/* All routes render inside Layout */}
            <Route path="/" element={<Layout />}>
            <Route index element={<Navigate to="/events" replace />} />
            <Route path="events" element={<EventsPage />} />

            {/* Events Menu */}
            <Route path="add-event" element={<AddEventPage />} />
            <Route path="library-import" element={<PlaceholderPage title="Library Import" description="Import existing events from your file system" />} />
            <Route path="mass-editor" element={<PlaceholderPage title="Mass Editor" description="Edit multiple events at once" />} />

            {/* Other Main Sections */}
            <Route path="calendar" element={<CalendarPage />} />
            <Route path="activity" element={<PlaceholderPage title="Activity" description="Monitor download queue and history" />} />

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
            <Route path="system/tasks" element={<PlaceholderPage title="Tasks" description="View and manage scheduled tasks" />} />
            <Route path="system/backup" element={<PlaceholderPage title="Backup" description="Manage database backups" />} />
            <Route path="system/updates" element={<PlaceholderPage title="Updates" description="Check for application updates" />} />
            <Route path="system/events" element={<PlaceholderPage title="System Events" description="View system event log" />} />
            <Route path="system/logs" element={<PlaceholderPage title="Log Files" description="View application logs" />} />

            {/* 404 Not Found - catch-all for unknown routes */}
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
    </ErrorBoundary>
  );
}

export default App;
