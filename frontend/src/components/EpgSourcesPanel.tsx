import { useEffect, useState } from 'react';
import { toast } from 'sonner';
import { PlusIcon, ArrowPathIcon } from '@heroicons/react/24/outline';
import apiClient from '../api/client';
import { formatDateInTimezone } from '../utils/timezone';
import { useUISettings } from '../hooks/useUISettings';

interface EpgSource {
  id: number;
  name: string;
  url: string;
  isActive: boolean;
  priority: number;
  lastUpdated?: string;
  lastError?: string;
  programCount: number;
}

interface EpgSourcesPanelProps {
  /// Fired after any change that affects guide data (add, delete, sync,
  /// priority) so host pages can refresh their own views.
  onSourcesChanged?: () => void;
}

/// Shared XMLTV EPG source manager. Rendered both on the TV Guide page
/// (behind the cogwheel) and in Settings > IPTV, so EPG setup is
/// discoverable next to the IPTV sources it belongs with.
export default function EpgSourcesPanel({ onSourcesChanged }: EpgSourcesPanelProps) {
  const { timezone } = useUISettings();
  const [sources, setSources] = useState<EpgSource[]>([]);
  const [newName, setNewName] = useState('');
  const [newUrl, setNewUrl] = useState('');
  const [newPriority, setNewPriority] = useState(25);
  const [syncingAll, setSyncingAll] = useState(false);

  useEffect(() => {
    loadSources();
  }, []);

  const loadSources = async () => {
    try {
      const response = await apiClient.get<EpgSource[]>('/epg/sources');
      setSources(response.data);
    } catch (error) {
      console.error('Failed to load EPG sources:', error);
    }
  };

  const notifyChanged = () => {
    onSourcesChanged?.();
  };

  const addSource = async () => {
    if (!newUrl.trim() || !newName.trim()) {
      toast.error('Please enter both name and URL');
      return;
    }
    try {
      await apiClient.post('/epg/sources', {
        name: newName.trim(),
        url: newUrl.trim(),
        priority: newPriority,
      });
      toast.success('EPG source added');
      setNewName('');
      setNewUrl('');
      setNewPriority(25);
      await loadSources();
      notifyChanged();
    } catch (error) {
      console.error('Failed to add EPG source:', error);
      toast.error('Failed to add EPG source');
    }
  };

  const deleteSource = async (id: number) => {
    try {
      await apiClient.delete(`/epg/sources/${id}`);
      toast.success('EPG source deleted');
      await loadSources();
      notifyChanged();
    } catch (error) {
      console.error('Failed to delete EPG source:', error);
      toast.error('Failed to delete EPG source');
    }
  };

  const syncSource = async (id: number) => {
    try {
      await apiClient.post(`/epg/sources/${id}/sync`);
      toast.success('Syncing...');
      await loadSources();
      notifyChanged();
    } catch (error) {
      console.error('Failed to sync EPG source:', error);
      toast.error('Failed to sync EPG source');
    }
  };

  const syncAll = async () => {
    setSyncingAll(true);
    try {
      await apiClient.post('/epg/sync-all');
      toast.success('EPG sync started');
      await loadSources();
      notifyChanged();
    } catch (error) {
      console.error('Failed to sync EPG sources:', error);
      toast.error('Failed to sync EPG sources');
    } finally {
      setSyncingAll(false);
    }
  };

  const updatePriority = async (source: EpgSource, priority: number) => {
    if (!Number.isFinite(priority) || priority < 1 || priority > 50) return;
    try {
      await apiClient.put(`/epg/sources/${source.id}`, {
        name: source.name,
        url: source.url,
        isActive: source.isActive,
        priority,
      });
      await loadSources();
      notifyChanged();
    } catch (error) {
      console.error('Failed to update EPG source priority:', error);
      toast.error('Failed to update priority');
    }
  };

  return (
    <div>
      {/* Add new source */}
      <div className="flex gap-2 mb-4">
        <input
          type="text"
          placeholder="Source name"
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          className="flex-1 max-w-xs px-3 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white placeholder-gray-400 focus:border-red-500 focus:ring-1 focus:ring-red-500"
        />
        <input
          type="text"
          placeholder="XMLTV URL (http://... or .xml.gz)"
          value={newUrl}
          onChange={(e) => setNewUrl(e.target.value)}
          className="flex-[2] px-3 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white placeholder-gray-400 focus:border-red-500 focus:ring-1 focus:ring-red-500"
        />
        <input
          type="number"
          min={1}
          max={50}
          title="Priority (1-50, lower wins when sources overlap)"
          value={newPriority}
          onChange={(e) => setNewPriority(parseInt(e.target.value, 10) || 25)}
          className="w-20 px-3 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white placeholder-gray-400 focus:border-red-500 focus:ring-1 focus:ring-red-500"
        />
        <button
          onClick={addSource}
          className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          title="Add EPG source"
        >
          <PlusIcon className="w-5 h-5" />
        </button>
        {sources.length > 0 && (
          <button
            onClick={syncAll}
            disabled={syncingAll}
            className="flex items-center gap-2 px-3 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors disabled:opacity-50"
            title="Sync all EPG sources"
          >
            <ArrowPathIcon className={`w-4 h-4 ${syncingAll ? 'animate-spin' : ''}`} />
            Sync All
          </button>
        )}
      </div>

      {/* Existing sources */}
      {sources.length === 0 ? (
        <p className="text-gray-400 text-sm">
          No EPG sources configured. Add an XMLTV URL above to get program data for your channels.
        </p>
      ) : (
        <div className="space-y-2">
          {sources.map((source) => (
            <div key={source.id} className="flex items-center justify-between p-3 bg-gray-700/50 rounded-lg">
              <div>
                <div className="text-white font-medium">{source.name}</div>
                <div className="text-gray-400 text-xs truncate max-w-md">{source.url}</div>
                <div className="text-gray-500 text-xs mt-1">
                  {source.programCount} programs
                  {source.lastUpdated && ` | Updated: ${formatDateInTimezone(source.lastUpdated, timezone, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}`}
                  {source.lastError && <span className="text-red-400"> | Error: {source.lastError}</span>}
                </div>
              </div>
              <div className="flex items-center gap-2">
                <label className="flex items-center gap-1 text-xs text-gray-400" title="Priority (1-50, lower wins when sources overlap)">
                  Priority
                  <input
                    type="number"
                    min={1}
                    max={50}
                    defaultValue={source.priority}
                    onBlur={(e) => {
                      const v = parseInt(e.target.value, 10);
                      if (v !== source.priority) updatePriority(source, v);
                    }}
                    className="w-16 px-2 py-1 bg-gray-700 border border-gray-600 rounded text-white focus:border-red-500 focus:ring-1 focus:ring-red-500"
                  />
                </label>
                <button
                  onClick={() => syncSource(source.id)}
                  className="p-2 text-gray-400 hover:text-white hover:bg-gray-600 rounded transition-colors"
                  title="Sync this source"
                >
                  <ArrowPathIcon className="w-4 h-4" />
                </button>
                <button
                  onClick={() => deleteSource(source.id)}
                  className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-900/30 rounded transition-colors"
                  title="Delete"
                >
                  <span className="text-lg">&times;</span>
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
