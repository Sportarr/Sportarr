import { useState, useEffect } from 'react';
import { FolderIcon, ChevronRightIcon, HomeIcon, XMarkIcon, ServerIcon } from '@heroicons/react/24/outline';
import { apiGet } from '../utils/api';

interface FileBrowserModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSelect: (path: string) => void;
  title?: string;
}

interface FileSystemItem {
  type: 'drive' | 'folder' | 'file';
  name: string;
  path: string;
  freeSpace?: number;
  totalSpace?: number;
  lastModified?: string;
}

interface FileSystemResponse {
  parent: string | null;
  directories: FileSystemItem[];
  files?: FileSystemItem[];
}

export default function FileBrowserModal({ isOpen, onClose, onSelect, title = 'Select Folder' }: FileBrowserModalProps) {
  const [currentPath, setCurrentPath] = useState<string>('');
  const [items, setItems] = useState<FileSystemItem[]>([]);
  const [parent, setParent] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (isOpen) {
      loadDirectory('');
    }
  }, [isOpen]);

  const loadDirectory = async (path: string) => {
    setLoading(true);
    setError(null);

    try {
      const queryParam = path ? `?path=${encodeURIComponent(path)}` : '';
      const response = await apiGet(`/api/filesystem${queryParam}`);

      if (response.ok) {
        const data: FileSystemResponse = await response.json();
        setItems(data.directories);
        setParent(data.parent);
        setCurrentPath(path);
      } else {
        const errorData = await response.json();
        setError(errorData.error || 'Failed to load directory');
      }
    } catch (err) {
      setError('Failed to load directory');
      console.error('Failed to load directory:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleItemClick = (item: FileSystemItem) => {
    if (item.type === 'drive' || item.type === 'folder') {
      loadDirectory(item.path);
    }
  };

  const handleGoUp = () => {
    if (parent !== null) {
      loadDirectory(parent);
    }
  };

  const handleSelectCurrent = () => {
    if (currentPath) {
      onSelect(currentPath);
      onClose();
    }
  };

  const formatBytes = (bytes: number) => {
    const gb = bytes / (1024 * 1024 * 1024);
    return `${gb.toFixed(2)} GB`;
  };

  const getPathBreadcrumbs = () => {
    if (!currentPath) return [];

    const parts = currentPath.split(/[\\/]/).filter(Boolean);
    const breadcrumbs: { name: string; path: string }[] = [];

    let accumulatedPath = '';
    parts.forEach((part, index) => {
      if (index === 0) {
        // Drive letter on Windows or root on Unix
        accumulatedPath = part.includes(':') ? `${part}\\` : `/${part}`;
      } else {
        accumulatedPath += `${part}\\`;
      }

      breadcrumbs.push({
        name: part,
        path: accumulatedPath
      });
    });

    return breadcrumbs;
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg max-w-3xl w-full max-h-[80vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-800">
          <h3 className="text-2xl font-bold text-white">{title}</h3>
          <button
            onClick={onClose}
            className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
          >
            <XMarkIcon className="w-6 h-6" />
          </button>
        </div>

        {/* Breadcrumb */}
        <div className="px-6 py-3 border-b border-gray-800 bg-black/30">
          <div className="flex items-center space-x-2 text-sm">
            <button
              onClick={() => loadDirectory('')}
              className="text-gray-400 hover:text-white transition-colors"
            >
              <HomeIcon className="w-4 h-4" />
            </button>

            {getPathBreadcrumbs().map((crumb, index) => (
              <div key={index} className="flex items-center space-x-2">
                <ChevronRightIcon className="w-3 h-3 text-gray-600" />
                <button
                  onClick={() => loadDirectory(crumb.path)}
                  className="text-gray-400 hover:text-white transition-colors"
                >
                  {crumb.name}
                </button>
              </div>
            ))}
          </div>
        </div>

        {/* Current Path Display */}
        <div className="px-6 py-2 bg-gray-900/50 border-b border-gray-800">
          <p className="text-sm text-gray-400">
            Current: <span className="text-white">{currentPath || 'Root'}</span>
          </p>
        </div>

        {/* Directory List */}
        <div className="flex-1 overflow-y-auto p-6">
          {error && (
            <div className="mb-4 p-4 bg-red-950/30 border border-red-900/50 rounded-lg">
              <p className="text-red-400">{error}</p>
            </div>
          )}

          {loading ? (
            <div className="text-center py-12">
              <div className="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-red-600"></div>
              <p className="text-gray-400 mt-4">Loading...</p>
            </div>
          ) : (
            <div className="space-y-1">
              {/* Go Up Button */}
              {parent !== null && (
                <button
                  onClick={handleGoUp}
                  className="w-full flex items-center p-3 hover:bg-gray-800/50 rounded-lg transition-colors text-left"
                >
                  <FolderIcon className="w-5 h-5 text-yellow-500 mr-3" />
                  <span className="text-white">..</span>
                </button>
              )}

              {/* Items */}
              {items.map((item, index) => (
                <button
                  key={index}
                  onClick={() => handleItemClick(item)}
                  className="w-full flex items-center justify-between p-3 hover:bg-gray-800/50 rounded-lg transition-colors text-left"
                >
                  <div className="flex items-center flex-1">
                    {item.type === 'drive' ? (
                      <ServerIcon className="w-5 h-5 text-blue-500 mr-3" />
                    ) : (
                      <FolderIcon className="w-5 h-5 text-yellow-500 mr-3" />
                    )}
                    <span className="text-white">{item.name}</span>
                  </div>

                  {item.type === 'drive' && item.freeSpace && item.totalSpace && (
                    <span className="text-sm text-gray-400 ml-4">
                      {formatBytes(item.freeSpace)} free of {formatBytes(item.totalSpace)}
                    </span>
                  )}
                </button>
              ))}

              {items.length === 0 && !error && (
                <div className="text-center py-12">
                  <FolderIcon className="w-16 h-16 text-gray-700 mx-auto mb-4" />
                  <p className="text-gray-500">No folders found</p>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="p-6 border-t border-gray-800 flex items-center justify-between">
          <div className="text-sm text-gray-400">
            {currentPath ? (
              <>Selected: <span className="text-white">{currentPath}</span></>
            ) : (
              'Select a folder from the list'
            )}
          </div>

          <div className="flex items-center space-x-3">
            <button
              onClick={onClose}
              className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleSelectCurrent}
              disabled={!currentPath}
              className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Select Folder
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
