import { useState, useEffect, useRef } from 'react';
import { DocumentTextIcon, PhotoIcon } from '@heroicons/react/24/outline';
import { apiGet, apiPut } from '../../utils/api';
import SettingsHeader from '../../components/SettingsHeader';
import TagSelector from '../../components/TagSelector';
import { useUnsavedChanges } from '../../hooks/useUnsavedChanges';

interface MetadataProvider {
  id: number;
  name: string;
  type: number; // 0 = Kodi, the only type MetadataWriterService implements today
  enabled: boolean;
  eventNfo: boolean;
  eventCardNfo: boolean;
  showNfo: boolean;
  eventImages: boolean;
  playerImages: boolean;
  leagueLogos: boolean;
  eventPosterFilename: string;
  eventFanartFilename: string;
  useEventFolder: boolean;
  imageQuality: number;
  tags: number[];
}

export default function MetadataProvidersSettings() {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const [provider, setProvider] = useState<MetadataProvider | null>(null);
  const initialProvider = useRef<MetadataProvider | null>(null);
  useUnsavedChanges(hasUnsavedChanges);

  useEffect(() => {
    loadProvider();
  }, []);

  const loadProvider = async () => {
    try {
      const response = await apiGet('/api/metadata');
      if (response.ok) {
        const providers: MetadataProvider[] = await response.json();
        // Kodi is the only type this ships a writer for - the seeded row
        // (type 0) is the one this page manages.
        const kodi = providers.find(p => p.type === 0) ?? null;
        setProvider(kodi);
        initialProvider.current = kodi ? { ...kodi } : null;
      }
    } catch (error) {
      console.error('Failed to load metadata provider:', error);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (!initialProvider.current || !provider) return;
    setHasUnsavedChanges(JSON.stringify(provider) !== JSON.stringify(initialProvider.current));
  }, [provider]);

  const handleSave = async () => {
    if (!provider) return;
    setSaving(true);
    try {
      const response = await apiPut(`/api/metadata/${provider.id}`, provider);
      if (response.ok) {
        initialProvider.current = { ...provider };
        setHasUnsavedChanges(false);
      } else {
        console.error('Failed to save metadata provider');
      }
    } catch (error) {
      console.error('Failed to save metadata provider:', error);
    } finally {
      setSaving(false);
    }
  };

  const update = <K extends keyof MetadataProvider>(key: K, value: MetadataProvider[K]) => {
    setProvider(prev => (prev ? { ...prev, [key]: value } : prev));
  };

  if (loading) {
    return (
      <div className="max-w-4xl mx-auto">
        <div className="mb-8">
          <h2 className="text-3xl font-bold text-white mb-2">Local Metadata</h2>
          <p className="text-gray-400">NFO files and artwork for Kodi</p>
        </div>
        <div className="text-center py-12">
          <p className="text-gray-500">Loading...</p>
        </div>
      </div>
    );
  }

  if (!provider) {
    return (
      <div className="max-w-4xl mx-auto px-6">
        <div className="mb-8">
          <h2 className="text-3xl font-bold text-white mb-2">Local Metadata</h2>
          <p className="text-gray-400">NFO files and artwork for Kodi</p>
        </div>
        <p className="text-gray-500">Could not load the metadata provider.</p>
      </div>
    );
  }

  return (
    <div>
      <SettingsHeader
        title="Local Metadata"
        subtitle="NFO files and artwork for Kodi - Kodi reads these directly, no plugin needed"
        onSave={handleSave}
        isSaving={saving}
        hasUnsavedChanges={hasUnsavedChanges}
        saveButtonText="Save Changes"
      />

      <div className="max-w-4xl mx-auto px-6">
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <label className="flex items-center space-x-3 cursor-pointer p-3 bg-black/30 rounded-lg hover:bg-black/50 transition-colors">
            <input
              type="checkbox"
              checked={provider.enabled}
              onChange={(e) => update('enabled', e.target.checked)}
              className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-sm font-medium text-white">Enabled</span>
              <p className="text-xs text-gray-500">
                Write .nfo files and artwork alongside imported videos whenever this is on. For Kodi rescanning
                after an import, see the Kodi connection under Notifications.
              </p>
            </div>
          </label>
          <div className="mt-4">
            <TagSelector
              selectedTags={provider.tags}
              onChange={(tags) => update('tags', tags)}
              helpText="Only apply to leagues with a matching tag. Leave empty to apply to every league."
            />
          </div>
        </div>

        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center mb-4">
            <DocumentTextIcon className="w-6 h-6 text-red-400 mr-3" />
            <h3 className="text-xl font-semibold text-white">NFO Files</h3>
          </div>
          <div className="space-y-3">
            <label className="flex items-center space-x-3 cursor-pointer p-3 bg-black/30 rounded-lg hover:bg-black/50 transition-colors">
              <input
                type="checkbox"
                checked={provider.eventNfo}
                onChange={(e) => update('eventNfo', e.target.checked)}
                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-sm font-medium text-gray-300">Episode NFO</span>
                <p className="text-xs text-gray-500">Write an NFO for each imported video, named to match the video file</p>
              </div>
            </label>

            <label className="flex items-center space-x-3 cursor-pointer p-3 bg-black/30 rounded-lg hover:bg-black/50 transition-colors">
              <input
                type="checkbox"
                checked={provider.showNfo}
                onChange={(e) => update('showNfo', e.target.checked)}
                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-sm font-medium text-gray-300">Show NFO</span>
                <p className="text-xs text-gray-500">Write a tvshow.nfo at each league's root folder</p>
              </div>
            </label>
          </div>
        </div>

        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center mb-4">
            <PhotoIcon className="w-6 h-6 text-red-400 mr-3" />
            <h3 className="text-xl font-semibold text-white">Images</h3>
          </div>
          <div className="space-y-3">
            <label className="flex items-center space-x-3 cursor-pointer p-3 bg-black/30 rounded-lg hover:bg-black/50 transition-colors">
              <input
                type="checkbox"
                checked={provider.eventImages}
                onChange={(e) => update('eventImages', e.target.checked)}
                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-sm font-medium text-gray-300">Episode Thumbnails</span>
                <p className="text-xs text-gray-500">Download a thumbnail image alongside each imported video</p>
              </div>
            </label>

            <label className="flex items-center space-x-3 cursor-pointer p-3 bg-black/30 rounded-lg hover:bg-black/50 transition-colors">
              <input
                type="checkbox"
                checked={provider.leagueLogos}
                onChange={(e) => update('leagueLogos', e.target.checked)}
                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-sm font-medium text-gray-300">League Poster &amp; Banner</span>
                <p className="text-xs text-gray-500">Download poster and banner art to each league's root folder</p>
              </div>
            </label>
          </div>

          <div className="mt-4 p-3 bg-blue-950/30 border border-blue-900/50 rounded-lg">
            <p className="text-sm text-blue-300">
              Filenames below follow Kodi's own naming convention - changing them isn't recommended, Kodi's local
              scraper looks for these exact names.
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mt-4">
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">Poster Filename</label>
              <input
                type="text"
                value={provider.eventPosterFilename}
                onChange={(e) => update('eventPosterFilename', e.target.value)}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">Banner Filename</label>
              <input
                type="text"
                value={provider.eventFanartFilename}
                onChange={(e) => update('eventFanartFilename', e.target.value)}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
