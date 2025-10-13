import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Layout from './components/Layout';
import PlaceholderPage from './components/PlaceholderPage';
import EventsPage from './pages/EventsPage';
import SystemPage from './pages/SystemPage';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60 * 5, // 5 minutes
      retry: 1,
    },
  },
});

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter basename={window.Fightarr?.urlBase || ''}>
        <Routes>
          <Route path="/" element={<Layout />}>
            <Route index element={<Navigate to="/events" replace />} />
            <Route path="events" element={<EventsPage />} />

            {/* Events Menu */}
            <Route path="add-event" element={<PlaceholderPage title="Add New Event" description="Search and add MMA events to your library" />} />
            <Route path="library-import" element={<PlaceholderPage title="Library Import" description="Import existing events from your file system" />} />
            <Route path="mass-editor" element={<PlaceholderPage title="Mass Editor" description="Edit multiple events at once" />} />

            {/* Other Main Sections */}
            <Route path="calendar" element={<PlaceholderPage title="Calendar" description="View upcoming MMA events" />} />
            <Route path="activity" element={<PlaceholderPage title="Activity" description="Monitor download queue and history" />} />

            {/* Settings */}
            <Route path="settings/media-management" element={<PlaceholderPage title="Media Management" description="Configure file naming and organization" />} />
            <Route path="settings/profiles" element={<PlaceholderPage title="Quality Profiles" description="Manage quality and format preferences" />} />
            <Route path="settings/quality" element={<PlaceholderPage title="Quality Definitions" description="Define quality size limits" />} />
            <Route path="settings/indexers" element={<PlaceholderPage title="Indexers" description="Configure search indexers" />} />
            <Route path="settings/download-clients" element={<PlaceholderPage title="Download Clients" description="Configure download clients" />} />
            <Route path="settings/connect" element={<PlaceholderPage title="Connect" description="Setup notifications and connections" />} />
            <Route path="settings/metadata" element={<PlaceholderPage title="Metadata" description="Configure metadata providers" />} />
            <Route path="settings/tags" element={<PlaceholderPage title="Tags" description="Manage tags" />} />
            <Route path="settings/general" element={<PlaceholderPage title="General Settings" description="Configure general application settings" />} />
            <Route path="settings/ui" element={<PlaceholderPage title="UI Settings" description="Customize the user interface" />} />

            {/* System */}
            <Route path="system" element={<Navigate to="/system/status" replace />} />
            <Route path="system/status" element={<SystemPage />} />
            <Route path="system/tasks" element={<PlaceholderPage title="Tasks" description="View and manage scheduled tasks" />} />
            <Route path="system/backup" element={<PlaceholderPage title="Backup" description="Manage database backups" />} />
            <Route path="system/updates" element={<PlaceholderPage title="Updates" description="Check for application updates" />} />
            <Route path="system/events" element={<PlaceholderPage title="System Events" description="View system event log" />} />
            <Route path="system/logs" element={<PlaceholderPage title="Log Files" description="View application logs" />} />

            {/* Catch-all redirect to events */}
            <Route path="*" element={<Navigate to="/events" replace />} />
          </Route>
        </Routes>
      </BrowserRouter>
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
  );
}

export default App;
