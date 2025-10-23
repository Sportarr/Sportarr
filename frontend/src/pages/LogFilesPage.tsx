import { useState } from 'react';
import { useLogFiles, useLogFileContent } from '../api/hooks';
import { ArrowDownTrayIcon, DocumentTextIcon, XMarkIcon } from '@heroicons/react/24/outline';

export default function LogFilesPage() {
  const { data: logFiles, isLoading, error } = useLogFiles();
  const [selectedFile, setSelectedFile] = useState<string | null>(null);
  const { data: logContent, isLoading: isLoadingContent } = useLogFileContent(selectedFile);

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(2)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleString();
  };

  const handleDownload = (filename: string) => {
    const urlBase = (window as any).Fightarr?.urlBase || '';
    window.open(`${urlBase}/api/log/file/${filename}/download`, '_blank');
  };

  if (isLoading) {
    return (
      <div className="p-8">
        <div className="flex items-center justify-center h-64">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-8">
        <div className="bg-red-900 border border-red-700 text-red-100 px-4 py-3 rounded">
          <p className="font-bold">Error loading log files</p>
          <p className="text-sm">{(error as Error).message}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="p-8">
      <div className="max-w-7xl mx-auto">
        <div className="mb-8">
          <h1 className="text-3xl font-bold text-white mb-2">Log Files</h1>
          <p className="text-gray-400">View and download application log files</p>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* Log Files List */}
          <div className="lg:col-span-1">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg shadow-xl overflow-hidden">
              <div className="px-6 py-4 bg-red-950/30 border-b border-red-900/30">
                <h2 className="text-xl font-semibold text-white">Available Logs</h2>
              </div>
              <div className="divide-y divide-red-900/20 max-h-[600px] overflow-y-auto">
                {logFiles && logFiles.length > 0 ? (
                  logFiles.map((file) => (
                    <div
                      key={file.filename}
                      className={`px-6 py-4 hover:bg-red-900/10 transition-colors cursor-pointer ${
                        selectedFile === file.filename ? 'bg-red-900/20' : ''
                      }`}
                      onClick={() => setSelectedFile(file.filename)}
                    >
                      <div className="flex items-start justify-between">
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2">
                            <DocumentTextIcon className="w-5 h-5 text-red-500 flex-shrink-0" />
                            <p className="text-white font-medium truncate">{file.filename}</p>
                          </div>
                          <p className="text-sm text-gray-400 mt-1">
                            {formatFileSize(file.size)}
                          </p>
                          <p className="text-xs text-gray-500 mt-1">
                            {formatDate(file.lastWriteTime)}
                          </p>
                        </div>
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            handleDownload(file.filename);
                          }}
                          className="ml-3 p-2 text-gray-400 hover:text-white hover:bg-red-900/20 rounded transition-colors"
                          title="Download"
                        >
                          <ArrowDownTrayIcon className="w-5 h-5" />
                        </button>
                      </div>
                    </div>
                  ))
                ) : (
                  <div className="px-6 py-8 text-center text-gray-400">
                    No log files available
                  </div>
                )}
              </div>
            </div>
          </div>

          {/* Log Content Viewer */}
          <div className="lg:col-span-2">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg shadow-xl overflow-hidden h-[600px] flex flex-col">
              <div className="px-6 py-4 bg-red-950/30 border-b border-red-900/30 flex items-center justify-between">
                <h2 className="text-xl font-semibold text-white">
                  {selectedFile ? selectedFile : 'Select a log file'}
                </h2>
                {selectedFile && (
                  <button
                    onClick={() => setSelectedFile(null)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-red-900/20 rounded transition-colors"
                    title="Close"
                  >
                    <XMarkIcon className="w-5 h-5" />
                  </button>
                )}
              </div>
              <div className="flex-1 overflow-auto p-6">
                {!selectedFile ? (
                  <div className="flex items-center justify-center h-full text-gray-400">
                    <p>Select a log file from the list to view its contents</p>
                  </div>
                ) : isLoadingContent ? (
                  <div className="flex items-center justify-center h-full">
                    <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
                  </div>
                ) : logContent ? (
                  <div className="font-mono text-xs text-gray-300 whitespace-pre-wrap break-all bg-black/50 p-4 rounded border border-red-900/20">
                    {logContent.content}
                  </div>
                ) : (
                  <div className="flex items-center justify-center h-full text-red-400">
                    <p>Error loading log file content</p>
                  </div>
                )}
              </div>
              {logContent && (
                <div className="px-6 py-3 bg-red-950/20 border-t border-red-900/30 text-sm text-gray-400 flex items-center justify-between">
                  <span>Size: {formatFileSize(logContent.size)}</span>
                  <span>Last updated: {formatDate(logContent.lastWriteTime)}</span>
                  <span className="text-green-400">Auto-refreshing</span>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
