import { Fragment, useState, useEffect } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { XMarkIcon, TrashIcon, FolderIcon, FilmIcon, ChevronDownIcon, ChevronRightIcon, ArrowPathIcon } from '@heroicons/react/24/outline';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../api/client';
import { toast } from 'sonner';

interface RenamePreviewItem {
  leagueId: number;
  leagueName: string;
  existingPath: string;
  newPath: string;
  changes: Array<{ field: string; oldValue: string; newValue: string }>;
}

interface LeagueFile {
  id: number;
  eventId: number;
  eventTitle: string;
  eventDate: string;
  season: string;
  filePath: string;
  size: number;
  quality?: string;
  qualityScore?: number;
  customFormatScore?: number;
  partName?: string;
  partNumber?: number;
  added: string;
  exists: boolean;
  fileName: string;
}

interface LeagueFilesResponse {
  leagueId: number;
  leagueName: string;
  season?: string;
  totalFiles: number;
  totalSize: number;
  files: LeagueFile[];
}

interface LeagueFilesModalProps {
  isOpen: boolean;
  onClose: () => void;
  leagueId: number;
  leagueName: string;
  season?: string; // If provided, only show files for this season
}

function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function formatDate(dateString: string): string {
  return new Date(dateString).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric'
  });
}

export default function LeagueFilesModal({
  isOpen,
  onClose,
  leagueId,
  leagueName,
  season,
}: LeagueFilesModalProps) {
  const queryClient = useQueryClient();
  const [expandedSeasons, setExpandedSeasons] = useState<Set<string>>(new Set());
  // Local state for immediate UI updates when files are deleted
  const [deletedFileIds, setDeletedFileIds] = useState<Set<number>>(new Set());

  // Rename functionality state
  const [showRenamePreview, setShowRenamePreview] = useState(false);
  const [isLoadingPreview, setIsLoadingPreview] = useState(false);
  const [renamePreview, setRenamePreview] = useState<RenamePreviewItem[]>([]);
  const [isRenaming, setIsRenaming] = useState(false);

  // Reset deleted file IDs and rename state when modal opens/closes or data changes
  useEffect(() => {
    if (!isOpen) {
      setDeletedFileIds(new Set());
      setShowRenamePreview(false);
      setRenamePreview([]);
    }
  }, [isOpen]);

  // Fetch files
  const { data, isLoading } = useQuery<LeagueFilesResponse>({
    queryKey: season
      ? ['league-season-files', leagueId, season]
      : ['league-files', leagueId],
    queryFn: async () => {
      const url = season
        ? `/leagues/${leagueId}/seasons/${encodeURIComponent(season)}/files`
        : `/leagues/${leagueId}/files`;
      const response = await apiClient.get(url);
      return response.data;
    },
    enabled: isOpen && !!leagueId,
  });

  // Filter out deleted files from the display
  const displayFiles = data?.files.filter(f => !deletedFileIds.has(f.id)) || [];

  // Delete single file mutation
  const deleteFileMutation = useMutation({
    mutationFn: async ({ eventId, fileId }: { eventId: number; fileId: number }) => {
      const response = await apiClient.delete(`/events/${eventId}/files/${fileId}`);
      return { data: response.data, fileId };
    },
    onSuccess: async ({ fileId }) => {
      toast.success('File deleted');
      // Immediately hide the deleted file in UI
      setDeletedFileIds(prev => new Set(prev).add(fileId));
      // Refetch files list in background
      if (season) {
        await queryClient.refetchQueries({ queryKey: ['league-season-files', leagueId, season] });
      } else {
        await queryClient.refetchQueries({ queryKey: ['league-files', leagueId] });
      }
      // Also refresh league events and league data
      await queryClient.refetchQueries({ queryKey: ['league-events', leagueId.toString()] });
      await queryClient.refetchQueries({ queryKey: ['league', leagueId.toString()] });
      await queryClient.refetchQueries({ queryKey: ['leagues'] });
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.detail || 'Failed to delete file');
    },
  });

  // Load rename preview for this league
  const loadRenamePreview = async () => {
    setIsLoadingPreview(true);
    setShowRenamePreview(true);
    try {
      const response = await apiClient.post('/leagues/rename/preview', {
        leagueIds: [leagueId]
      });
      setRenamePreview(response.data || []);
    } catch (error) {
      console.error('Failed to load rename preview:', error);
      toast.error('Failed to load rename preview');
      setShowRenamePreview(false);
    } finally {
      setIsLoadingPreview(false);
    }
  };

  // Execute rename for this league
  const handleRename = async () => {
    setIsRenaming(true);
    try {
      const response = await apiClient.post('/leagues/rename', {
        leagueIds: [leagueId]
      });
      const { totalRenamed } = response.data;

      toast.success('Files Renamed Successfully', {
        description: `${totalRenamed} file(s) have been renamed according to your naming scheme.`,
      });
      setShowRenamePreview(false);
      setRenamePreview([]);

      // Refetch files list
      if (season) {
        await queryClient.refetchQueries({ queryKey: ['league-season-files', leagueId, season] });
      } else {
        await queryClient.refetchQueries({ queryKey: ['league-files', leagueId] });
      }
      // Also refresh league events and league data
      await queryClient.refetchQueries({ queryKey: ['league-events', leagueId.toString()] });
      await queryClient.refetchQueries({ queryKey: ['league', leagueId.toString()] });
      await queryClient.refetchQueries({ queryKey: ['leagues'] });
    } catch (error) {
      console.error('Failed to rename files:', error);
      toast.error('Failed to rename files');
    } finally {
      setIsRenaming(false);
    }
  };

  // Group files by season (only relevant when viewing all files)
  // Uses displayFiles which excludes deleted files for immediate UI update
  const filesBySeason = displayFiles.reduce((acc, file) => {
    const seasonKey = file.season || 'Unknown';
    if (!acc[seasonKey]) {
      acc[seasonKey] = [];
    }
    acc[seasonKey].push(file);
    return acc;
  }, {} as Record<string, LeagueFile[]>);

  // Sort seasons (most recent first)
  const sortedSeasons = Object.keys(filesBySeason).sort((a, b) => {
    // Try to parse as year or use string comparison
    const yearA = parseInt(a.replace(/\D/g, '')) || 0;
    const yearB = parseInt(b.replace(/\D/g, '')) || 0;
    return yearB - yearA;
  });

  const toggleSeason = (seasonKey: string) => {
    setExpandedSeasons(prev => {
      const newSet = new Set(prev);
      if (newSet.has(seasonKey)) {
        newSet.delete(seasonKey);
      } else {
        newSet.add(seasonKey);
      }
      return newSet;
    });
  };

  const title = season ? `${leagueName} - ${season} Files` : `${leagueName} - All Files`;

  return (
    <Transition
      appear
      show={isOpen}
      as={Fragment}
      unmount={true}
      afterLeave={() => {
        document.querySelectorAll('[inert]').forEach((el) => {
          el.removeAttribute('inert');
        });
        setExpandedSeasons(new Set());
      }}
    >
      <Dialog as="div" className="relative z-50" onClose={onClose}>
        <Transition.Child
          as={Fragment}
          enter="ease-out duration-300"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-200"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black/80" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-y-auto">
          <div className="flex min-h-full items-center justify-center p-4 text-center">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-4xl transform overflow-hidden rounded-lg bg-gray-900 text-left align-middle shadow-xl transition-all border border-gray-700">
                {/* Header */}
                <div className="flex items-center justify-between p-4 border-b border-gray-700">
                  <div>
                    <Dialog.Title className="text-lg font-medium text-white">
                      {title}
                    </Dialog.Title>
                    {data && (
                      <p className="text-sm text-gray-400 mt-1">
                        {displayFiles.length} file{displayFiles.length !== 1 ? 's' : ''} • {formatFileSize(displayFiles.reduce((sum, f) => sum + f.size, 0))}
                      </p>
                    )}
                  </div>
                  <button
                    onClick={onClose}
                    className="text-gray-400 hover:text-white transition-colors"
                  >
                    <XMarkIcon className="w-6 h-6" />
                  </button>
                </div>

                {/* Content */}
                <div className="max-h-[70vh] overflow-y-auto">
                  {showRenamePreview ? (
                    // Rename Preview View
                    <div className="p-4">
                      <p className="text-gray-400 mb-4">
                        The following files will be renamed according to your naming settings:
                      </p>

                      {isLoadingPreview ? (
                        <div className="flex items-center justify-center h-32">
                          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-red-600"></div>
                        </div>
                      ) : renamePreview.length > 0 ? (
                        <div className="space-y-2">
                          <div className="bg-blue-900/20 border border-blue-600/50 rounded-lg p-3">
                            <p className="text-blue-400 text-sm">
                              <strong>{renamePreview.length}</strong> file{renamePreview.length !== 1 ? 's' : ''} will be renamed
                            </p>
                          </div>
                          <div className="space-y-2 max-h-60 overflow-y-auto">
                            {renamePreview.map((preview, index) => (
                              <div key={index} className="bg-gray-800/50 rounded-lg p-3 border border-red-900/20">
                                <div className="space-y-1">
                                  <div>
                                    <p className="text-gray-400 text-xs">Current:</p>
                                    <p className="text-gray-300 font-mono text-xs break-all">{preview.existingPath}</p>
                                  </div>
                                  <div>
                                    <p className="text-gray-400 text-xs">New:</p>
                                    <p className="text-green-400 font-mono text-xs break-all">{preview.newPath}</p>
                                  </div>
                                </div>
                              </div>
                            ))}
                          </div>
                        </div>
                      ) : (
                        <div className="bg-gray-800/50 rounded-lg p-6 text-center">
                          <ArrowPathIcon className="w-12 h-12 text-gray-600 mx-auto mb-3" />
                          <p className="text-gray-400">No files need renaming</p>
                          <p className="text-gray-500 text-sm mt-1">
                            All files are already using the correct naming format
                          </p>
                        </div>
                      )}
                    </div>
                  ) : isLoading ? (
                    <div className="flex items-center justify-center py-12">
                      <div className="animate-spin rounded-full h-8 w-8 border-t-2 border-b-2 border-red-500"></div>
                    </div>
                  ) : displayFiles.length === 0 ? (
                    <div className="text-center py-12 text-gray-400">
                      <FolderIcon className="w-12 h-12 mx-auto mb-4 opacity-50" />
                      <p>No files found</p>
                    </div>
                  ) : season ? (
                    // Single season view - flat list
                    <div className="p-4 space-y-2">
                      {displayFiles.map((file) => (
                        <FileRow
                          key={file.id}
                          file={file}
                          onDelete={() => deleteFileMutation.mutate({ eventId: file.eventId, fileId: file.id })}
                          isDeleting={deleteFileMutation.isPending}
                          showEventInfo
                        />
                      ))}
                    </div>
                  ) : (
                    // All seasons view - grouped by season
                    <div className="divide-y divide-gray-700">
                      {sortedSeasons.map((seasonKey) => {
                        const seasonFiles = filesBySeason[seasonKey];
                        const seasonSize = seasonFiles.reduce((sum, f) => sum + f.size, 0);
                        const isExpanded = expandedSeasons.has(seasonKey);

                        return (
                          <div key={seasonKey}>
                            <button
                              onClick={() => toggleSeason(seasonKey)}
                              className="w-full flex items-center justify-between p-4 hover:bg-gray-800/50 transition-colors"
                            >
                              <div className="flex items-center gap-3">
                                {isExpanded ? (
                                  <ChevronDownIcon className="w-5 h-5 text-gray-400" />
                                ) : (
                                  <ChevronRightIcon className="w-5 h-5 text-gray-400" />
                                )}
                                <span className="text-white font-medium">{seasonKey}</span>
                                <span className="text-sm text-gray-400">
                                  {seasonFiles.length} file{seasonFiles.length !== 1 ? 's' : ''}
                                </span>
                              </div>
                              <span className="text-sm text-gray-400">{formatFileSize(seasonSize)}</span>
                            </button>

                            {isExpanded && (
                              <div className="px-4 pb-4 space-y-2">
                                {seasonFiles.map((file) => (
                                  <FileRow
                                    key={file.id}
                                    file={file}
                                    onDelete={() => deleteFileMutation.mutate({ eventId: file.eventId, fileId: file.id })}
                                    isDeleting={deleteFileMutation.isPending}
                                    showEventInfo
                                  />
                                ))}
                              </div>
                            )}
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>

                {/* Footer */}
                <div className="flex justify-between gap-3 p-4 border-t border-gray-700 bg-gray-800/50">
                  <div>
                    {!showRenamePreview && displayFiles.length > 0 && (
                      <button
                        onClick={loadRenamePreview}
                        disabled={isLoadingPreview}
                        className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded transition-colors flex items-center gap-2 disabled:opacity-50"
                      >
                        <ArrowPathIcon className="w-4 h-4" />
                        Rename Files
                      </button>
                    )}
                  </div>
                  <div className="flex gap-3">
                    {showRenamePreview ? (
                      <>
                        <button
                          onClick={() => {
                            setShowRenamePreview(false);
                            setRenamePreview([]);
                          }}
                          disabled={isRenaming}
                          className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors disabled:opacity-50"
                        >
                          Back
                        </button>
                        {renamePreview.length > 0 && (
                          <button
                            onClick={handleRename}
                            disabled={isRenaming}
                            className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded transition-colors flex items-center gap-2 disabled:opacity-50"
                          >
                            {isRenaming ? (
                              <>
                                <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                                Renaming...
                              </>
                            ) : (
                              <>
                                <ArrowPathIcon className="w-4 h-4" />
                                Confirm Rename
                              </>
                            )}
                          </button>
                        )}
                      </>
                    ) : (
                      <button
                        onClick={onClose}
                        className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors"
                      >
                        Close
                      </button>
                    )}
                  </div>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}

// File row component
function FileRow({
  file,
  onDelete,
  isDeleting,
  showEventInfo,
}: {
  file: LeagueFile;
  onDelete: () => void;
  isDeleting: boolean;
  showEventInfo?: boolean;
}) {
  return (
    <div className="bg-gray-800 rounded-lg p-3 border border-gray-700">
      <div className="flex items-start justify-between gap-4">
        <div className="flex-1 min-w-0">
          {/* Event Info */}
          {showEventInfo && (
            <div className="flex items-center gap-2 mb-1">
              <span className="text-sm font-medium text-white truncate">{file.eventTitle}</span>
              <span className="text-xs text-gray-500">{formatDate(file.eventDate)}</span>
            </div>
          )}

          {/* Part Name */}
          {file.partName && (
            <div className="text-xs font-medium text-red-400 mb-1">
              {file.partName}
            </div>
          )}

          {/* File Name */}
          <div className="text-sm text-gray-300 font-mono truncate" title={file.filePath}>
            <FilmIcon className="w-4 h-4 inline mr-1.5 text-gray-500" />
            {file.fileName}
          </div>

          {/* File Details */}
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 mt-1.5 text-xs text-gray-400">
            <span>{formatFileSize(file.size)}</span>
            {file.quality && (
              <span className="px-1.5 py-0.5 bg-blue-600/20 text-blue-400 rounded">
                {file.quality}
              </span>
            )}
            {file.customFormatScore !== undefined && file.customFormatScore !== 0 && (
              <span className="px-1.5 py-0.5 bg-purple-600/20 text-purple-400 rounded" title="Custom Format Score - Higher is better">
                CF Score: {file.customFormatScore}
              </span>
            )}
          </div>
        </div>

        {/* Delete Button */}
        <button
          onClick={() => {
            if (confirm(`Delete "${file.fileName}"? This action cannot be undone.`)) {
              onDelete();
            }
          }}
          disabled={isDeleting}
          className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-600/10 rounded transition-colors disabled:opacity-50 flex-shrink-0"
          title="Delete file"
        >
          <TrashIcon className="w-5 h-5" />
        </button>
      </div>
    </div>
  );
}
