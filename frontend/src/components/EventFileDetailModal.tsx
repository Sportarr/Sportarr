import { Fragment, useState, useEffect } from 'react';
import { Dialog, Transition } from '@headlessui/react';
import { XMarkIcon, TrashIcon, FolderIcon, FilmIcon } from '@heroicons/react/24/outline';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../api/client';
import { toast } from 'sonner';

interface EventFile {
  id: number;
  eventId: number;
  filePath: string;
  size: number;
  quality?: string;
  qualityScore?: number;
  customFormatScore?: number;
  partName?: string;
  partNumber?: number;
  added: string;
  exists: boolean;
  originalTitle?: string;
}

interface EventFileDetailModalProps {
  isOpen: boolean;
  onClose: () => void;
  eventId: number;
  eventTitle: string;
  files: EventFile[];
  leagueId?: string;
  isFightingSport?: boolean;
}

type BlocklistAction = 'none' | 'blocklistAndSearch' | 'blocklistOnly';

interface DeleteFileDialog {
  file: EventFile;
  blocklistAction: BlocklistAction;
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
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit'
  });
}

export default function EventFileDetailModal({
  isOpen,
  onClose,
  eventId,
  eventTitle,
  files,
  leagueId,
  isFightingSport = false,
}: EventFileDetailModalProps) {
  const queryClient = useQueryClient();

  // Local state to track files - allows immediate UI updates on delete
  const [localFiles, setLocalFiles] = useState<EventFile[]>(files);

  // Delete dialog state
  const [deleteDialog, setDeleteDialog] = useState<DeleteFileDialog | null>(null);
  const [deleteAllBlocklistAction, setDeleteAllBlocklistAction] = useState<BlocklistAction>('none');
  const [showDeleteAllDialog, setShowDeleteAllDialog] = useState(false);

  // Sync local state with prop when modal opens or files prop changes
  useEffect(() => {
    setLocalFiles(files);
  }, [files]);

  // Delete single file mutation
  const deleteFileMutation = useMutation({
    mutationFn: async ({ fileId, blocklistAction }: { fileId: number; blocklistAction: BlocklistAction }) => {
      const response = await apiClient.delete(`/events/${eventId}/files/${fileId}`, {
        params: { blocklistAction }
      });
      return { data: response.data, fileId };
    },
    onSuccess: async ({ data, fileId }) => {
      const action = deleteDialog?.blocklistAction;
      let message = 'File deleted';
      if (action === 'blocklistAndSearch') {
        message = 'File deleted, release blocklisted, searching for replacement...';
      } else if (action === 'blocklistOnly') {
        message = 'File deleted and release blocklisted';
      }
      toast.success(message);
      setDeleteDialog(null);
      // Immediately remove from local state for instant UI update
      setLocalFiles(prev => prev.filter(f => f.id !== fileId));
      // Refetch events to update parent UI
      if (leagueId) {
        await queryClient.refetchQueries({ queryKey: ['league-events', leagueId] });
        await queryClient.refetchQueries({ queryKey: ['league', leagueId] });
      }
      await queryClient.refetchQueries({ queryKey: ['leagues'] });
      // Close modal if no files left
      if (!data.eventHasFiles) {
        onClose();
      }
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.detail || 'Failed to delete file');
    },
  });

  // Delete all files mutation
  const deleteAllFilesMutation = useMutation({
    mutationFn: async (blocklistAction: BlocklistAction) => {
      const response = await apiClient.delete(`/events/${eventId}/files`, {
        params: { blocklistAction }
      });
      return response.data;
    },
    onSuccess: async (data) => {
      let message = 'All files deleted';
      if (deleteAllBlocklistAction === 'blocklistAndSearch') {
        message = 'All files deleted, releases blocklisted, searching for replacements...';
      } else if (deleteAllBlocklistAction === 'blocklistOnly') {
        message = 'All files deleted and releases blocklisted';
      }
      toast.success(message);
      setShowDeleteAllDialog(false);
      // Clear local state
      setLocalFiles([]);
      // Refetch events to update parent UI
      if (leagueId) {
        await queryClient.refetchQueries({ queryKey: ['league-events', leagueId] });
        await queryClient.refetchQueries({ queryKey: ['league', leagueId] });
      }
      await queryClient.refetchQueries({ queryKey: ['leagues'] });
      onClose();
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.detail || 'Failed to delete files');
    },
  });

  const existingFiles = localFiles.filter(f => f.exists);
  const totalSize = existingFiles.reduce((sum, f) => sum + f.size, 0);

  const handleDeleteFile = () => {
    if (!deleteDialog) return;
    deleteFileMutation.mutate({
      fileId: deleteDialog.file.id,
      blocklistAction: deleteDialog.blocklistAction
    });
  };

  const handleDeleteAllFiles = () => {
    deleteAllFilesMutation.mutate(deleteAllBlocklistAction);
  };

  const openDeleteDialog = (file: EventFile) => {
    setDeleteDialog({
      file,
      blocklistAction: 'none'
    });
  };

  return (
    <>
    <Transition
      appear
      show={isOpen}
      as={Fragment}
      unmount={true}
      afterLeave={() => {
        document.querySelectorAll('[inert]').forEach((el) => {
          el.removeAttribute('inert');
        });
        // Reset dialog states when modal closes
        setDeleteDialog(null);
        setShowDeleteAllDialog(false);
        setDeleteAllBlocklistAction('none');
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
              <Dialog.Panel className="w-full max-w-2xl transform overflow-hidden rounded-lg bg-gray-900 text-left align-middle shadow-xl transition-all border border-gray-700">
                {/* Header */}
                <div className="flex items-center justify-between p-4 border-b border-gray-700">
                  <div>
                    <Dialog.Title className="text-lg font-medium text-white">
                      Event Files
                    </Dialog.Title>
                    <p className="text-sm text-gray-400 mt-1">{eventTitle}</p>
                  </div>
                  <button
                    onClick={onClose}
                    className="text-gray-400 hover:text-white transition-colors"
                  >
                    <XMarkIcon className="w-6 h-6" />
                  </button>
                </div>

                {/* Summary */}
                <div className="p-4 bg-gray-800/50 border-b border-gray-700">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-4">
                      <div className="flex items-center gap-2 text-gray-300">
                        <FilmIcon className="w-5 h-5" />
                        <span>{existingFiles.length} file{existingFiles.length !== 1 ? 's' : ''}</span>
                      </div>
                      <div className="flex items-center gap-2 text-gray-300">
                        <FolderIcon className="w-5 h-5" />
                        <span>{formatFileSize(totalSize)}</span>
                      </div>
                    </div>
                    {existingFiles.length > 1 && (
                      <button
                        onClick={() => {
                          setDeleteAllBlocklistAction('none');
                          setShowDeleteAllDialog(true);
                        }}
                        disabled={deleteAllFilesMutation.isPending || deleteFileMutation.isPending}
                        className="px-3 py-1.5 bg-red-600 hover:bg-red-700 disabled:bg-red-600/50 text-white text-sm font-medium rounded transition-colors flex items-center gap-2"
                      >
                        <TrashIcon className="w-4 h-4" />
                        Delete All
                      </button>
                    )}
                  </div>
                </div>

                {/* File List */}
                <div className="p-4 max-h-96 overflow-y-auto">
                  {existingFiles.length === 0 ? (
                    <div className="text-center py-8 text-gray-400">
                      No files found for this event
                    </div>
                  ) : (
                    <div className="space-y-3">
                      {existingFiles.map((file) => (
                        <div
                          key={file.id}
                          className="bg-gray-800 rounded-lg p-4 border border-gray-700"
                        >
                          <div className="flex items-start justify-between gap-4">
                            <div className="flex-1 min-w-0">
                              {/* Part Name (for fighting sports) */}
                              {isFightingSport && file.partName && (
                                <div className="text-sm font-medium text-red-400 mb-1">
                                  {file.partName}
                                </div>
                              )}

                              {/* File Path */}
                              <div className="text-sm text-gray-200 font-mono truncate" title={file.filePath}>
                                {file.filePath.split(/[/\\]/).pop()}
                              </div>

                              {/* File Details */}
                              <div className="flex flex-wrap items-center gap-x-4 gap-y-1 mt-2 text-xs text-gray-400">
                                <span>{formatFileSize(file.size)}</span>
                                {file.quality && (
                                  <span className="px-2 py-0.5 bg-blue-600/20 text-blue-400 rounded">
                                    {file.quality}
                                  </span>
                                )}
                                {file.customFormatScore !== undefined && file.customFormatScore !== 0 && (
                                  <span className="px-2 py-0.5 bg-purple-600/20 text-purple-400 rounded" title="Custom Format Score - Higher is better">
                                    CF Score: {file.customFormatScore}
                                  </span>
                                )}
                                <span>Added: {formatDate(file.added)}</span>
                              </div>

                              {/* Original Grabbed Title (shows the release name before renaming) */}
                              {file.originalTitle && (
                                <details className="mt-2">
                                  <summary className="text-xs text-gray-500 cursor-pointer hover:text-gray-400">
                                    Original grabbed title
                                  </summary>
                                  <div className="mt-1 p-2 bg-yellow-900/20 border border-yellow-600/30 rounded text-xs text-yellow-300 font-mono break-all">
                                    {file.originalTitle}
                                  </div>
                                </details>
                              )}

                              {/* Full Path (collapsed) */}
                              <details className="mt-2">
                                <summary className="text-xs text-gray-500 cursor-pointer hover:text-gray-400">
                                  Full path
                                </summary>
                                <div className="mt-1 p-2 bg-gray-900 rounded text-xs text-gray-400 font-mono break-all">
                                  {file.filePath}
                                </div>
                              </details>
                            </div>

                            {/* Delete Button */}
                            <button
                              onClick={() => openDeleteDialog(file)}
                              disabled={deleteFileMutation.isPending || deleteAllFilesMutation.isPending}
                              className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-600/10 rounded transition-colors disabled:opacity-50"
                              title="Delete file"
                            >
                              <TrashIcon className="w-5 h-5" />
                            </button>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                {/* Footer */}
                <div className="flex justify-end gap-3 p-4 border-t border-gray-700 bg-gray-800/50">
                  <button
                    onClick={onClose}
                    className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors"
                  >
                    Close
                  </button>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>

      </Dialog>
    </Transition>

      {/* Delete Single File Dialog - OUTSIDE the HeadlessUI Dialog/Transition to prevent event capture */}
      {deleteDialog && (
        <div
          className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-[60] p-4"
          onClick={(e) => {
            e.stopPropagation();
            // Only close if clicking the backdrop, not the inner content
            if (e.target === e.currentTarget) {
              setDeleteDialog(null);
            }
          }}
        >
          <div
            className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-lg w-full p-6"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-start justify-between mb-4">
              <h3 className="text-xl font-bold text-white">Delete File</h3>
              <button
                onClick={() => setDeleteDialog(null)}
                className="text-gray-400 hover:text-white transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <p className="text-gray-300 mb-2">
              Are you sure you want to delete this file?
            </p>
            {deleteDialog.file.partName && (
              <p className="text-red-400 text-sm mb-2">Part: {deleteDialog.file.partName}</p>
            )}
            <p className="text-gray-400 text-sm font-mono mb-6 break-all">
              {deleteDialog.file.filePath.split(/[/\\]/).pop()}
            </p>

            {/* Blocklist Options */}
            <div className="mb-6">
              <label className="block text-gray-300 font-medium mb-2">Blocklist Release</label>
              <select
                value={deleteDialog.blocklistAction}
                onChange={(e) => setDeleteDialog({
                  ...deleteDialog,
                  blocklistAction: e.target.value as BlocklistAction
                })}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-600 text-white rounded-lg focus:outline-none focus:ring-2 focus:ring-red-600"
              >
                <option value="none">Do not Blocklist</option>
                <option value="blocklistAndSearch">Blocklist and Search for Replacement</option>
                <option value="blocklistOnly">Blocklist Only</option>
              </select>
              <p className="text-sm text-gray-400 mt-2">
                {deleteDialog.blocklistAction === 'none' && 'The release will remain eligible for future searches'}
                {deleteDialog.blocklistAction === 'blocklistAndSearch' && 'Blocklist this release and automatically search for a replacement'}
                {deleteDialog.blocklistAction === 'blocklistOnly' && 'Blocklist this release without searching for a replacement'}
              </p>
            </div>

            <div className="flex justify-end gap-3">
              <button
                onClick={() => setDeleteDialog(null)}
                className="px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleDeleteFile}
                disabled={deleteFileMutation.isPending}
                className="px-6 py-2 bg-red-600 hover:bg-red-700 disabled:bg-red-600/50 text-white rounded-lg transition-colors"
              >
                {deleteFileMutation.isPending ? 'Deleting...' : 'Delete'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete All Files Dialog - OUTSIDE the HeadlessUI Dialog to prevent event capture */}
      {showDeleteAllDialog && (
        <div
          className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-[60] p-4"
          onClick={(e) => {
            e.stopPropagation();
            // Only close if clicking the backdrop, not the inner content
            if (e.target === e.currentTarget) {
              setShowDeleteAllDialog(false);
            }
          }}
        >
          <div
            className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-lg w-full p-6"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-start justify-between mb-4">
              <h3 className="text-xl font-bold text-white">Delete All Files</h3>
              <button
                onClick={() => setShowDeleteAllDialog(false)}
                className="text-gray-400 hover:text-white transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <p className="text-gray-300 mb-2">
              Are you sure you want to delete ALL {existingFiles.length} files for this event?
            </p>
            <p className="text-red-400 text-sm mb-6">
              This action cannot be undone.
            </p>

            {/* Blocklist Options */}
            <div className="mb-6">
              <label className="block text-gray-300 font-medium mb-2">Blocklist Releases</label>
              <select
                value={deleteAllBlocklistAction}
                onChange={(e) => setDeleteAllBlocklistAction(e.target.value as BlocklistAction)}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-600 text-white rounded-lg focus:outline-none focus:ring-2 focus:ring-red-600"
              >
                <option value="none">Do not Blocklist</option>
                <option value="blocklistAndSearch">Blocklist All and Search for Replacements</option>
                <option value="blocklistOnly">Blocklist All Only</option>
              </select>
              <p className="text-sm text-gray-400 mt-2">
                {deleteAllBlocklistAction === 'none' && 'Releases will remain eligible for future searches'}
                {deleteAllBlocklistAction === 'blocklistAndSearch' && 'Blocklist all releases and automatically search for replacements'}
                {deleteAllBlocklistAction === 'blocklistOnly' && 'Blocklist all releases without searching for replacements'}
              </p>
            </div>

            <div className="flex justify-end gap-3">
              <button
                onClick={() => setShowDeleteAllDialog(false)}
                className="px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleDeleteAllFiles}
                disabled={deleteAllFilesMutation.isPending}
                className="px-6 py-2 bg-red-600 hover:bg-red-700 disabled:bg-red-600/50 text-white rounded-lg transition-colors"
              >
                {deleteAllFilesMutation.isPending ? 'Deleting...' : 'Delete All'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
