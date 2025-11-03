import { Fragment, useState, useEffect } from 'react';
import { toast } from 'sonner';
import { Dialog, Transition } from '@headlessui/react';
import {
  XMarkIcon,
  DocumentTextIcon,
  CheckCircleIcon,
  ArrowPathIcon,
} from '@heroicons/react/24/outline';

interface PreviewRenameModalProps {
  isOpen: boolean;
  onClose: () => void;
  renameType: 'organization' | 'event' | 'fightcard';
  title: string;
  renameParams: {
    organizationName?: string;
    eventId?: number;
    fightCardId?: number;
  };
}

interface RenamePreview {
  existingPath: string;
  newPath: string;
  changes: {
    field: string;
    oldValue: string;
    newValue: string;
  }[];
}

export default function PreviewRenameModal({
  isOpen,
  onClose,
  renameType,
  title,
  renameParams,
}: PreviewRenameModalProps) {
  const [isLoading, setIsLoading] = useState(false);
  const [previews, setPreviews] = useState<RenamePreview[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isRenaming, setIsRenaming] = useState(false);

  useEffect(() => {
    if (isOpen) {
      fetchRenamePreview();
    }
  }, [isOpen]);

  const fetchRenamePreview = async () => {
    setIsLoading(true);
    setError(null);
    setPreviews([]);

    try {
      let endpoint = '';
      if (renameType === 'event' && renameParams.eventId) {
        endpoint = `/api/event/${renameParams.eventId}/rename-preview`;
      } else if (renameType === 'fightcard' && renameParams.fightCardId) {
        endpoint = `/api/fightcard/${renameParams.fightCardId}/rename-preview`;
      } else if (renameType === 'organization' && renameParams.organizationName) {
        endpoint = `/api/organization/${encodeURIComponent(renameParams.organizationName)}/rename-preview`;
      }

      const response = await fetch(endpoint);
      if (!response.ok) {
        throw new Error('Failed to fetch rename preview');
      }

      const data = await response.json();
      setPreviews(data || []);
    } catch (error) {
      console.error('Failed to fetch rename preview:', error);
      setError('Failed to load rename preview. Please try again.');
    } finally {
      setIsLoading(false);
    }
  };

  const handleRename = async () => {
    setIsRenaming(true);
    setError(null);

    try {
      let endpoint = '';
      if (renameType === 'event' && renameParams.eventId) {
        endpoint = `/api/event/${renameParams.eventId}/rename`;
      } else if (renameType === 'fightcard' && renameParams.fightCardId) {
        endpoint = `/api/fightcard/${renameParams.fightCardId}/rename`;
      } else if (renameType === 'organization' && renameParams.organizationName) {
        endpoint = `/api/organization/${encodeURIComponent(renameParams.organizationName)}/rename`;
      }

      const response = await fetch(endpoint, {
        method: 'POST',
      });

      if (!response.ok) {
        throw new Error('Failed to rename files');
      }

      toast.success('Files Renamed Successfully', {
        description: `${previews.length} file(s) have been renamed according to your naming scheme.`,
      });
      onClose();
    } catch (error) {
      console.error('Failed to rename files:', error);
      setError('Failed to rename files. Please try again.');
    } finally {
      setIsRenaming(false);
    }
  };

  const getRenameTitle = () => {
    switch (renameType) {
      case 'organization':
        return `Preview Rename: ${title}`;
      case 'event':
        return `Preview Rename: ${title}`;
      case 'fightcard':
        return `Preview Rename: ${title}`;
    }
  };

  return (
    <Transition appear show={isOpen} as={Fragment}>
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
          <div className="flex min-h-full items-center justify-center p-4">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-full max-w-4xl transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 shadow-2xl transition-all">
                {/* Header */}
                <div className="relative bg-gradient-to-r from-gray-900 via-red-950/20 to-gray-900 border-b border-red-900/30 p-6">
                  <div className="flex items-center justify-between">
                    <div>
                      <h2 className="text-2xl font-bold text-white mb-1">{getRenameTitle()}</h2>
                      <p className="text-gray-400 text-sm">
                        Preview file naming changes based on your naming template
                      </p>
                    </div>
                    <button
                      onClick={onClose}
                      className="p-2 rounded-lg bg-black/50 hover:bg-black/70 transition-colors"
                    >
                      <XMarkIcon className="w-6 h-6 text-white" />
                    </button>
                  </div>
                </div>

                {/* Content */}
                <div className="p-6 max-h-[70vh] overflow-y-auto">
                  <div className="space-y-4">
                    {/* Error Message */}
                    {error && (
                      <div className="bg-red-900/20 border border-red-600/50 rounded-lg p-4">
                        <p className="text-red-400 text-sm">{error}</p>
                      </div>
                    )}

                    {/* Loading State */}
                    {isLoading ? (
                      <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
                        <p className="text-gray-400">Loading rename preview...</p>
                      </div>
                    ) : previews.length > 0 ? (
                      <>
                        <div className="bg-blue-900/20 border border-blue-600/50 rounded-lg p-4 mb-4">
                          <p className="text-blue-400 text-sm">
                            <strong>{previews.length}</strong> file{previews.length !== 1 ? 's' : ''} will be renamed
                          </p>
                        </div>

                        <div className="space-y-3">
                          {previews.map((preview, index) => (
                            <div
                              key={index}
                              className="bg-gray-800/50 rounded-lg p-4 border border-red-900/20 hover:border-red-600/50 transition-colors"
                            >
                              <div className="space-y-3">
                                {/* File paths */}
                                <div className="space-y-2">
                                  <div>
                                    <p className="text-gray-400 text-xs mb-1">Current Path:</p>
                                    <p className="text-gray-300 font-mono text-sm break-all bg-gray-900/50 p-2 rounded">
                                      {preview.existingPath}
                                    </p>
                                  </div>
                                  <div>
                                    <p className="text-gray-400 text-xs mb-1">New Path:</p>
                                    <p className="text-green-400 font-mono text-sm break-all bg-gray-900/50 p-2 rounded">
                                      {preview.newPath}
                                    </p>
                                  </div>
                                </div>

                                {/* Changes breakdown */}
                                {preview.changes && preview.changes.length > 0 && (
                                  <div className="pt-3 border-t border-gray-700">
                                    <p className="text-gray-400 text-xs mb-2">Changes:</p>
                                    <div className="space-y-1">
                                      {preview.changes.map((change, changeIdx) => (
                                        <div
                                          key={changeIdx}
                                          className="flex items-start gap-2 text-xs bg-gray-900/30 p-2 rounded"
                                        >
                                          <span className="text-gray-400 min-w-[80px]">{change.field}:</span>
                                          <span className="text-red-400 line-through">{change.oldValue}</span>
                                          <span className="text-gray-500">→</span>
                                          <span className="text-green-400">{change.newValue}</span>
                                        </div>
                                      ))}
                                    </div>
                                  </div>
                                )}
                              </div>
                            </div>
                          ))}
                        </div>
                      </>
                    ) : (
                      <div className="bg-gray-800/50 rounded-lg p-8 border border-red-900/20 text-center">
                        <DocumentTextIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                        <p className="text-gray-400 mb-2">No files to rename</p>
                        <p className="text-gray-500 text-sm">
                          All files are already using the correct naming format
                        </p>
                      </div>
                    )}
                  </div>
                </div>

                {/* Footer */}
                <div className="px-6 py-4 bg-gray-900/50 border-t border-red-900/30 flex justify-between items-center">
                  <div className="flex items-center gap-2 text-sm text-gray-400">
                    <CheckCircleIcon className="w-5 h-5" />
                    <span>Preview only - no changes made yet</span>
                  </div>
                  <div className="flex gap-2">
                    <button
                      onClick={onClose}
                      className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                    >
                      Cancel
                    </button>
                    {previews.length > 0 && (
                      <button
                        onClick={handleRename}
                        disabled={isRenaming}
                        className="px-4 py-2 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 text-white rounded-lg transition-colors flex items-center gap-2"
                      >
                        {isRenaming ? (
                          <>
                            <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                            <span>Renaming...</span>
                          </>
                        ) : (
                          <>
                            <ArrowPathIcon className="w-5 h-5" />
                            <span>Rename Files</span>
                          </>
                        )}
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
