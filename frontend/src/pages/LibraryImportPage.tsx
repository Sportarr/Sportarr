import React, { useState, useCallback, useRef, useEffect } from 'react';
import {
  FolderIcon,
  MagnifyingGlassIcon,
  CheckCircleIcon,
  XCircleIcon,
  ExclamationCircleIcon,
  FolderOpenIcon,
  LinkIcon,
  ArrowPathIcon,
  ArrowRightIcon,
  ArrowLeftIcon,
  DocumentArrowDownIcon,
  PencilSquareIcon
} from '@heroicons/react/24/outline';
import FileBrowserModal from '../components/FileBrowserModal';
import FileDetailsModal from '../components/FileDetailsModal';
import { apiGet, apiPost } from '../utils/api';

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
  matchedLeagueName?: string;
  matchedSeason?: string;
  destinationPreview?: string;
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
  partName?: string;
  partNumber?: number;
  leagueId?: number;
  season?: string;
}

interface FileMapping {
  eventId?: number;
  eventTitle?: string;
  leagueId?: number;
  leagueName?: string;
  season?: string;
  partName?: string;
  partNumber?: number;
  createNew?: boolean;
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

type WizardStep = 'select' | 'scan' | 'review' | 'importing' | 'complete';

const LibraryImportPage: React.FC = () => {
  // Wizard step
  const [currentStep, setCurrentStep] = useState<WizardStep>('select');

  // Folder selection
  const [folderPath, setFolderPath] = useState('');
  const [includeSubfolders, setIncludeSubfolders] = useState(true);
  const [showFileBrowser, setShowFileBrowser] = useState(false);

  // Scan state
  const [scanning, setScanning] = useState(false);
  const [scanResult, setScanResult] = useState<ScanResult | null>(null);
  const [scanError, setScanError] = useState<string | null>(null);

  // Selection state
  const [selectedFiles, setSelectedFiles] = useState<Set<string>>(new Set());
  const [fileEventMappings, setFileEventMappings] = useState<Map<string, FileMapping>>(new Map());

  // Import state
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<ImportResult | null>(null);

  // File details modal state
  const [showFileDetailsModal, setShowFileDetailsModal] = useState(false);
  const [activeFile, setActiveFile] = useState<ImportableFile | null>(null);

  // Legacy search modal state (keeping for quick search)
  const [showSearchModal, setShowSearchModal] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<EventSearchResult[]>([]);
  const [searching, setSearching] = useState(false);
  const searchTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const steps: { key: WizardStep; label: string; number: number }[] = [
    { key: 'select', label: 'Select Folder', number: 1 },
    { key: 'review', label: 'Review & Select', number: 2 },
    { key: 'complete', label: 'Complete', number: 3 }
  ];

  const handleScan = async () => {
    if (!folderPath.trim()) {
      setScanError('Please select a folder path');
      return;
    }

    setCurrentStep('scan');
    setScanning(true);
    setScanError(null);
    setScanResult(null);
    setSelectedFiles(new Set());
    setFileEventMappings(new Map());

    try {
      const response = await apiPost(
        `/api/library/scan?folderPath=${encodeURIComponent(folderPath)}&includeSubfolders=${includeSubfolders}`,
        {}
      );

      if (!response.ok) {
        throw new Error('Failed to scan folder');
      }

      const result: ScanResult = await response.json();
      setScanResult(result);

      // Auto-select all matched files
      const autoSelected = new Set(result.matchedFiles.map(f => f.filePath));
      setSelectedFiles(autoSelected);

      setCurrentStep('review');
    } catch (err) {
      setScanError(err instanceof Error ? err.message : 'An error occurred');
      setCurrentStep('select');
    } finally {
      setScanning(false);
    }
  };

  // TheSportsDB search for unmatched files
  const searchEvents = useCallback((query: string) => {
    if (searchTimeoutRef.current) {
      clearTimeout(searchTimeoutRef.current);
    }

    if (!query.trim() || query.length < 3) {
      setSearchResults([]);
      return;
    }

    searchTimeoutRef.current = setTimeout(async () => {
      setSearching(true);
      try {
        const response = await apiGet(
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

  useEffect(() => {
    return () => {
      if (searchTimeoutRef.current) {
        clearTimeout(searchTimeoutRef.current);
      }
    };
  }, []);

  const openFileDetailsModal = (file: ImportableFile) => {
    setActiveFile(file);
    setShowFileDetailsModal(true);
  };

  const handleFileDetailsSave = (mapping: FileMapping) => {
    if (!activeFile) return;

    const newMappings = new Map(fileEventMappings);
    newMappings.set(activeFile.filePath, mapping);
    setFileEventMappings(newMappings);

    const newSelected = new Set(selectedFiles);
    newSelected.add(activeFile.filePath);
    setSelectedFiles(newSelected);

    setActiveFile(null);
  };

  // Legacy quick search functions
  const openSearchForFile = (file: ImportableFile) => {
    setActiveFile(file);
    setSearchQuery(file.parsedTitle || file.fileName);
    setShowSearchModal(true);
    if (file.parsedTitle || file.fileName) {
      searchEvents(file.parsedTitle || file.fileName);
    }
  };

  const selectEventForFile = (event: EventSearchResult) => {
    if (!activeFile || !event.id) return;

    const newMappings = new Map(fileEventMappings);
    newMappings.set(activeFile.filePath, { eventId: event.id, eventTitle: event.title });
    setFileEventMappings(newMappings);

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
      setScanError('No files selected for import');
      return;
    }

    setCurrentStep('importing');
    setImporting(true);
    setScanError(null);

    try {
      const requests: FileImportRequest[] = Array.from(selectedFiles).map(filePath => {
        // Also check alreadyInLibrary for re-imports
        const file = [...scanResult.matchedFiles, ...scanResult.unmatchedFiles, ...scanResult.alreadyInLibrary]
          .find(f => f.filePath === filePath);

        if (!file) {
          throw new Error(`File not found: ${filePath}`);
        }

        const manualMapping = fileEventMappings.get(filePath);
        if (manualMapping) {
          return {
            filePath: file.filePath,
            eventId: manualMapping.eventId,
            createNew: manualMapping.createNew ?? false,
            eventTitle: manualMapping.eventTitle,
            partName: manualMapping.partName,
            partNumber: manualMapping.partNumber,
            leagueId: manualMapping.leagueId,
            season: manualMapping.season
          };
        }

        // Use matchedEventId for new matches, or existingEventId for re-imports
        if (file.matchedEventId || file.existingEventId) {
          return {
            filePath: file.filePath,
            eventId: file.matchedEventId || file.existingEventId,
            createNew: false
          };
        }

        return {
          filePath: file.filePath,
          createNew: true,
          eventTitle: file.parsedTitle,
          organization: file.parsedOrganization,
          eventDate: file.parsedDate,
          quality: file.quality
        };
      });

      const response = await apiPost('/api/library/import', requests);

      if (!response.ok) {
        throw new Error('Failed to import files');
      }

      const result: ImportResult = await response.json();
      setImportResult(result);
      setCurrentStep('complete');
    } catch (err) {
      setScanError(err instanceof Error ? err.message : 'An error occurred');
      setCurrentStep('review');
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

  const selectAll = () => {
    if (!scanResult) return;
    const all = [...scanResult.matchedFiles, ...scanResult.unmatchedFiles, ...scanResult.alreadyInLibrary].map(f => f.filePath);
    setSelectedFiles(new Set(all));
  };

  const selectMatched = () => {
    if (!scanResult) return;
    // Include both matched and already-in-library (for re-import)
    const matched = [...scanResult.matchedFiles, ...scanResult.alreadyInLibrary].map(f => f.filePath);
    setSelectedFiles(new Set(matched));
  };

  const clearSelection = () => {
    setSelectedFiles(new Set());
  };

  const resetWizard = () => {
    setCurrentStep('select');
    setFolderPath('');
    setScanResult(null);
    setScanError(null);
    setSelectedFiles(new Set());
    setFileEventMappings(new Map());
    setImportResult(null);
  };

  const getConfidenceBadge = (confidence?: number) => {
    if (!confidence) return null;
    let colorClass = 'bg-red-600';
    if (confidence >= 80) colorClass = 'bg-green-600';
    else if (confidence >= 60) colorClass = 'bg-yellow-600';
    else if (confidence >= 40) colorClass = 'bg-orange-600';
    return (
      <span className={`${colorClass} text-white text-xs px-2 py-0.5 rounded-full`}>
        {confidence}%
      </span>
    );
  };

  const getStepIndex = (step: WizardStep) => {
    if (step === 'select') return 0;
    if (step === 'scan') return 0;
    if (step === 'review') return 1;
    if (step === 'importing') return 1;
    if (step === 'complete') return 2;
    return 0;
  };

  return (
    <div className="p-8 max-w-6xl mx-auto">
      {/* Header */}
      <div className="mb-6">
        <h1 className="text-3xl font-bold text-white mb-2 flex items-center gap-3">
          <DocumentArrowDownIcon className="w-8 h-8 text-red-500" />
          Library Import
        </h1>
        <p className="text-gray-400">
          Import existing video files into your Sportarr library with proper organization and renaming
        </p>
      </div>

      {/* Progress Steps */}
      <div className="mb-8">
        <div className="flex items-center gap-2">
          {steps.map((step, index) => (
            <React.Fragment key={step.key}>
              <div className={`flex items-center gap-2 ${
                getStepIndex(currentStep) === index
                  ? 'text-red-400'
                  : getStepIndex(currentStep) > index
                  ? 'text-green-400'
                  : 'text-gray-500'
              }`}>
                <div className={`w-10 h-10 rounded-full flex items-center justify-center text-sm font-bold ${
                  getStepIndex(currentStep) === index
                    ? 'bg-red-600 text-white'
                    : getStepIndex(currentStep) > index
                    ? 'bg-green-600 text-white'
                    : 'bg-gray-700 text-gray-400'
                }`}>
                  {getStepIndex(currentStep) > index ? (
                    <CheckCircleIcon className="w-6 h-6" />
                  ) : (
                    step.number
                  )}
                </div>
                <span className="font-medium hidden sm:inline">{step.label}</span>
              </div>
              {index < steps.length - 1 && (
                <div className={`flex-1 h-1 rounded ${
                  getStepIndex(currentStep) > index ? 'bg-green-600' : 'bg-gray-700'
                }`} />
              )}
            </React.Fragment>
          ))}
        </div>
      </div>

      {/* Error Display */}
      {scanError && (
        <div className="mb-6 bg-red-900/20 border border-red-800 rounded-lg p-4">
          <p className="text-red-400 flex items-center gap-2">
            <XCircleIcon className="w-5 h-5" />
            {scanError}
          </p>
        </div>
      )}

      {/* Step 1: Select Folder */}
      {currentStep === 'select' && (
        <div className="bg-gray-800 rounded-lg p-6 border border-red-900/30">
          <div className="flex items-center gap-4 mb-6">
            <FolderIcon className="w-12 h-12 text-yellow-400" />
            <div>
              <h2 className="text-xl font-semibold text-white">Select Import Folder</h2>
              <p className="text-gray-400">Choose a folder containing video files to import</p>
            </div>
          </div>

          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">Folder Path</label>
              <div className="flex gap-2">
                <input
                  type="text"
                  value={folderPath}
                  onChange={(e) => setFolderPath(e.target.value)}
                  placeholder="Click Browse to select a folder..."
                  className="flex-1 px-4 py-3 bg-gray-700 border border-gray-600 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-red-500"
                  readOnly
                />
                <button
                  onClick={() => setShowFileBrowser(true)}
                  className="px-6 py-3 bg-gray-700 hover:bg-gray-600 text-white rounded-lg border border-gray-600 flex items-center gap-2 transition-colors"
                >
                  <FolderOpenIcon className="w-5 h-5" />
                  Browse
                </button>
              </div>
            </div>

            <label className="flex items-center gap-3 cursor-pointer">
              <input
                type="checkbox"
                checked={includeSubfolders}
                onChange={(e) => setIncludeSubfolders(e.target.checked)}
                className="w-5 h-5 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
              />
              <span className="text-gray-300">Include subfolders (recursive scan)</span>
            </label>
          </div>

          <div className="mt-6 p-4 bg-blue-900/20 border border-blue-800 rounded-lg">
            <p className="text-sm text-gray-300">
              <strong className="text-blue-400">How it works:</strong> Files will be scanned and matched to events using
              sports-specific filename parsing. Matched files will be moved/copied to your library folder with proper naming.
              Configure import behavior (move vs copy) in Settings → Media Management.
            </p>
          </div>

          <div className="mt-6 flex justify-end">
            <button
              onClick={handleScan}
              disabled={!folderPath.trim()}
              className="px-6 py-3 bg-red-600 hover:bg-red-700 text-white rounded-lg flex items-center gap-2 transition-colors disabled:opacity-50 disabled:cursor-not-allowed font-medium"
            >
              Scan Folder
              <ArrowRightIcon className="w-5 h-5" />
            </button>
          </div>
        </div>
      )}

      {/* Scanning State */}
      {currentStep === 'scan' && (
        <div className="bg-gray-800 rounded-lg p-12 border border-red-900/30 text-center">
          <ArrowPathIcon className="w-16 h-16 mx-auto mb-4 text-red-400 animate-spin" />
          <h2 className="text-xl font-semibold text-white mb-2">Scanning...</h2>
          <p className="text-gray-400">Analyzing video files and matching to events</p>
        </div>
      )}

      {/* Step 2: Review & Select */}
      {currentStep === 'review' && scanResult && (
        <div className="space-y-6">
          {/* Summary Cards */}
          <div className="grid grid-cols-4 gap-4">
            <div className="bg-gray-800 rounded-lg p-4 border border-gray-700 text-center">
              <p className="text-3xl font-bold text-white">{scanResult.totalFiles}</p>
              <p className="text-sm text-gray-400 mt-1">Total Files</p>
            </div>
            <div className="bg-gray-800 rounded-lg p-4 border border-green-700 text-center">
              <p className="text-3xl font-bold text-green-400">{scanResult.matchedFiles.length}</p>
              <p className="text-sm text-gray-400 mt-1">Auto-Matched</p>
            </div>
            <div className="bg-gray-800 rounded-lg p-4 border border-yellow-700 text-center">
              <p className="text-3xl font-bold text-yellow-400">{scanResult.unmatchedFiles.length}</p>
              <p className="text-sm text-gray-400 mt-1">Unmatched</p>
            </div>
            <div className="bg-gray-800 rounded-lg p-4 border border-gray-700 text-center">
              <p className="text-3xl font-bold text-gray-400">{scanResult.alreadyInLibrary.length}</p>
              <p className="text-sm text-gray-400 mt-1">Already Imported</p>
            </div>
          </div>

          {/* Selection Controls */}
          <div className="bg-gray-800 rounded-lg p-4 border border-red-900/30 flex items-center gap-3 flex-wrap">
            <button onClick={selectAll} className="px-3 py-1.5 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm transition-colors">
              Select All
            </button>
            <button onClick={selectMatched} className="px-3 py-1.5 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm transition-colors">
              Select Matched Only
            </button>
            <button onClick={clearSelection} className="px-3 py-1.5 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm transition-colors">
              Clear Selection
            </button>
            <div className="flex-1" />
            <span className="text-gray-400 text-sm">{selectedFiles.size} file(s) selected</span>
          </div>

          {/* File Lists */}
          <div className="space-y-6">
            {/* Matched Files */}
            {scanResult.matchedFiles.length > 0 && (
              <div>
                <h3 className="text-lg font-semibold text-white mb-3 flex items-center gap-2">
                  <CheckCircleIcon className="w-6 h-6 text-green-400" />
                  Matched Files ({scanResult.matchedFiles.length})
                </h3>
                <div className="space-y-2 max-h-64 overflow-y-auto">
                  {scanResult.matchedFiles.map(file => {
                    const isSelected = selectedFiles.has(file.filePath);
                    const mapping = fileEventMappings.get(file.filePath);
                    return (
                      <div key={file.filePath} className={`p-3 bg-gray-800 rounded-lg border ${isSelected ? 'border-green-500' : 'border-gray-700'}`}>
                        <div className="flex items-center gap-3">
                          <input
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => toggleFileSelection(file.filePath)}
                            className="w-5 h-5 rounded border-gray-600 bg-gray-700 text-green-600"
                          />
                          <div className="flex-1 min-w-0">
                            <p className="text-white font-medium truncate">{file.fileName}</p>
                            <p className="text-sm text-gray-400">
                              → <span className="text-green-400">{file.destinationPreview || mapping?.eventTitle || file.matchedEventTitle}</span>
                              {mapping?.partName && (
                                <span className="text-blue-400 ml-1">({mapping.partName})</span>
                              )}
                              <span className="text-gray-500 ml-2">({file.fileSizeFormatted})</span>
                            </p>
                          </div>
                          {!mapping && getConfidenceBadge(file.matchConfidence)}
                          <button
                            onClick={() => openFileDetailsModal(file)}
                            className="px-3 py-1 bg-gray-600 hover:bg-gray-500 text-white text-sm rounded transition-colors flex items-center gap-1"
                            title="Edit match details"
                          >
                            <PencilSquareIcon className="w-4 h-4" />
                            Edit
                          </button>
                        </div>
                      </div>
                    );
                  })}
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
                <p className="text-sm text-gray-400 mb-2">
                  Click "Match" to manually select the league, season, event, and part for each file
                </p>
                <div className="space-y-2 max-h-64 overflow-y-auto">
                  {scanResult.unmatchedFiles.map(file => {
                    const isSelected = selectedFiles.has(file.filePath);
                    const mapping = fileEventMappings.get(file.filePath);
                    return (
                      <div key={file.filePath} className={`p-3 bg-gray-800 rounded-lg border ${mapping ? 'border-green-500' : isSelected ? 'border-yellow-500' : 'border-gray-700'}`}>
                        <div className="flex items-center gap-3">
                          <input
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => toggleFileSelection(file.filePath)}
                            className="w-5 h-5 rounded border-gray-600 bg-gray-700 text-yellow-600"
                          />
                          <div className="flex-1 min-w-0">
                            <p className="text-white font-medium truncate">{file.fileName}</p>
                            <p className="text-sm text-gray-400">
                              {mapping ? (
                                <>
                                  <LinkIcon className="w-3 h-3 inline mr-1" />
                                  <span className="text-green-400">{mapping.eventTitle}</span>
                                  {mapping.partName && (
                                    <span className="text-blue-400 ml-1">({mapping.partName})</span>
                                  )}
                                  {mapping.leagueName && (
                                    <span className="text-gray-500 ml-1">• {mapping.leagueName}</span>
                                  )}
                                </>
                              ) : file.parsedTitle ? (
                                <>Parsed: {file.parsedTitle}</>
                              ) : (
                                <span className="text-yellow-400">Will create new event</span>
                              )}
                              <span className="text-gray-500 ml-2">({file.fileSizeFormatted})</span>
                            </p>
                          </div>
                          <button
                            onClick={() => openFileDetailsModal(file)}
                            className="px-3 py-1 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded transition-colors flex items-center gap-1"
                          >
                            <PencilSquareIcon className="w-4 h-4" />
                            {mapping ? 'Edit' : 'Match'}
                          </button>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {/* Already in Library */}
            {scanResult.alreadyInLibrary.length > 0 && (
              <div>
                <h3 className="text-lg font-semibold text-white mb-3 flex items-center gap-2">
                  <CheckCircleIcon className="w-6 h-6 text-blue-400" />
                  Already in Library ({scanResult.alreadyInLibrary.length})
                </h3>
                <p className="text-sm text-gray-400 mb-2">
                  These files are linked to events but may need re-importing if they weren't properly moved/renamed.
                  Click "Re-import" to move them to the correct location with proper naming.
                </p>
                <div className="space-y-2 max-h-64 overflow-y-auto">
                  {scanResult.alreadyInLibrary.map(file => {
                    const isSelected = selectedFiles.has(file.filePath);
                    const mapping = fileEventMappings.get(file.filePath);
                    return (
                      <div key={file.filePath} className={`p-3 bg-gray-800 rounded-lg border ${isSelected ? 'border-blue-500' : 'border-gray-700'}`}>
                        <div className="flex items-center gap-3">
                          <input
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => toggleFileSelection(file.filePath)}
                            className="w-5 h-5 rounded border-gray-600 bg-gray-700 text-blue-600"
                          />
                          <div className="flex-1 min-w-0">
                            <p className="text-white font-medium truncate">{file.fileName}</p>
                            <p className="text-sm text-gray-400">
                              {mapping ? (
                                <>
                                  <LinkIcon className="w-3 h-3 inline mr-1" />
                                  <span className="text-green-400">{mapping.eventTitle}</span>
                                  {mapping.partName && (
                                    <span className="text-blue-400 ml-1">({mapping.partName})</span>
                                  )}
                                </>
                              ) : file.matchedEventTitle ? (
                                <>
                                  Current: <span className="text-blue-400">{file.matchedEventTitle}</span>
                                </>
                              ) : (
                                <span className="text-gray-500">Linked to event #{file.existingEventId}</span>
                              )}
                              <span className="text-gray-500 ml-2">({file.fileSizeFormatted})</span>
                            </p>
                          </div>
                          <button
                            onClick={() => openFileDetailsModal(file)}
                            className="px-3 py-1 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded transition-colors flex items-center gap-1"
                          >
                            <ArrowPathIcon className="w-4 h-4" />
                            Re-import
                          </button>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}
          </div>

          {/* Navigation */}
          <div className="flex justify-between pt-4">
            <button
              onClick={() => setCurrentStep('select')}
              className="px-6 py-3 bg-gray-700 hover:bg-gray-600 text-white rounded-lg flex items-center gap-2 transition-colors"
            >
              <ArrowLeftIcon className="w-5 h-5" />
              Back
            </button>
            <button
              onClick={handleImport}
              disabled={selectedFiles.size === 0}
              className="px-6 py-3 bg-green-600 hover:bg-green-700 text-white rounded-lg flex items-center gap-2 transition-colors disabled:opacity-50 disabled:cursor-not-allowed font-medium"
            >
              Import {selectedFiles.size} File(s)
              <ArrowRightIcon className="w-5 h-5" />
            </button>
          </div>
        </div>
      )}

      {/* Importing State */}
      {currentStep === 'importing' && (
        <div className="bg-gray-800 rounded-lg p-12 border border-red-900/30 text-center">
          <ArrowPathIcon className="w-16 h-16 mx-auto mb-4 text-green-400 animate-spin" />
          <h2 className="text-xl font-semibold text-white mb-2">Importing...</h2>
          <p className="text-gray-400">Moving files to library and updating database</p>
        </div>
      )}

      {/* Step 3: Complete */}
      {currentStep === 'complete' && importResult && (
        <div className="bg-gray-800 rounded-lg p-8 border border-green-700">
          <div className="text-center mb-8">
            <CheckCircleIcon className="w-20 h-20 mx-auto mb-4 text-green-400" />
            <h2 className="text-2xl font-bold text-white mb-2">Import Complete!</h2>
            <p className="text-gray-400">Your files have been imported to the library</p>
          </div>

          <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
            <div className="bg-gray-900 rounded-lg p-4 text-center">
              <p className="text-3xl font-bold text-green-400">{importResult.imported.length}</p>
              <p className="text-sm text-gray-400 mt-1">Imported</p>
            </div>
            <div className="bg-gray-900 rounded-lg p-4 text-center">
              <p className="text-3xl font-bold text-blue-400">{importResult.created.length}</p>
              <p className="text-sm text-gray-400 mt-1">Created</p>
            </div>
            <div className="bg-gray-900 rounded-lg p-4 text-center">
              <p className="text-3xl font-bold text-gray-400">{importResult.skipped.length}</p>
              <p className="text-sm text-gray-400 mt-1">Skipped</p>
            </div>
            <div className="bg-gray-900 rounded-lg p-4 text-center">
              <p className="text-3xl font-bold text-red-400">{importResult.failed.length}</p>
              <p className="text-sm text-gray-400 mt-1">Failed</p>
            </div>
          </div>

          {importResult.errors.length > 0 && (
            <div className="mb-8 p-4 bg-red-900/20 border border-red-800 rounded-lg">
              <h3 className="text-red-400 font-medium mb-2">Errors:</h3>
              <ul className="text-sm text-red-400 space-y-1">
                {importResult.errors.map((err, i) => (
                  <li key={i}>• {err}</li>
                ))}
              </ul>
            </div>
          )}

          <div className="flex justify-center">
            <button
              onClick={resetWizard}
              className="px-6 py-3 bg-red-600 hover:bg-red-700 text-white rounded-lg flex items-center gap-2 transition-colors font-medium"
            >
              Import More Files
            </button>
          </div>
        </div>
      )}

      {/* File Browser Modal */}
      <FileBrowserModal
        isOpen={showFileBrowser}
        onClose={() => setShowFileBrowser(false)}
        onSelect={(path) => setFolderPath(path)}
        title="Select Import Folder"
      />

      {/* Search Modal */}
      {showSearchModal && (
        <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50">
          <div className="bg-gray-900 rounded-lg w-full max-w-2xl max-h-[80vh] overflow-hidden border border-gray-700">
            <div className="p-4 border-b border-gray-700">
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-lg font-semibold text-white">Search Events</h3>
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
                  Matching: <span className="text-white">{activeFile.fileName}</span>
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
                <p className="text-gray-400 text-center py-8">No events found</p>
              )}

              {searchResults.length === 0 && !searching && searchQuery.length < 3 && (
                <p className="text-gray-400 text-center py-8">Enter at least 3 characters</p>
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
                          <div className="text-sm text-gray-400 mt-1">
                            {event.sport} • {new Date(event.eventDate).toLocaleDateString()}
                          </div>
                        </div>
                        <div className="ml-4">
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

            <div className="p-4 border-t border-gray-700 flex justify-end">
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

      {/* File Details Modal */}
      <FileDetailsModal
        isOpen={showFileDetailsModal}
        onClose={() => {
          setShowFileDetailsModal(false);
          setActiveFile(null);
        }}
        onSave={handleFileDetailsSave}
        fileName={activeFile?.fileName || ''}
        parsedTitle={activeFile?.parsedTitle}
        parsedDate={activeFile?.parsedDate}
        currentMapping={activeFile ? fileEventMappings.get(activeFile.filePath) : undefined}
      />
    </div>
  );
};

export default LibraryImportPage;
