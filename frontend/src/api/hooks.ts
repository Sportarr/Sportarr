import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from './client';
import type { Event, SystemStatus, Tag, QualityProfile, Indexer } from '../types';

// Events
export const useEvents = () => {
  return useQuery({
    queryKey: ['events'],
    queryFn: async () => {
      const { data } = await apiClient.get<Event[]>('/events');
      return data;
    },
  });
};

// System Status
export const useSystemStatus = () => {
  return useQuery({
    queryKey: ['system', 'status'],
    queryFn: async () => {
      const { data} = await apiClient.get<SystemStatus>('/system/status');
      return data;
    },
  });
};

// Tags
export const useTags = () => {
  return useQuery({
    queryKey: ['tags'],
    queryFn: async () => {
      const { data } = await apiClient.get<Tag[]>('/tag');
      return data;
    },
  });
};

// Quality Profiles
export const useQualityProfiles = () => {
  return useQuery({
    queryKey: ['qualityProfiles'],
    queryFn: async () => {
      const { data } = await apiClient.get<QualityProfile[]>('/qualityprofile');
      return data;
    },
  });
};

// Indexers
export const useIndexers = () => {
  return useQuery({
    queryKey: ['indexers'],
    queryFn: async () => {
      const { data } = await apiClient.get<Indexer[]>('/indexer');
      return data;
    },
    refetchInterval: 30000, // Auto-refresh every 30 seconds to show Prowlarr-synced indexers
  });
};

// Create Indexer
export const useCreateIndexer = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (indexer: Omit<Indexer, 'id'>) => {
      const { data } = await apiClient.post<Indexer>('/indexer', indexer);
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['indexers'] });
    },
  });
};

// Update Indexer
export const useUpdateIndexer = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (indexer: Indexer) => {
      const { data } = await apiClient.put<Indexer>(`/indexer/${indexer.id}`, indexer);
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['indexers'] });
    },
  });
};

// Delete Indexer
export const useDeleteIndexer = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      await apiClient.delete(`/indexer/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['indexers'] });
    },
  });
};

// Bulk Delete Indexers
export const useBulkDeleteIndexers = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (ids: number[]) => {
      await apiClient.post('/indexer/bulk/delete', { ids });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['indexers'] });
    },
  });
};

// Log Files
export interface LogFile {
  filename: string;
  lastWriteTime: string;
  size: number;
}

export interface LogFileContent {
  filename: string;
  content: string;
  lastWriteTime: string;
  size: number;
}

export const useLogFiles = () => {
  return useQuery({
    queryKey: ['logFiles'],
    queryFn: async () => {
      const { data } = await apiClient.get<LogFile[]>('/log/file');
      return data;
    },
    refetchInterval: 5000, // Auto-refresh every 5 seconds
  });
};

export const useLogFileContent = (filename: string | null) => {
  return useQuery({
    queryKey: ['logFile', filename],
    queryFn: async () => {
      if (!filename) return null;
      const { data } = await apiClient.get<LogFileContent>(`/log/file/${filename}`);
      return data;
    },
    enabled: !!filename,
    refetchInterval: 3000, // Auto-refresh every 3 seconds for real-time updates
  });
};

// Tasks
export interface AppTask {
  id: number;
  name: string;
  commandName: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled' | 'Aborting';
  queued: string;
  started: string | null;
  ended: string | null;
  duration: string | null;
  message: string | null;
  progress: number | null;
  priority: number;
  body: string | null;
  exception: string | null;
}

export const useTasks = (pageSize?: number) => {
  return useQuery({
    queryKey: ['tasks', pageSize],
    queryFn: async () => {
      const params = pageSize ? `?pageSize=${pageSize}` : '';
      const { data } = await apiClient.get<AppTask[]>(`/task${params}`);
      return data;
    },
    refetchInterval: 2000, // Auto-refresh every 2 seconds for real-time updates
  });
};

export const useTask = (id: number | null) => {
  return useQuery({
    queryKey: ['task', id],
    queryFn: async () => {
      if (!id) return null;
      const { data } = await apiClient.get<AppTask>(`/task/${id}`);
      return data;
    },
    enabled: !!id,
    refetchInterval: 1000, // Auto-refresh every 1 second for progress updates
  });
};

export const useQueueTask = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (task: { name: string; commandName: string; priority?: number; body?: string }) => {
      const { data } = await apiClient.post<AppTask>('/task', task);
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tasks'] });
    },
  });
};

export const useCancelTask = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => {
      await apiClient.delete(`/task/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tasks'] });
    },
  });
};

// Search Queue Status (for tracking event search progress on League Detail page)
export interface SearchQueueItem {
  id: string;
  eventId: number;
  eventTitle: string;
  part: string | null;
  status: 'Queued' | 'Searching' | 'Completed' | 'NoResults' | 'Failed' | 'Cancelled';
  message: string;
  queuedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  releasesFound: number;
  success: boolean;
  selectedRelease: string | null;
  quality: string | null;
}

export interface SearchQueueStatus {
  pendingCount: number;
  activeCount: number;
  maxConcurrent: number;
  pendingSearches: SearchQueueItem[];
  activeSearches: SearchQueueItem[];
  recentlyCompleted: SearchQueueItem[];
}

export const useSearchQueueStatus = () => {
  return useQuery({
    queryKey: ['searchQueueStatus'],
    queryFn: async () => {
      const { data } = await apiClient.get<SearchQueueStatus>('/search/queue');
      return data;
    },
    refetchInterval: 500, // Poll every 500ms for very responsive real-time updates
  });
};

// Download Queue Status (for tracking download/import progress on League Detail page)
export interface DownloadQueueItem {
  id: number;
  eventId: number;
  event?: { id: number; title: string };
  title: string;
  status: number; // 0=Queued, 1=Downloading, 2=Paused, 3=Completed, 4=Failed, 5=Warning, 6=Importing, 7=Imported
  quality?: string;
  progress: number;
  added: string;
  importedAt?: string;
  part?: string;
}

export const useDownloadQueue = () => {
  return useQuery({
    queryKey: ['downloadQueue'],
    queryFn: async () => {
      const { data } = await apiClient.get<DownloadQueueItem[]>('/queue');
      return data;
    },
    refetchInterval: 2000, // Poll for download status updates
  });
};
