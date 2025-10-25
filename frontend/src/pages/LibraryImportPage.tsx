import React, { useState } from 'react';
import { FolderIcon, MagnifyingGlassIcon, CheckCircleIcon, XCircleIcon, ExclamationCircleIcon } from '@heroicons/react/24/outline';

interface ImportableFile {
  filePath: string;
  fileName: string;
  fileSize: number;
  fileSizeFormatted: string;
  parsedTitle?: string;
  parsedOrganization?: string;
  parsedDate?: string;
  quality?: string;
  matchedEventId?: number;
  matchedEventTitle?: string;
  existingEventId?: number;
}

interface ScanResult {
  folderPath: string;
  scannedAt: string;
  totalFiles: number;
  matchedFiles: ImportableFile[];
  unmatchedFiles: ImportableFile[];
  alreadyInLibrary: ImportableFile[];
  errors: string[];
}

interface FileImportRequest {
  filePath: string;
  eventId?: number;
  createNew: boolean;
  eventTitle?: string;
  organization?: string;
  eventDate?: string;
  quality?: string;
}

interface ImportResult {
  imported: string[];
  created: string[];
  skipped: string[];
  failed: string[];
  errors: string[];
}

const LibraryImportPage: React.FC = () => {
  const [folderPath, setFolderPath] = useState('');
  const [includeSubfolders, setIncludeSubfolders] = useState(true);
  const [scanning, setScanning] = useState(false);
  const [importing, setImporting] = useState(false);
  const [scanResult, setScanResult] = useState<ScanResult | null>(null);
  const [importResult, setImportResult] = useState<ImportResult | null>(null);
  const [selectedFiles, setSelectedFiles] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);

  const handleScan = async () => {
    if (!folderPath.trim()) {
      setError('Please enter a folder path');
      return;
    }

    setScanning(true);
    setError(null);
    setScanResult(null);
    setImportResult(null);
    setSelectedFiles(new Set());

    try {
      const response = await fetch(
        `/api/library/scan?folderPath=${encodeURIComponent(folderPath)}&includeSubfolders=${includeSubfolders}`,
        { method: 'POST' }
      );

      if (!response.ok) {
        throw new Error('Failed to scan folder');
      }

      const result: ScanResult = await response.json();
      setScanResult(result);

      // Auto-select all matched files
      const autoSelected = new Set(result.matchedFiles.map(f => f.filePath));
      setSelectedFiles(autoSelected);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'An error occurred');
    } finally {
      setScanning(false);
    }
  };

  const handleImport = async () => {
    if (!scanResult || selectedFiles.size === 0) {
      setError('No files selected for import');
      return;
    }

    setImporting(true);
    setError(null);

    try {
      const requests: FileImportRequest[] = Array.from(selectedFiles).map(filePath => {
        const file = [...scanResult.matchedFiles, ...scanResult.unmatchedFiles]
          .find(f => f.filePath === filePath);

        if (!file) {
          throw new Error(`File not found: ${filePath}`);
        }

        // If matched to existing event, import to that event
        if (file.matchedEventId) {
          return {
            filePath: file.filePath,
            eventId: file.matchedEventId,
            createNew: false
          };
        }

        // If unmatched, create new event
        return {
          filePath: file.filePath,
          createNew: true,
          eventTitle: file.parsedTitle,
          organization: file.parsedOrganization,
          eventDate: file.parsedDate,
          quality: file.quality
        };
      });

      const response = await fetch('/api/library/import', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requests)
      });

      if (!response.ok) {
        throw new Error('Failed to import files');
      }

      const result: ImportResult = await response.json();
      setImportResult(result);
      setSelectedFiles(new Set());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'An error occurred');
    } finally {
      setImporting(false);
    }
  };

  const toggleFileSelection = (filePath: string) => {
    const newSelected = new Set(selectedFiles);
    if (newSelected.has(filePath)) {
      newSelected.delete(filePath);
    } else {
      newSelected.add(filePath);
    }
    setSelectedFiles(newSelected);
  };

  const selectAllMatched = () => {
    if (!scanResult) return;
    setSelectedFiles(new Set(scanResult.matchedFiles.map(f => f.filePath)));
  };

  const selectAllUnmatched = () => {
    if (!scanResult) return;
    setSelectedFiles(new Set(scanResult.unmatchedFiles.map(f => f.filePath)));
  };

  const selectAll = () => {
    if (!scanResult) return;
    const all = [...scanResult.matchedFiles, ...scanResult.unmatchedFiles].map(f => f.filePath);
    setSelectedFiles(new Set(all));
  };

  const clearSelection = () => {
    setSelectedFiles(new Set());
  };

  const renderFileCard = (file: ImportableFile, type: 'matched' | 'unmatched' | 'existing') => {
    const isSelected = selectedFiles.has(file.filePath);
    const isExisting = type === 'existing';

    return (
      <div
        key={file.filePath}
        className={`bg-gray-800 rounded-lg p-4 border transition-colors ${
          isExisting
            ? 'border-gray-700 opacity-60'
            : isSelected
            ? 'border-blue-500'
            : 'border-gray-700 hover:border-gray-600'
        }`}
      >
        <div className="flex items-start gap-3">
          {!isExisting && (
            <input
              type="checkbox"
              checked={isSelected}
              onChange={() => toggleFileSelection(file.filePath)}
              className="mt-1 w-4 h-4 rounded border-gray-600 bg-gray-700 text-blue-600 focus:ring-blue-500"
            />
          )}

          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-2">
              <h4 className="font-medium text-white truncate">{file.fileName}</h4>
              {type === 'matched' && (
                <CheckCircleIcon className="w-5 h-5 text-green-400 flex-shrink-0" />
              )}
              {type === 'unmatched' && (
                <ExclamationCircleIcon className="w-5 h-5 text-yellow-400 flex-shrink-0" />
              )}
              {type === 'existing' && (
                <XCircleIcon className="w-5 h-5 text-gray-500 flex-shrink-0" />
              )}
            </div>

            <div className="text-sm text-gray-400 space-y-1">
              <p className="truncate">Path: {file.filePath}</p>
              <p>Size: {file.fileSizeFormatted}</p>
              {file.parsedTitle && <p>Parsed Title: {file.parsedTitle}</p>}
              {file.parsedOrganization && <p>Organization: {file.parsedOrganization}</p>}
              {file.quality && <p>Quality: {file.quality}</p>}
              {file.matchedEventTitle && (
                <p className="text-green-400">Will import to: {file.matchedEventTitle}</p>
              )}
              {type === 'unmatched' && (
                <p className="text-yellow-400">Will create new event</p>
              )}
              {type === 'existing' && (
                <p className="text-gray-500">Already in library (Event ID: {file.existingEventId})</p>
              )}
            </div>
          </div>
        </div>
      </div>
    );
  };

  return (
    <div className="p-6">
      <div className="mb-6">
        <h1 className="text-3xl font-bold text-white mb-2">Library Import</h1>
        <p className="text-gray-400">
          Scan your file system for existing event videos and import them into Fightarr
        </p>
      </div>

      {/* Info Box */}
      <div className="mb-6 p-4 bg-blue-900/20 border border-blue-800 rounded-lg">
        <div className="flex items-start gap-3">
          <FolderIcon className="w-5 h-5 text-blue-400 flex-shrink-0 mt-0.5" />
          <div className="text-sm text-gray-300">
            <strong className="text-white">How it works:</strong> Enter a folder path to scan for video files.
            Fightarr will attempt to match files to existing events based on title. Matched files can be imported to existing events,
            and unmatched files can be imported as new events.
          </div>
        </div>
      </div>

      {/* Scan Form */}
      <div className="mb-6 bg-gray-800 rounded-lg p-6 border border-gray-700">
        <h2 className="text-xl font-semibold text-white mb-4">Scan Folder</h2>

        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">
              Folder Path
            </label>
            <input
              type="text"
              value={folderPath}
              onChange={(e) => setFolderPath(e.target.value)}
              placeholder="/path/to/events"
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          </div>

          <div className="flex items-center">
            <input
              type="checkbox"
              id="includeSubfolders"
              checked={includeSubfolders}
              onChange={(e) => setIncludeSubfolders(e.target.checked)}
              className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-blue-600 focus:ring-blue-500"
            />
            <label htmlFor="includeSubfolders" className="ml-2 text-sm text-gray-300">
              Include subfolders
            </label>
          </div>

          <button
            onClick={handleScan}
            disabled={scanning || !folderPath.trim()}
            className="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
          >
            <MagnifyingGlassIcon className="w-5 h-5" />
            {scanning ? 'Scanning...' : 'Scan Folder'}
          </button>
        </div>
      </div>

      {/* Error */}
      {error && (
        <div className="mb-6 bg-red-900/20 border border-red-800 rounded-lg p-4">
          <p className="text-red-400">Error: {error}</p>
        </div>
      )}

      {/* Import Result */}
      {importResult && (
        <div className="mb-6 bg-gray-800 rounded-lg p-6 border border-gray-700">
          <h2 className="text-xl font-semibold text-white mb-4">Import Complete</h2>
          <div className="space-y-2 text-sm">
            {importResult.imported.length > 0 && (
              <p className="text-green-400">Imported {importResult.imported.length} files to existing events</p>
            )}
            {importResult.created.length > 0 && (
              <p className="text-blue-400">Created {importResult.created.length} new events</p>
            )}
            {importResult.skipped.length > 0 && (
              <p className="text-gray-400">Skipped {importResult.skipped.length} files</p>
            )}
            {importResult.failed.length > 0 && (
              <p className="text-red-400">Failed to import {importResult.failed.length} files</p>
            )}
            {importResult.errors.length > 0 && (
              <div className="mt-4 space-y-1">
                <p className="text-red-400 font-medium">Errors:</p>
                {importResult.errors.map((err, i) => (
                  <p key={i} className="text-red-400 text-xs">{err}</p>
                ))}
              </div>
            )}
          </div>
        </div>
      )}

      {/* Scan Results */}
      {scanResult && (
        <div className="space-y-6">
          {/* Summary */}
          <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
            <h2 className="text-xl font-semibold text-white mb-4">Scan Results</h2>
            <div className="grid grid-cols-4 gap-4 text-center">
              <div>
                <p className="text-2xl font-bold text-white">{scanResult.totalFiles}</p>
                <p className="text-sm text-gray-400">Total Files</p>
              </div>
              <div>
                <p className="text-2xl font-bold text-green-400">{scanResult.matchedFiles.length}</p>
                <p className="text-sm text-gray-400">Matched</p>
              </div>
              <div>
                <p className="text-2xl font-bold text-yellow-400">{scanResult.unmatchedFiles.length}</p>
                <p className="text-sm text-gray-400">Unmatched</p>
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-400">{scanResult.alreadyInLibrary.length}</p>
                <p className="text-sm text-gray-400">Already in Library</p>
              </div>
            </div>

            {scanResult.errors.length > 0 && (
              <div className="mt-4 p-3 bg-red-900/20 border border-red-800 rounded">
                <p className="text-red-400 font-medium mb-2">Scan Errors:</p>
                {scanResult.errors.map((err, i) => (
                  <p key={i} className="text-red-400 text-xs">{err}</p>
                ))}
              </div>
            )}

            {/* Selection Controls */}
            {(scanResult.matchedFiles.length > 0 || scanResult.unmatchedFiles.length > 0) && (
              <div className="mt-4 flex items-center gap-2 flex-wrap">
                <button
                  onClick={selectAll}
                  className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm"
                >
                  Select All
                </button>
                <button
                  onClick={selectAllMatched}
                  className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm"
                >
                  Select Matched
                </button>
                <button
                  onClick={selectAllUnmatched}
                  className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm"
                >
                  Select Unmatched
                </button>
                <button
                  onClick={clearSelection}
                  className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm"
                >
                  Clear Selection
                </button>
                <div className="flex-1"></div>
                <span className="text-sm text-gray-400">
                  {selectedFiles.size} file(s) selected
                </span>
                <button
                  onClick={handleImport}
                  disabled={importing || selectedFiles.size === 0}
                  className="px-4 py-2 bg-green-600 text-white rounded hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {importing ? 'Importing...' : `Import Selected (${selectedFiles.size})`}
                </button>
              </div>
            )}
          </div>

          {/* Matched Files */}
          {scanResult.matchedFiles.length > 0 && (
            <div>
              <h3 className="text-lg font-semibold text-white mb-3 flex items-center gap-2">
                <CheckCircleIcon className="w-6 h-6 text-green-400" />
                Matched Files ({scanResult.matchedFiles.length})
              </h3>
              <div className="space-y-3">
                {scanResult.matchedFiles.map(file => renderFileCard(file, 'matched'))}
              </div>
            </div>
          )}

          {/* Unmatched Files */}
          {scanResult.unmatchedFiles.length > 0 && (
            <div>
              <h3 className="text-lg font-semibold text-white mb-3 flex items-center gap-2">
                <ExclamationCircleIcon className="w-6 h-6 text-yellow-400" />
                Unmatched Files ({scanResult.unmatchedFiles.length})
              </h3>
              <p className="text-sm text-gray-400 mb-3">
                These files will be imported as new events
              </p>
              <div className="space-y-3">
                {scanResult.unmatchedFiles.map(file => renderFileCard(file, 'unmatched'))}
              </div>
            </div>
          )}

          {/* Already in Library */}
          {scanResult.alreadyInLibrary.length > 0 && (
            <div>
              <h3 className="text-lg font-semibold text-white mb-3 flex items-center gap-2">
                <XCircleIcon className="w-6 h-6 text-gray-500" />
                Already in Library ({scanResult.alreadyInLibrary.length})
              </h3>
              <div className="space-y-3">
                {scanResult.alreadyInLibrary.map(file => renderFileCard(file, 'existing'))}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default LibraryImportPage;
