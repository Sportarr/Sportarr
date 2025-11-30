import React, { useState } from 'react';
import {
  FolderIcon,
  FolderOpenIcon,
  CheckCircleIcon,
  XCircleIcon,
  ExclamationCircleIcon,
  ArrowRightIcon,
  ArrowLeftIcon,
  ArrowPathIcon,
  LinkIcon
} from '@heroicons/react/24/outline';
import FileBrowserModal from './FileBrowserModal';

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

interface ImportResult {
  imported: string[];
  created: string[];
  skipped: string[];
  failed: string[];
  errors: string[];
}

interface BulkImportWizardProps {
  isOpen: boolean;
  onClose: () => void;
  onComplete?: () => void;
}

type WizardStep = 'source' | 'scan' | 'review' | 'import' | 'complete';

const BulkImportWizard: React.FC<BulkImportWizardProps> = ({ isOpen, onClose, onComplete }) => {
  // Wizard state
  const [currentStep, setCurrentStep] = useState<WizardStep>('source');

  // Source configuration
  const [folderPath, setFolderPath] = useState('');
  const [includeSubfolders, setIncludeSubfolders] = useState(true);
  const [showFileBrowser, setShowFileBrowser] = useState(false);

  // Scan results
  const [scanning, setScanning] = useState(false);
  const [scanResult, setScanResult] = useState<ScanResult | null>(null);
  const [scanError, setScanError] = useState<string | null>(null);

  // Selection state
  const [selectedFiles, setSelectedFiles] = useState<Set<string>>(new Set());
  const [fileEventMappings, setFileEventMappings] = useState<Map<string, { eventId: number; eventTitle: string }>>(new Map());

  // Import state
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<ImportResult | null>(null);

  const steps: { key: WizardStep; label: string }[] = [
    { key: 'source', label: 'Select Folder' },
    { key: 'scan', label: 'Scan Files' },
    { key: 'review', label: 'Review Matches' },
    { key: 'import', label: 'Import' },
    { key: 'complete', label: 'Complete' }
  ];

  const handleScan = async () => {
    if (!folderPath.trim()) {
      setScanError('Please select a folder path');
      return;
    }

    setScanning(true);
    setScanError(null);
    setScanResult(null);

    try {
      const endpoint = `/api/library/scan?folderPath=${encodeURIComponent(folderPath)}&includeSubfolders=${includeSubfolders}`;
      const response = await fetch(endpoint, { method: 'POST' });

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
    } finally {
      setScanning(false);
    }
  };

  const handleImport = async () => {
    if (!scanResult || selectedFiles.size === 0) {
      return;
    }

    setImporting(true);
    setCurrentStep('import');

    try {
      const requests = Array.from(selectedFiles).map(filePath => {
        const file = [...scanResult.matchedFiles, ...scanResult.unmatchedFiles]
          .find(f => f.filePath === filePath);

        if (!file) {
          return null;
        }

        // Check for manual mapping
        const manualMapping = fileEventMappings.get(filePath);
        if (manualMapping) {
          return {
            filePath: file.filePath,
            eventId: manualMapping.eventId,
            createNew: false
          };
        }

        // Auto-matched
        if (file.matchedEventId) {
          return {
            filePath: file.filePath,
            eventId: file.matchedEventId,
            createNew: false
          };
        }

        // Create new
        return {
          filePath: file.filePath,
          createNew: true,
          eventTitle: file.parsedTitle,
          organization: file.parsedOrganization,
          eventDate: file.parsedDate,
          quality: file.quality
        };
      }).filter(Boolean);

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
      setCurrentStep('complete');
    } catch (err) {
      setScanError(err instanceof Error ? err.message : 'An error occurred');
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
    const all = [...scanResult.matchedFiles, ...scanResult.unmatchedFiles].map(f => f.filePath);
    setSelectedFiles(new Set(all));
  };

  const selectMatched = () => {
    if (!scanResult) return;
    setSelectedFiles(new Set(scanResult.matchedFiles.map(f => f.filePath)));
  };

  const clearSelection = () => {
    setSelectedFiles(new Set());
  };

  const handleClose = () => {
    // Reset state
    setCurrentStep('source');
    setFolderPath('');
    setScanResult(null);
    setScanError(null);
    setSelectedFiles(new Set());
    setFileEventMappings(new Map());
    setImportResult(null);
    onClose();
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

  const canProceed = () => {
    if (currentStep === 'source') {
      return !!folderPath;
    }
    if (currentStep === 'review') {
      return selectedFiles.size > 0;
    }
    return true;
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50">
      <div className="bg-gray-900 rounded-lg w-full max-w-4xl max-h-[90vh] overflow-hidden border border-gray-700 flex flex-col">
        {/* Header */}
        <div className="p-4 border-b border-gray-700">
          <div className="flex items-center justify-between mb-4">
            <h2 className="text-xl font-semibold text-white">Bulk Import Wizard</h2>
            <button onClick={handleClose} className="text-gray-400 hover:text-white">
              <XCircleIcon className="w-6 h-6" />
            </button>
          </div>

          {/* Progress Steps */}
          <div className="flex items-center gap-2">
            {steps.map((step, index) => (
              <React.Fragment key={step.key}>
                <div className={`flex items-center gap-2 ${
                  currentStep === step.key
                    ? 'text-red-400'
                    : steps.findIndex(s => s.key === currentStep) > index
                    ? 'text-green-400'
                    : 'text-gray-500'
                }`}>
                  <div className={`w-8 h-8 rounded-full flex items-center justify-center text-sm font-medium ${
                    currentStep === step.key
                      ? 'bg-red-600 text-white'
                      : steps.findIndex(s => s.key === currentStep) > index
                      ? 'bg-green-600 text-white'
                      : 'bg-gray-700 text-gray-400'
                  }`}>
                    {steps.findIndex(s => s.key === currentStep) > index ? (
                      <CheckCircleIcon className="w-5 h-5" />
                    ) : (
                      index + 1
                    )}
                  </div>
                  <span className="text-sm hidden sm:inline">{step.label}</span>
                </div>
                {index < steps.length - 1 && (
                  <div className={`flex-1 h-0.5 ${
                    steps.findIndex(s => s.key === currentStep) > index
                      ? 'bg-green-600'
                      : 'bg-gray-700'
                  }`} />
                )}
              </React.Fragment>
            ))}
          </div>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-6">
          {/* Step 1: Select Source */}
          {currentStep === 'source' && (
            <div className="space-y-6">
              <div className="flex items-center gap-4 mb-6">
                <FolderIcon className="w-12 h-12 text-yellow-400" />
                <div>
                  <h3 className="text-lg font-medium text-white">Select Import Folder</h3>
                  <p className="text-sm text-gray-400">Browse to a folder containing video files to import into Sportarr</p>
                </div>
              </div>

              <div className="p-4 bg-gray-800 rounded-lg border border-gray-700">
                <div className="space-y-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Folder Path</label>
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
                </div>
              </div>

              {/* Info Box */}
              <div className="p-4 bg-blue-900/20 border border-blue-800 rounded-lg">
                <p className="text-sm text-gray-300">
                  <strong className="text-white">Tip:</strong> The wizard will scan for video files (.mkv, .mp4, .avi, etc.)
                  and attempt to match them with events in your database using sports-specific filename parsing.
                  Files that can't be matched will be available to create as new events.
                </p>
              </div>
            </div>
          )}

          {/* Step 2: Scan */}
          {currentStep === 'scan' && (
            <div className="space-y-6">
              <div className="text-center py-12">
                {scanning ? (
                  <>
                    <ArrowPathIcon className="w-16 h-16 mx-auto mb-4 text-red-400 animate-spin" />
                    <h3 className="text-lg font-medium text-white mb-2">Scanning...</h3>
                    <p className="text-gray-400">This may take a moment depending on the number of files.</p>
                  </>
                ) : scanError ? (
                  <>
                    <XCircleIcon className="w-16 h-16 mx-auto mb-4 text-red-400" />
                    <h3 className="text-lg font-medium text-white mb-2">Scan Failed</h3>
                    <p className="text-red-400">{scanError}</p>
                  </>
                ) : null}
              </div>
            </div>
          )}

          {/* Step 3: Review */}
          {currentStep === 'review' && scanResult && (
            <div className="space-y-6">
              {/* Summary */}
              <div className="grid grid-cols-4 gap-4 text-center">
                <div className="p-4 bg-gray-800 rounded-lg">
                  <p className="text-2xl font-bold text-white">{scanResult.totalFiles}</p>
                  <p className="text-sm text-gray-400">Total Files</p>
                </div>
                <div className="p-4 bg-gray-800 rounded-lg">
                  <p className="text-2xl font-bold text-green-400">{scanResult.matchedFiles.length}</p>
                  <p className="text-sm text-gray-400">Matched</p>
                </div>
                <div className="p-4 bg-gray-800 rounded-lg">
                  <p className="text-2xl font-bold text-yellow-400">{scanResult.unmatchedFiles.length}</p>
                  <p className="text-sm text-gray-400">Unmatched</p>
                </div>
                <div className="p-4 bg-gray-800 rounded-lg">
                  <p className="text-2xl font-bold text-gray-400">{scanResult.alreadyInLibrary.length}</p>
                  <p className="text-sm text-gray-400">Already Imported</p>
                </div>
              </div>

              {/* Selection Controls */}
              <div className="flex items-center gap-2 flex-wrap">
                <button onClick={selectAll} className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm">
                  Select All
                </button>
                <button onClick={selectMatched} className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm">
                  Select Matched
                </button>
                <button onClick={clearSelection} className="px-3 py-1 bg-gray-700 text-white rounded hover:bg-gray-600 text-sm">
                  Clear
                </button>
                <span className="text-sm text-gray-400 ml-auto">{selectedFiles.size} selected</span>
              </div>

              {/* File List */}
              <div className="space-y-4">
                {/* Matched Files */}
                {scanResult.matchedFiles.length > 0 && (
                  <div>
                    <h4 className="text-white font-medium mb-2 flex items-center gap-2">
                      <CheckCircleIcon className="w-5 h-5 text-green-400" />
                      Matched Files ({scanResult.matchedFiles.length})
                    </h4>
                    <div className="space-y-2 max-h-48 overflow-y-auto">
                      {scanResult.matchedFiles.map(file => {
                        const isSelected = selectedFiles.has(file.filePath);
                        const mapping = fileEventMappings.get(file.filePath);
                        return (
                          <div
                            key={file.filePath}
                            className={`p-3 bg-gray-800 rounded border ${isSelected ? 'border-green-500' : 'border-gray-700'}`}
                          >
                            <div className="flex items-center gap-3">
                              <input
                                type="checkbox"
                                checked={isSelected}
                                onChange={() => toggleFileSelection(file.filePath)}
                                className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-600"
                              />
                              <div className="flex-1 min-w-0">
                                <p className="text-white truncate">{file.fileName}</p>
                                <p className="text-sm text-gray-400">
                                  {mapping ? (
                                    <span className="text-green-400">→ {mapping.eventTitle}</span>
                                  ) : (
                                    <span className="text-green-400">→ {file.matchedEventTitle}</span>
                                  )}
                                </p>
                              </div>
                              {getConfidenceBadge(file.matchConfidence)}
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
                    <h4 className="text-white font-medium mb-2 flex items-center gap-2">
                      <ExclamationCircleIcon className="w-5 h-5 text-yellow-400" />
                      Unmatched Files ({scanResult.unmatchedFiles.length})
                    </h4>
                    <p className="text-sm text-gray-400 mb-2">These will be imported as new events</p>
                    <div className="space-y-2 max-h-48 overflow-y-auto">
                      {scanResult.unmatchedFiles.map(file => {
                        const isSelected = selectedFiles.has(file.filePath);
                        const mapping = fileEventMappings.get(file.filePath);
                        return (
                          <div
                            key={file.filePath}
                            className={`p-3 bg-gray-800 rounded border ${
                              mapping ? 'border-green-500' : isSelected ? 'border-yellow-500' : 'border-gray-700'
                            }`}
                          >
                            <div className="flex items-center gap-3">
                              <input
                                type="checkbox"
                                checked={isSelected}
                                onChange={() => toggleFileSelection(file.filePath)}
                                className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-600"
                              />
                              <div className="flex-1 min-w-0">
                                <p className="text-white truncate">{file.fileName}</p>
                                <p className="text-sm text-gray-400">
                                  {mapping ? (
                                    <>
                                      <LinkIcon className="w-3 h-3 inline mr-1" />
                                      <span className="text-green-400">{mapping.eventTitle}</span>
                                    </>
                                  ) : file.parsedTitle ? (
                                    <span>Parsed: {file.parsedTitle}</span>
                                  ) : (
                                    <span className="text-yellow-400">Will create new event</span>
                                  )}
                                </p>
                              </div>
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
                    <h4 className="text-white font-medium mb-2 flex items-center gap-2">
                      <XCircleIcon className="w-5 h-5 text-gray-500" />
                      Already in Library ({scanResult.alreadyInLibrary.length})
                    </h4>
                    <div className="space-y-2 max-h-32 overflow-y-auto opacity-50">
                      {scanResult.alreadyInLibrary.slice(0, 5).map(file => (
                        <div key={file.filePath} className="p-3 bg-gray-800 rounded border border-gray-700">
                          <p className="text-gray-400 truncate">{file.fileName}</p>
                        </div>
                      ))}
                      {scanResult.alreadyInLibrary.length > 5 && (
                        <p className="text-sm text-gray-500">
                          And {scanResult.alreadyInLibrary.length - 5} more...
                        </p>
                      )}
                    </div>
                  </div>
                )}
              </div>
            </div>
          )}

          {/* Step 4: Import Progress */}
          {currentStep === 'import' && (
            <div className="space-y-6">
              <div className="text-center py-12">
                <ArrowPathIcon className="w-16 h-16 mx-auto mb-4 text-red-400 animate-spin" />
                <h3 className="text-lg font-medium text-white mb-2">Importing...</h3>
                <p className="text-gray-400">Please wait while your files are being imported.</p>
              </div>
            </div>
          )}

          {/* Step 5: Complete */}
          {currentStep === 'complete' && importResult && (
            <div className="space-y-6">
              <div className="text-center py-8">
                <CheckCircleIcon className="w-16 h-16 mx-auto mb-4 text-green-400" />
                <h3 className="text-lg font-medium text-white mb-2">Import Complete!</h3>
              </div>

              <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-center">
                <div className="p-4 bg-gray-800 rounded-lg">
                  <p className="text-2xl font-bold text-green-400">{importResult.imported.length}</p>
                  <p className="text-sm text-gray-400">Imported</p>
                </div>
                <div className="p-4 bg-gray-800 rounded-lg">
                  <p className="text-2xl font-bold text-blue-400">{importResult.created.length}</p>
                  <p className="text-sm text-gray-400">Created</p>
                </div>
                <div className="p-4 bg-gray-800 rounded-lg">
                  <p className="text-2xl font-bold text-gray-400">{importResult.skipped.length}</p>
                  <p className="text-sm text-gray-400">Skipped</p>
                </div>
                <div className="p-4 bg-gray-800 rounded-lg">
                  <p className="text-2xl font-bold text-red-400">{importResult.failed.length}</p>
                  <p className="text-sm text-gray-400">Failed</p>
                </div>
              </div>

              {importResult.errors.length > 0 && (
                <div className="p-4 bg-red-900/20 border border-red-800 rounded-lg">
                  <h4 className="text-red-400 font-medium mb-2">Errors:</h4>
                  <ul className="text-sm text-red-400 space-y-1">
                    {importResult.errors.map((err, i) => (
                      <li key={i}>• {err}</li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="p-4 border-t border-gray-700 flex justify-between">
          <button
            onClick={() => {
              if (currentStep === 'source') {
                handleClose();
              } else if (currentStep === 'review') {
                setCurrentStep('source');
              }
            }}
            className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded flex items-center gap-2 transition-colors"
            disabled={currentStep === 'scan' || currentStep === 'import'}
          >
            <ArrowLeftIcon className="w-4 h-4" />
            {currentStep === 'source' ? 'Cancel' : 'Back'}
          </button>

          <button
            onClick={() => {
              if (currentStep === 'source') {
                setCurrentStep('scan');
                handleScan();
              } else if (currentStep === 'review') {
                handleImport();
              } else if (currentStep === 'complete') {
                handleClose();
                onComplete?.();
              }
            }}
            disabled={!canProceed() || currentStep === 'scan' || currentStep === 'import'}
            className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded flex items-center gap-2 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {currentStep === 'complete' ? 'Close' : (
              <>
                {currentStep === 'source' ? 'Scan Files' : 'Import Selected'}
                <ArrowRightIcon className="w-4 h-4" />
              </>
            )}
          </button>
        </div>
      </div>

      {/* File Browser Modal */}
      <FileBrowserModal
        isOpen={showFileBrowser}
        onClose={() => setShowFileBrowser(false)}
        onSelect={(path) => setFolderPath(path)}
        title="Select Import Folder"
      />
    </div>
  );
};

export default BulkImportWizard;
