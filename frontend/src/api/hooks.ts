import { useQuery } from '@tanstack/react-query';
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
