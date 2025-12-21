import { useMemo } from 'react';
import {
  MagnifyingGlassIcon,
  ArrowDownTrayIcon,
  ArrowPathIcon,
  CheckCircleIcon,
  XCircleIcon,
  ClockIcon,
  QueueListIcon,
} from '@heroicons/react/24/outline';
import type { SearchQueueItem, DownloadQueueItem } from '../api/hooks';

interface EventStatusBadgeProps {
  eventId: number;
  part?: string;
  searchQueue?: {
    pendingSearches: SearchQueueItem[];
    activeSearches: SearchQueueItem[];
    recentlyCompleted: SearchQueueItem[];
  };
  downloadQueue?: DownloadQueueItem[];
}

type StatusType = 'queued' | 'searching' | 'grabbed' | 'downloading' | 'importing' | 'imported' | 'completed' | 'noResults' | 'failed' | null;

interface StatusInfo {
  type: StatusType;
  label: string;
  message?: string;
  quality?: string;
  progress?: number;
  releasesFound?: number;
  accentColor: string;
  bgColor: string;
  textColor: string;
  icon: React.ComponentType<{ className?: string }>;
  animate?: boolean;
}

/**
 * EventStatusBadge - Shows real-time search/download/import status for an event
 *
 * Displays in the same location as quality/CF score, but shows status when event
 * is being searched, downloaded, or imported. Once fully imported, this component
 * returns null and the parent shows the quality info instead.
 */
export default function EventStatusBadge({
  eventId,
  part,
  searchQueue,
  downloadQueue,
}: EventStatusBadgeProps) {
  const status = useMemo<StatusInfo | null>(() => {
    // Check search queue first (higher priority - event might be searching)
    if (searchQueue) {
      // Check if event is actively searching
      const activeSearch = searchQueue.activeSearches.find(
        s => s.eventId === eventId && (part ? s.part === part : !s.part)
      );
      if (activeSearch) {
        return {
          type: 'searching',
          label: 'Searching',
          message: activeSearch.message,
          releasesFound: activeSearch.releasesFound,
          accentColor: 'bg-blue-500',
          bgColor: 'bg-blue-900/30',
          textColor: 'text-blue-400',
          icon: MagnifyingGlassIcon,
          animate: true,
        };
      }

      // Check if event is queued for search
      const pendingSearch = searchQueue.pendingSearches.find(
        s => s.eventId === eventId && (part ? s.part === part : !s.part)
      );
      if (pendingSearch) {
        return {
          type: 'queued',
          label: 'Queued',
          message: 'Waiting to search...',
          accentColor: 'bg-gray-500',
          bgColor: 'bg-gray-800/50',
          textColor: 'text-gray-400',
          icon: QueueListIcon,
        };
      }

      // Check for recently completed search (show briefly before download starts)
      const recentSearch = searchQueue.recentlyCompleted.find(
        s => s.eventId === eventId && (part ? s.part === part : !s.part)
      );
      if (recentSearch) {
        const completedTime = recentSearch.completedAt ? new Date(recentSearch.completedAt).getTime() : 0;
        const now = Date.now();
        // Only show completed status for 3 seconds
        if (now - completedTime < 3000) {
          if (recentSearch.success) {
            return {
              type: 'completed',
              label: 'Found',
              message: recentSearch.selectedRelease || `${recentSearch.releasesFound} releases`,
              quality: recentSearch.quality || undefined,
              accentColor: 'bg-green-500',
              bgColor: 'bg-green-900/30',
              textColor: 'text-green-400',
              icon: CheckCircleIcon,
            };
          } else if (recentSearch.status === 'NoResults') {
            return {
              type: 'noResults',
              label: 'No Results',
              message: 'No matching releases found',
              accentColor: 'bg-yellow-500',
              bgColor: 'bg-yellow-900/30',
              textColor: 'text-yellow-400',
              icon: XCircleIcon,
            };
          } else if (recentSearch.status === 'Failed') {
            return {
              type: 'failed',
              label: 'Failed',
              message: recentSearch.message || 'Search failed',
              accentColor: 'bg-red-500',
              bgColor: 'bg-red-900/30',
              textColor: 'text-red-400',
              icon: XCircleIcon,
            };
          }
        }
      }
    }

    // Check download queue
    if (downloadQueue) {
      const downloadItem = downloadQueue.find(
        d => d.eventId === eventId && (part ? d.part === part : !d.part)
      );

      if (downloadItem) {
        // Status: 0=Queued/Grabbed, 1=Downloading, 2=Paused, 3=Completed, 4=Failed, 5=Warning, 6=Importing, 7=Imported
        switch (downloadItem.status) {
          case 0: // Queued/Grabbed
            return {
              type: 'grabbed',
              label: 'Grabbed',
              message: 'Sent to download client',
              quality: downloadItem.quality,
              accentColor: 'bg-blue-500',
              bgColor: 'bg-blue-900/30',
              textColor: 'text-blue-400',
              icon: ArrowDownTrayIcon,
            };
          case 1: // Downloading
            return {
              type: 'downloading',
              label: 'Downloading',
              message: downloadItem.progress > 0 ? `${Math.round(downloadItem.progress)}%` : 'In progress...',
              quality: downloadItem.quality,
              progress: downloadItem.progress,
              accentColor: 'bg-blue-500',
              bgColor: 'bg-blue-900/30',
              textColor: 'text-blue-400',
              icon: ArrowDownTrayIcon,
              animate: true,
            };
          case 2: // Paused
            return {
              type: 'downloading',
              label: 'Paused',
              message: 'Download paused',
              quality: downloadItem.quality,
              progress: downloadItem.progress,
              accentColor: 'bg-yellow-500',
              bgColor: 'bg-yellow-900/30',
              textColor: 'text-yellow-400',
              icon: ClockIcon,
            };
          case 6: // Importing
            return {
              type: 'importing',
              label: 'Importing',
              message: 'Importing to library...',
              quality: downloadItem.quality,
              accentColor: 'bg-yellow-500',
              bgColor: 'bg-yellow-900/30',
              textColor: 'text-yellow-400',
              icon: ArrowPathIcon,
              animate: true,
            };
          case 7: // Imported (briefly show before removing from queue)
            return {
              type: 'imported',
              label: 'Imported',
              message: 'Successfully imported',
              quality: downloadItem.quality,
              accentColor: 'bg-green-500',
              bgColor: 'bg-green-900/30',
              textColor: 'text-green-400',
              icon: CheckCircleIcon,
            };
          case 4: // Failed
            return {
              type: 'failed',
              label: 'Failed',
              message: 'Download failed',
              accentColor: 'bg-red-500',
              bgColor: 'bg-red-900/30',
              textColor: 'text-red-400',
              icon: XCircleIcon,
            };
          case 5: // Warning
            return {
              type: 'failed',
              label: 'Warning',
              message: 'Download warning',
              accentColor: 'bg-yellow-500',
              bgColor: 'bg-yellow-900/30',
              textColor: 'text-yellow-400',
              icon: XCircleIcon,
            };
        }
      }
    }

    return null;
  }, [eventId, part, searchQueue, downloadQueue]);

  if (!status) return null;

  const Icon = status.icon;

  return (
    <div className="flex items-center gap-2">
      <div className={`relative px-2.5 py-1 rounded ${status.bgColor} overflow-hidden`}>
        {/* Content */}
        <div className="flex items-center gap-1.5">
          <Icon className={`w-3.5 h-3.5 ${status.textColor} ${status.animate ? 'animate-pulse' : ''}`} />
          <span className={`text-xs font-medium ${status.textColor}`}>
            {status.label}
          </span>
          {status.quality && (
            <span className="text-xs text-gray-400">
              ({status.quality})
            </span>
          )}
          {status.releasesFound !== undefined && status.releasesFound > 0 && (
            <span className="text-xs text-green-400">
              • {status.releasesFound} found
            </span>
          )}
        </div>

        {/* Colored accent line at bottom */}
        <div className={`absolute bottom-0 left-0 right-0 h-0.5 ${status.accentColor}`}>
          {/* Progress bar for downloading */}
          {status.type === 'downloading' && status.progress !== undefined && status.progress > 0 && (
            <div
              className="absolute inset-0 bg-black/50"
              style={{
                left: `${Math.min(100, status.progress)}%`,
                right: 0
              }}
            />
          )}
          {/* Animated shimmer for active states */}
          {status.animate && (
            <div className="absolute inset-0 bg-gradient-to-r from-transparent via-white/30 to-transparent animate-shimmer" />
          )}
        </div>
      </div>
    </div>
  );
}
