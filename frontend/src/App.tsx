import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Layout from './components/Layout';
import PlaceholderPage from './components/PlaceholderPage';
import EventsPage from './pages/EventsPage';
import AddEventPage from './pages/AddEventPage';
import SystemPage from './pages/SystemPage';
import SettingsPage from './pages/SettingsPage';

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
            <Route path="add-event" element={<AddEventPage />} />
            <Route path="library-import" element={<PlaceholderPage title="Library Import" description="Import existing events from your file system" />} />
            <Route path="mass-editor" element={<PlaceholderPage title="Mass Editor" description="Edit multiple events at once" />} />

            {/* Other Main Sections */}
            <Route path="calendar" element={<PlaceholderPage title="Calendar" description="View upcoming MMA events" />} />
            <Route path="activity" element={<PlaceholderPage title="Activity" description="Monitor download queue and history" />} />

            {/* Settings - Use Settings page with nested routes */}
            <Route path="settings/*" element={<SettingsPage />} />

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
