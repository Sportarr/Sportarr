import { useState, useEffect } from 'react';
import {
  XMarkIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  MagnifyingGlassIcon
} from '@heroicons/react/24/outline';
import apiClient from '../api/client';

interface Event {
  id: number;
  title: string;
  organization?: string;
  eventDate: string;
  league?: {
    name: string;
  };
  season?: string;
}

interface ImportSuggestion {
  eventId?: number;
  eventTitle?: string;
  league?: string;
  season?: string;
  eventDate?: string;
  quality?: string;
  qualityScore?: number;
  part?: string;
  confidence: number;
}

interface PendingImport {
  id: number;
  title: string;
  filePath: string;
  size: number;
  quality?: string;
  qualityScore: number;
  suggestedEventId?: number;
  suggestedEvent?: Event;
  suggestedPart?: string;
  suggestionConfidence: number;
  detected: string;
  protocol?: string;
}

interface Props {
  pendingImport: PendingImport;
  onClose: () => void;
  onSuccess: () => void;
}

export default function ManualImportModal({ pendingImport, onClose, onSuccess }: Props) {
  const [isLoading, setIsLoading] = useState(false);
  const [allMatches, setAllMatches] = useState<ImportSuggestion[]>([]);
  const [selectedEventId, setSelectedEventId] = useState<number | null>(
    pendingImport.suggestedEventId || null
  );
  const [selectedPart, setSelectedPart] = useState<string | undefined>(
    pendingImport.suggestedPart
  );
  const [showAllMatches, setShowAllMatches] = useState(false);

  useEffect(() => {
    loadAllMatches();
  }, []);

  const loadAllMatches = async () => {
    try {
      const response = await apiClient.get(`/pending-imports/${pendingImport.id}/matches`);
      setAllMatches(response.data);
    } catch (error) {
      console.error('Failed to load matches:', error);
    }
  };

  const handleAccept = async () => {
    if (!selectedEventId) {
      alert('Please select an event to import to');
      return;
    }

    setIsLoading(true);
    try {
      // Update suggestion if user changed it
      if (selectedEventId !== pendingImport.suggestedEventId || selectedPart !== pendingImport.suggestedPart) {
        await apiClient.put(`/pending-imports/${pendingImport.id}/suggestion`, {
          eventId: selectedEventId,
          part: selectedPart
        });
      }

      // Accept the import
      await apiClient.post(`/pending-imports/${pendingImport.id}/accept`);
      onSuccess();
    } catch (error: any) {
      console.error('Failed to import:', error);
      alert(`Import failed: ${error.response?.data?.error || error.message}`);
    } finally {
      setIsLoading(false);
    }
  };

  const handleReject = async () => {
    setIsLoading(true);
    try {
      await apiClient.post(`/pending-imports/${pendingImport.id}/reject`);
      onSuccess();
    } catch (error) {
      console.error('Failed to reject:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i];
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleDateString();
  };

  const getConfidenceColor = (confidence: number) => {
    if (confidence >= 80) return 'text-green-400';
    if (confidence >= 60) return 'text-yellow-400';
    return 'text-orange-400';
  };

  return (
    <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-4xl w-full max-h-[90vh] overflow-y-auto">
        {/* Header */}
        <div className="sticky top-0 bg-gradient-to-br from-gray-900 to-black border-b border-gray-700 px-6 py-4 flex items-center justify-between">
          <div>
            <h3 className="text-xl font-bold text-white">Manual Import</h3>
            <p className="text-sm text-gray-400 mt-1">Map external download to an event</p>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-white transition-colors"
          >
            <XMarkIcon className="w-6 h-6" />
          </button>
        </div>

        <div className="p-6 space-y-6">
          {/* Download Info */}
          <div className="bg-gray-800/50 rounded-lg p-4 border border-gray-700">
            <h4 className="text-white font-medium mb-3">Download Information</h4>
            <div className="space-y-2 text-sm">
              <div className="flex justify-between">
                <span className="text-gray-400">Title:</span>
                <span className="text-white max-w-md truncate" title={pendingImport.title}>{pendingImport.title}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-400">Size:</span>
                <span className="text-white">{formatBytes(pendingImport.size)}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-400">Quality:</span>
                <span className="px-2 py-0.5 bg-purple-900/30 text-purple-400 text-xs rounded">
                  {pendingImport.quality || 'Unknown'}
                </span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-400">Protocol:</span>
                <span className="px-2 py-0.5 bg-blue-900/30 text-blue-400 text-xs rounded uppercase">
                  {pendingImport.protocol || 'Unknown'}
                </span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-400">Detected:</span>
                <span className="text-white">{formatDate(pendingImport.detected)}</span>
              </div>
            </div>
          </div>

          {/* AI Suggestion */}
          {pendingImport.suggestedEvent && (
            <div className="bg-green-900/20 border border-green-700/50 rounded-lg p-4">
              <div className="flex items-start justify-between mb-3">
                <div className="flex items-center gap-2">
                  <CheckCircleIcon className="w-5 h-5 text-green-400" />
                  <h4 className="text-white font-medium">AI Suggestion</h4>
                </div>
                <span className={`text-sm font-medium ${getConfidenceColor(pendingImport.suggestionConfidence)}`}>
                  {pendingImport.suggestionConfidence}% confidence
                </span>
              </div>
              <div className="space-y-2 text-sm">
                <div>
                  <div className="text-white font-medium">{pendingImport.suggestedEvent.title}</div>
                  <div className="text-gray-400">{pendingImport.suggestedEvent.league?.name || pendingImport.suggestedEvent.organization}</div>
                </div>
                {pendingImport.suggestedPart && (
                  <div className="text-gray-400">
                    Part: <span className="text-white">{pendingImport.suggestedPart}</span>
                  </div>
                )}
              </div>
            </div>
          )}

          {/* Manual Selection */}
          <div className="bg-gray-800/50 rounded-lg p-4 border border-gray-700">
            <div className="flex items-center justify-between mb-3">
              <h4 className="text-white font-medium">Select Event</h4>
              <button
                onClick={() => setShowAllMatches(!showAllMatches)}
                className="flex items-center gap-2 px-3 py-1 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded transition-colors"
              >
                <MagnifyingGlassIcon className="w-4 h-4" />
                {showAllMatches ? 'Hide' : 'Show'} All Matches
              </button>
            </div>

            {showAllMatches && allMatches.length > 0 && (
              <div className="mb-4 max-h-64 overflow-y-auto bg-gray-900 rounded border border-gray-700">
                {allMatches.map((match, index) => (
                  <div
                    key={index}
                    onClick={() => {
                      if (match.eventId) {
                        setSelectedEventId(match.eventId);
                        setSelectedPart(match.part);
                        setShowAllMatches(false);
                      }
                    }}
                    className={`p-3 border-b border-gray-700 last:border-b-0 cursor-pointer hover:bg-gray-800 transition-colors ${
                      selectedEventId === match.eventId ? 'bg-red-900/30' : ''
                    }`}
                  >
                    <div className="flex items-start justify-between">
                      <div>
                        <div className="text-white font-medium">{match.eventTitle}</div>
                        <div className="text-sm text-gray-400">{match.league}</div>
                        {match.part && (
                          <div className="text-sm text-gray-400">Part: {match.part}</div>
                        )}
                      </div>
                      <span className={`text-sm font-medium ${getConfidenceColor(match.confidence)}`}>
                        {match.confidence}%
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            )}

            <div className="space-y-3">
              <div>
                <label className="block text-gray-300 text-sm mb-1">Event ID</label>
                <input
                  type="number"
                  value={selectedEventId || ''}
                  onChange={(e) => setSelectedEventId(e.target.value ? parseInt(e.target.value) : null)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-600 text-white rounded-lg focus:outline-none focus:ring-2 focus:ring-red-600"
                  placeholder="Enter event ID or select from matches above"
                />
              </div>
              <div>
                <label className="block text-gray-300 text-sm mb-1">Part (Optional, for multi-part events)</label>
                <input
                  type="text"
                  value={selectedPart || ''}
                  onChange={(e) => setSelectedPart(e.target.value || undefined)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-600 text-white rounded-lg focus:outline-none focus:ring-2 focus:ring-red-600"
                  placeholder="e.g., Prelims, Main Card"
                />
              </div>
            </div>
          </div>

          {/* No Matches Warning */}
          {!pendingImport.suggestedEvent && allMatches.length === 0 && (
            <div className="bg-yellow-900/20 border border-yellow-700/50 rounded-lg p-4">
              <div className="flex items-center gap-2">
                <ExclamationTriangleIcon className="w-5 h-5 text-yellow-400" />
                <p className="text-yellow-400 text-sm">
                  No matching events found. Please manually enter an event ID or ensure the event exists in your library.
                </p>
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="sticky bottom-0 bg-gradient-to-br from-gray-900 to-black border-t border-gray-700 px-6 py-4 flex justify-end gap-3">
          <button
            onClick={handleReject}
            disabled={isLoading}
            className="px-6 py-2 bg-gray-700 hover:bg-gray-600 disabled:opacity-50 text-white rounded-lg transition-colors"
          >
            Reject
          </button>
          <button
            onClick={onClose}
            className="px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={handleAccept}
            disabled={isLoading || !selectedEventId}
            className="px-6 py-2 bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded-lg transition-colors"
          >
            {isLoading ? 'Importing...' : 'Import'}
          </button>
        </div>
      </div>
    </div>
  );
}
