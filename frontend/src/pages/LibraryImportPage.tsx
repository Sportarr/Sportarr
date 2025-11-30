import React, { useState, useCallback, useRef, useEffect } from 'react';
import { FolderIcon, MagnifyingGlassIcon, CheckCircleIcon, XCircleIcon, ExclamationCircleIcon, FolderOpenIcon, LinkIcon, ArrowPathIcon, SparklesIcon } from '@heroicons/react/24/outline';
import FileBrowserModal from '../components/FileBrowserModal';
import BulkImportWizard from '../components/BulkImportWizard';

interface ImportableFile {
  filePath: string;
  fileName: string;
  fileSize: number;
  fileSizeFormatted: string;
  parsedTitle?: string;
  parsedOrganization?: string;
  parsedSport?: string;
  parsedDate?: string;
  quality?: string;
  matchedEventId?: number;
  matchedEventTitle?: string;
  matchConfidence?: number;
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

interface EventSearchResult {
  id?: number;
  externalId?: string;
  title: string;
  sport: string;
  eventDate: string;
  venue?: string;
  leagueName?: string;
  homeTeam?: string;
  awayTeam?: string;
  existsInDatabase: boolean;
  hasFile: boolean;
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
  const [showFileBrowser, setShowFileBrowser] = useState(false);

  // TheSportsDB search modal state
  const [showSearchModal, setShowSearchModal] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<EventSearchResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [activeFile, setActiveFile] = useState<ImportableFile | null>(null);
  const [fileEventMappings, setFileEventMappings] = useState<Map<string, { eventId: number; eventTitle: string }>>(new Map());
  const searchTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Bulk import wizard state
  const [showBulkWizard, setShowBulkWizard] = useState(false);

  const handleScan = async () => {
    if (!folderPath.trim()) {
      setError('Please select a folder path');
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

  // TheSportsDB search for unmatched files
  const searchEvents = useCallback((query: string) => {
    // Clear existing timeout
    if (searchTimeoutRef.current) {
      clearTimeout(searchTimeoutRef.current);
    }

    if (!query.trim() || query.length < 3) {
      setSearchResults([]);
      return;
    }

    // Debounce the search
    searchTimeoutRef.current = setTimeout(async () => {
      setSearching(true);
      try {
        const response = await fetch(
          `/api/library/search?query=${encodeURIComponent(query)}`
        );
        if (response.ok) {
          const data = await response.json();
          setSearchResults(data.results || []);
        }
      } catch {
        console.error('Failed to search events');
      } finally {
        setSearching(false);
      }
    }, 300);
  }, []);

  // Cleanup search timeout on unmount
  useEffect(() => {
    return () => {
      if (searchTimeoutRef.current) {
        clearTimeout(searchTimeoutRef.current);
      }
    };
  }, []);

  const openSearchForFile = (file: ImportableFile) => {
    setActiveFile(file);
    setSearchQuery(file.parsedTitle || file.fileName);
    setShowSearchModal(true);
    // Trigger initial search
    if (file.parsedTitle || file.fileName) {
      searchEvents(file.parsedTitle || file.fileName);
    }
  };

  const selectEventForFile = (event: EventSearchResult) => {
    if (!activeFile || !event.id) return;

    // Update mappings
    const newMappings = new Map(fileEventMappings);
    newMappings.set(activeFile.filePath, { eventId: event.id, eventTitle: event.title });
    setFileEventMappings(newMappings);

    // Auto-select the file
    const newSelected = new Set(selectedFiles);
    newSelected.add(activeFile.filePath);
    setSelectedFiles(newSelected);

    setShowSearchModal(false);
    setActiveFile(null);
    setSearchQuery('');
    setSearchResults([]);
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

        // Check if user manually mapped this file to an event
        const manualMapping = fileEventMappings.get(filePath);
        if (manualMapping) {
          return {
            filePath: file.filePath,
            eventId: manualMapping.eventId,
            createNew: false
          };
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

  const getConfidenceBadge = (confidence?: number) => {
    if (!confidence) return null;

    let colorClass = 'bg-red-600';
    if (confidence >= 80) colorClass = 'bg-green-600';
    else if (confidence >= 60) colorClass = 'bg-yellow-600';
    else if (confidence >= 40) colorClass = 'bg-orange-600';

    return (
      <span className={`${colorClass} text-white text-xs px-2 py-0.5 rounded-full`}>
        {confidence}% match
      </span>
    );
  };

  const renderFileCard = (file: ImportableFile, type: 'matched' | 'unmatched' | 'existing') => {
    const isSelected = selectedFiles.has(file.filePath);
    const isExisting = type === 'existing';
    const manualMapping = fileEventMappings.get(file.filePath);
    const hasManualMapping = !!manualMapping;

    return (
      <div
        key={file.filePath}
        className={`bg-gray-800 rounded-lg p-4 border transition-colors ${
          isExisting
            ? 'border-gray-700 opacity-60'
            : hasManualMapping
            ? 'border-green-500'
            : isSelected
            ? 'border-red-500'
            : 'border-gray-700 hover:border-gray-600'
        }`}
      >
        <div className="flex items-start gap-3">
          {!isExisting && (
            <input
              type="checkbox"
              checked={isSelected}
              onChange={() => toggleFileSelection(file.filePath)}
              className="mt-1 w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
            />
          )}

          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-2 flex-wrap">
              <h4 className="font-medium text-white truncate">{file.fileName}</h4>
              {hasManualMapping && (
                <>
                  <LinkIcon className="w-5 h-5 text-green-400 flex-shrink-0" />
                  <span className="bg-green-600 text-white text-xs px-2 py-0.5 rounded-full">Manually Matched</span>
                </>
              )}
              {type === 'matched' && !hasManualMapping && (
                <>
                  <CheckCircleIcon className="w-5 h-5 text-green-400 flex-shrink-0" />
                  {getConfidenceBadge(file.matchConfidence)}
                </>
              )}
              {type === 'unmatched' && !hasManualMapping && (
                <ExclamationCircleIcon className="w-5 h-5 text-yellow-400 flex-shrink-0" />
              )}
              {type === 'existing' && (
                <XCircleIcon className="w-5 h-5 text-gray-500 flex-shrink-0" />
              )}
            </div>

            <div className="text-sm text-gray-400 space-y-1">
              <p className="truncate">Path: {file.filePath}</p>
              <p>Size: {file.fileSizeFormatted}</p>
              {file.parsedTitle && <p>Parsed Title: <span className="text-white">{file.parsedTitle}</span></p>}
              {file.parsedOrganization && <p>Organization: <span className="text-white">{file.parsedOrganization}</span></p>}
              {file.parsedSport && <p>Sport: <span className="text-white">{file.parsedSport}</span></p>}
              {file.quality && <p>Quality: <span className="text-blue-400">{file.quality}</span></p>}

              {/* Show manual mapping or auto-match */}
              {hasManualMapping ? (
                <p className="text-green-400">Will import to: <span className="font-medium">{manualMapping.eventTitle}</span></p>
              ) : file.matchedEventTitle ? (
                <p className="text-green-400">Will import to: <span className="font-medium">{file.matchedEventTitle}</span></p>
              ) : type === 'unmatched' ? (
                <p className="text-yellow-400">Will create new event</p>
              ) : null}

              {type === 'existing' && (
                <p className="text-gray-500">Already in library (Event ID: {file.existingEventId})</p>
              )}
            </div>

            {/* Search button for unmatched files */}
            {type === 'unmatched' && !isExisting && (
              <div className="mt-3">
                <button
                  onClick={() => openSearchForFile(file)}
                  className="px-3 py-1.5 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded flex items-center gap-2 transition-colors"
                >
                  <MagnifyingGlassIcon className="w-4 h-4" />
                  {hasManualMapping ? 'Change Match' : 'Search TheSportsDB'}
                </button>
              </div>
            )}
          </div>
        </div>
      </div>
    );
  };

  return (
    <div className="p-6">
      <div className="mb-6 flex items-start justify-between">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">Library Import</h1>
          <p className="text-gray-400">
            Scan your file system for existing event videos and import them into Sportarr
          </p>
        </div>
        <button
          onClick={() => setShowBulkWizard(true)}
          className="px-4 py-2 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white rounded-lg flex items-center gap-2 transition-all shadow-lg"
        >
          <SparklesIcon className="w-5 h-5" />
          Import Wizard
        </button>
      </div>

      {/* Info Box */}
      <div className="mb-6 p-4 bg-red-900/20 border border-red-800 rounded-lg">
        <div className="flex items-start gap-3">
          <FolderIcon className="w-5 h-5 text-red-400 flex-shrink-0 mt-0.5" />
          <div className="text-sm text-gray-300">
            <strong className="text-white">How it works:</strong> Browse to a folder containing video files.
            Sportarr will parse filenames using sports-specific patterns (UFC, WWE, NFL, NBA, etc.) and attempt to match
            files to existing events. Matched files can be imported to existing events, and unmatched files can be imported
            as new events.
          </div>
        </div>
      </div>

      {/* Scan Form */}
      <div className="mb-6 bg-gray-800 rounded-lg p-6 border border-red-900/30">
        <h2 className="text-xl font-semibold text-white mb-4">Select Folder to Scan</h2>

        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">
              Folder Path
            </label>
            <div className="flex gap-2">
              <input
                type="text"
                value={folderPath}
                onChange={(e) => setFolderPath(e.target.value)}
                placeholder="Click Browse to select a folder..."
                className="flex-1 px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-red-500"
                readOnly
              />
              <button
                onClick={() => setShowFileBrowser(true)}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded border border-gray-600 flex items-center gap-2 transition-colors"
              >
                <FolderOpenIcon className="w-5 h-5" />
                Browse
              </button>
            </div>
          </div>

          <div className="flex items-center">
            <input
              type="checkbox"
              id="includeSubfolders"
              checked={includeSubfolders}
              onChange={(e) => setIncludeSubfolders(e.target.checked)}
              className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
            />
            <label htmlFor="includeSubfolders" className="ml-2 text-sm text-gray-300">
              Include subfolders (recursive scan)
            </label>
          </div>

          <button
            onClick={handleScan}
            disabled={scanning || !folderPath.trim()}
            className="px-4 py-2 bg-red-600 text-white rounded hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2 transition-colors"
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
        <div className="mb-6 bg-gray-800 rounded-lg p-6 border border-green-800">
          <h2 className="text-xl font-semibold text-white mb-4">Import Complete</h2>
          <div className="space-y-2 text-sm">
            {importResult.imported.length > 0 && (
              <p className="text-green-400">✓ Imported {importResult.imported.length} files to existing events</p>
            )}
            {importResult.created.length > 0 && (
              <p className="text-blue-400">✓ Created {importResult.created.length} new events</p>
            )}
            {importResult.skipped.length > 0 && (
              <p className="text-gray-400">○ Skipped {importResult.skipped.length} files</p>
            )}
            {importResult.failed.length > 0 && (
              <p className="text-red-400">✗ Failed to import {importResult.failed.length} files</p>
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
          <div className="bg-gray-800 rounded-lg p-6 border border-red-900/30">
            <h2 className="text-xl font-semibold text-white mb-4">Scan Results</h2>
            <div className="grid grid-cols-4 gap-4 text-center">
              <div className="p-4 bg-gray-900 rounded-lg">
                <p className="text-2xl font-bold text-white">{scanResult.totalFiles}</p>
                <p className="text-sm text-gray-400">Total Files</p>
              </div>
              <div className="p-4 bg-gray-900 rounded-lg">
                <p className="text-2xl font-bold text-green-400">{scanResult.matchedFiles.length}</p>
                <p className="text-sm text-gray-400">Matched</p>
              </div>
              <div className="p-4 bg-gray-900 rounded-lg">
                <p className="text-2xl font-bold text-yellow-400">{scanResult.unmatchedFiles.length}</p>
                <p className="text-sm text-gray-400">Unmatched</p>
              </div>
              <div className="p-4 bg-gray-900 rounded-lg">
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
                  className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm transition-colors"
                >
                  Select All
                </button>
                <button
                  onClick={selectAllMatched}
                  className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm transition-colors"
                >
                  Select Matched
                </button>
                <button
                  onClick={selectAllUnmatched}
                  className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm transition-colors"
                >
                  Select Unmatched
                </button>
                <button
                  onClick={clearSelection}
                  className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm transition-colors"
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
                  className="px-4 py-2 bg-green-600 text-white rounded hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
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
                These files will be imported as new events. Consider adding the league/organization first for better organization.
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

      {/* File Browser Modal */}
      <FileBrowserModal
        isOpen={showFileBrowser}
        onClose={() => setShowFileBrowser(false)}
        onSelect={(path) => setFolderPath(path)}
        title="Select Import Folder"
      />

      {/* TheSportsDB Search Modal */}
      {showSearchModal && (
        <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50">
          <div className="bg-gray-900 rounded-lg w-full max-w-2xl max-h-[80vh] overflow-hidden border border-gray-700">
            <div className="p-4 border-b border-gray-700">
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-lg font-semibold text-white">
                  Search TheSportsDB
                </h3>
                <button
                  onClick={() => {
                    setShowSearchModal(false);
                    setActiveFile(null);
                    setSearchQuery('');
                    setSearchResults([]);
                  }}
                  className="text-gray-400 hover:text-white"
                >
                  <XCircleIcon className="w-6 h-6" />
                </button>
              </div>

              {activeFile && (
                <p className="text-sm text-gray-400 mb-3">
                  Finding match for: <span className="text-white">{activeFile.fileName}</span>
                </p>
              )}

              <div className="relative">
                <input
                  type="text"
                  value={searchQuery}
                  onChange={(e) => {
                    setSearchQuery(e.target.value);
                    searchEvents(e.target.value);
                  }}
                  placeholder="Search for events..."
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-600 rounded text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-red-500"
                  autoFocus
                />
                {searching && (
                  <ArrowPathIcon className="absolute right-3 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400 animate-spin" />
                )}
              </div>
            </div>

            <div className="p-4 overflow-y-auto max-h-[50vh]">
              {searchResults.length === 0 && !searching && searchQuery.length >= 3 && (
                <p className="text-gray-400 text-center py-8">No events found. Try a different search term.</p>
              )}

              {searchResults.length === 0 && !searching && searchQuery.length < 3 && (
                <p className="text-gray-400 text-center py-8">Enter at least 3 characters to search.</p>
              )}

              {searchResults.length > 0 && (
                <div className="space-y-2">
                  {searchResults.map((event, index) => (
                    <div
                      key={`${event.externalId || event.id}-${index}`}
                      className={`p-3 rounded-lg border cursor-pointer transition-colors ${
                        event.hasFile
                          ? 'border-gray-700 bg-gray-800/50 opacity-50 cursor-not-allowed'
                          : event.existsInDatabase
                          ? 'border-green-700 bg-green-900/20 hover:bg-green-900/40'
                          : 'border-gray-700 bg-gray-800 hover:bg-gray-700'
                      }`}
                      onClick={() => {
                        if (!event.hasFile && event.id) {
                          selectEventForFile(event);
                        }
                      }}
                    >
                      <div className="flex items-center justify-between">
                        <div className="flex-1 min-w-0">
                          <h4 className="font-medium text-white truncate">{event.title}</h4>
                          <div className="text-sm text-gray-400 mt-1 flex flex-wrap gap-2">
                            <span>{event.sport}</span>
                            {event.leagueName && <span>• {event.leagueName}</span>}
                            <span>• {new Date(event.eventDate).toLocaleDateString()}</span>
                          </div>
                          {(event.homeTeam || event.awayTeam) && (
                            <p className="text-sm text-gray-500 mt-1">
                              {event.homeTeam} vs {event.awayTeam}
                            </p>
                          )}
                        </div>
                        <div className="ml-4 flex-shrink-0">
                          {event.hasFile ? (
                            <span className="text-xs text-red-400 bg-red-900/30 px-2 py-1 rounded">Has File</span>
                          ) : event.existsInDatabase ? (
                            <span className="text-xs text-green-400 bg-green-900/30 px-2 py-1 rounded">In Database</span>
                          ) : (
                            <span className="text-xs text-blue-400 bg-blue-900/30 px-2 py-1 rounded">From API</span>
                          )}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div className="p-4 border-t border-gray-700 flex justify-end gap-2">
              <button
                onClick={() => {
                  setShowSearchModal(false);
                  setActiveFile(null);
                  setSearchQuery('');
                  setSearchResults([]);
                }}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Bulk Import Wizard */}
      <BulkImportWizard
        isOpen={showBulkWizard}
        onClose={() => setShowBulkWizard(false)}
        onComplete={() => {
          // Optionally refresh the page or show a success message
        }}
      />
    </div>
  );
};

export default LibraryImportPage;
