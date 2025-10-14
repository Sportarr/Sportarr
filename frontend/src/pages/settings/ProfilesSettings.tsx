import { useState } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, ArrowsUpDownIcon, XMarkIcon } from '@heroicons/react/24/outline';

interface ProfilesSettingsProps {
  showAdvanced: boolean;
}

interface QualityProfile {
  id: number;
  name: string;
  upgradeAllowed: boolean;
  cutoff: string;
  items: QualityItem[];
  minFormatScore: number;
  cutoffFormatScore: number;
}

interface QualityItem {
  id: string;
  name: string;
  allowed: boolean;
}

interface LanguageProfile {
  id: number;
  name: string;
  upgradeAllowed: boolean;
  cutoff: string;
  languages: LanguageItem[];
}

interface LanguageItem {
  id: string;
  name: string;
  allowed: boolean;
}

// Available quality items that can be selected
const availableQualities: QualityItem[] = [
  { id: 'bluray-2160p', name: 'Bluray-2160p', allowed: false },
  { id: 'webdl-2160p', name: 'WEBDL-2160p', allowed: false },
  { id: 'webrip-2160p', name: 'WEBRip-2160p', allowed: false },
  { id: 'bluray-1080p', name: 'Bluray-1080p', allowed: false },
  { id: 'webdl-1080p', name: 'WEBDL-1080p', allowed: false },
  { id: 'webrip-1080p', name: 'WEBRip-1080p', allowed: false },
  { id: 'bluray-720p', name: 'Bluray-720p', allowed: false },
  { id: 'webdl-720p', name: 'WEBDL-720p', allowed: false },
  { id: 'webrip-720p', name: 'WEBRip-720p', allowed: false },
  { id: 'bluray-480p', name: 'Bluray-480p', allowed: false },
  { id: 'webdl-480p', name: 'WEBDL-480p', allowed: false },
  { id: 'dvd', name: 'DVD', allowed: false },
];

// Available languages
const availableLanguages: LanguageItem[] = [
  { id: 'english', name: 'English', allowed: false },
  { id: 'spanish', name: 'Spanish', allowed: false },
  { id: 'portuguese', name: 'Portuguese', allowed: false },
  { id: 'japanese', name: 'Japanese', allowed: false },
  { id: 'korean', name: 'Korean', allowed: false },
  { id: 'thai', name: 'Thai', allowed: false },
  { id: 'russian', name: 'Russian', allowed: false },
  { id: 'chinese', name: 'Chinese', allowed: false },
  { id: 'german', name: 'German', allowed: false },
  { id: 'french', name: 'French', allowed: false },
];

export default function ProfilesSettings({ showAdvanced }: ProfilesSettingsProps) {
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfile[]>([]);
  const [languageProfiles, setLanguageProfiles] = useState<LanguageProfile[]>([]);

  const [editingProfile, setEditingProfile] = useState<QualityProfile | null>(null);
  const [editingLangProfile, setEditingLangProfile] = useState<LanguageProfile | null>(null);
  const [showAddQualityModal, setShowAddQualityModal] = useState(false);
  const [showAddLangModal, setShowAddLangModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<{ type: 'quality' | 'language', id: number } | null>(null);

  // Form state for Quality Profile
  const [qualityFormData, setQualityFormData] = useState<Partial<QualityProfile>>({
    name: '',
    upgradeAllowed: true,
    cutoff: '',
    minFormatScore: 0,
    cutoffFormatScore: 0,
    items: availableQualities.map(q => ({ ...q }))
  });

  // Form state for Language Profile
  const [langFormData, setLangFormData] = useState<Partial<LanguageProfile>>({
    name: '',
    upgradeAllowed: false,
    cutoff: '',
    languages: availableLanguages.map(l => ({ ...l }))
  });

  // Quality Profile Handlers
  const handleAddQualityProfile = () => {
    setEditingProfile(null);
    setQualityFormData({
      name: '',
      upgradeAllowed: true,
      cutoff: '',
      minFormatScore: 0,
      cutoffFormatScore: 0,
      items: availableQualities.map(q => ({ ...q }))
    });
    setShowAddQualityModal(true);
  };

  const handleEditQualityProfile = (profile: QualityProfile) => {
    setEditingProfile(profile);
    setQualityFormData(profile);
    setShowAddQualityModal(true);
  };

  const handleSaveQualityProfile = () => {
    if (!qualityFormData.name || !qualityFormData.cutoff) {
      alert('Please fill in all required fields');
      return;
    }

    if (editingProfile) {
      // Update existing
      setQualityProfiles(prev =>
        prev.map(p => p.id === editingProfile.id ? { ...p, ...qualityFormData } as QualityProfile : p)
      );
    } else {
      // Add new
      const newProfile: QualityProfile = {
        id: Date.now(),
        ...qualityFormData
      } as QualityProfile;
      setQualityProfiles(prev => [...prev, newProfile]);
    }

    setShowAddQualityModal(false);
    setEditingProfile(null);
  };

  const handleDeleteQualityProfile = (id: number) => {
    setQualityProfiles(prev => prev.filter(p => p.id !== id));
    setShowDeleteConfirm(null);
  };

  const handleToggleQuality = (qualityId: string) => {
    setQualityFormData(prev => ({
      ...prev,
      items: prev.items?.map(item =>
        item.id === qualityId ? { ...item, allowed: !item.allowed } : item
      )
    }));
  };

  // Language Profile Handlers
  const handleAddLangProfile = () => {
    setEditingLangProfile(null);
    setLangFormData({
      name: '',
      upgradeAllowed: false,
      cutoff: '',
      languages: availableLanguages.map(l => ({ ...l }))
    });
    setShowAddLangModal(true);
  };

  const handleEditLangProfile = (profile: LanguageProfile) => {
    setEditingLangProfile(profile);
    setLangFormData(profile);
    setShowAddLangModal(true);
  };

  const handleSaveLangProfile = () => {
    if (!langFormData.name || !langFormData.cutoff) {
      alert('Please fill in all required fields');
      return;
    }

    if (editingLangProfile) {
      // Update existing
      setLanguageProfiles(prev =>
        prev.map(p => p.id === editingLangProfile.id ? { ...p, ...langFormData } as LanguageProfile : p)
      );
    } else {
      // Add new
      const newProfile: LanguageProfile = {
        id: Date.now(),
        ...langFormData
      } as LanguageProfile;
      setLanguageProfiles(prev => [...prev, newProfile]);
    }

    setShowAddLangModal(false);
    setEditingLangProfile(null);
  };

  const handleDeleteLangProfile = (id: number) => {
    setLanguageProfiles(prev => prev.filter(p => p.id !== id));
    setShowDeleteConfirm(null);
  };

  const handleToggleLanguage = (langId: string) => {
    setLangFormData(prev => ({
      ...prev,
      languages: prev.languages?.map(lang =>
        lang.id === langId ? { ...lang, allowed: !lang.allowed } : lang
      )
    }));
  };

  return (
    <div className="max-w-6xl">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Profiles</h2>
        <p className="text-gray-400">
          Quality and language profiles determine which releases Fightarr will download
        </p>
      </div>

      {/* Quality Profiles */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h3 className="text-xl font-semibold text-white">Quality Profiles</h3>
            <p className="text-sm text-gray-400 mt-1">
              Configure quality settings for downloads (compatible with TRaSH Guides)
            </p>
          </div>
          <button
            onClick={handleAddQualityProfile}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Profile
          </button>
        </div>

        <div className="space-y-3">
          {qualityProfiles.map((profile) => (
            <div
              key={profile.id}
              className="group bg-black/30 border border-gray-800 hover:border-red-900/50 rounded-lg p-4 transition-all"
            >
              <div className="flex items-center justify-between">
                <div className="flex-1">
                  <div className="flex items-center space-x-3 mb-2">
                    <h4 className="text-lg font-semibold text-white">{profile.name}</h4>
                    {profile.upgradeAllowed && (
                      <span className="px-2 py-0.5 bg-green-900/30 text-green-400 text-xs rounded">
                        Upgrades Allowed
                      </span>
                    )}
                  </div>
                  <div className="flex items-center space-x-6 text-sm text-gray-400">
                    <div>
                      <span className="text-gray-500">Cutoff:</span>{' '}
                      <span className="text-white">{profile.cutoff}</span>
                    </div>
                    <div>
                      <span className="text-gray-500">Qualities:</span>{' '}
                      <span className="text-white">
                        {profile.items.filter((q) => q.allowed).length} enabled
                      </span>
                    </div>
                    {showAdvanced && (
                      <div>
                        <span className="text-gray-500">Min Score:</span>{' '}
                        <span className="text-white">{profile.minFormatScore}</span>
                      </div>
                    )}
                  </div>
                </div>
                <div className="flex items-center space-x-2">
                  <button
                    onClick={() => handleEditQualityProfile(profile)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                    title="Edit"
                  >
                    <PencilIcon className="w-5 h-5" />
                  </button>
                  <button
                    onClick={() => setShowDeleteConfirm({ type: 'quality', id: profile.id })}
                    className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                    title="Delete"
                  >
                    <TrashIcon className="w-5 h-5" />
                  </button>
                </div>
              </div>

              {/* Quality Items (collapsed by default) */}
              <div className="mt-4 pt-4 border-t border-gray-800">
                <div className="grid grid-cols-3 gap-2">
                  {profile.items.map((item) => (
                    <div
                      key={item.id}
                      className={`flex items-center px-3 py-2 rounded text-sm ${
                        item.allowed
                          ? 'bg-green-950/30 text-green-400 border border-green-900/50'
                          : 'bg-gray-900/50 text-gray-500 border border-gray-800'
                      }`}
                    >
                      {item.allowed ? '✓' : '×'} {item.name}
                    </div>
                  ))}
                </div>
              </div>
            </div>
          ))}
        </div>

        <div className="mt-6 p-4 bg-blue-950/30 border border-blue-900/50 rounded-lg">
          <p className="text-sm text-blue-300">
            <strong>TRaSH Guides Compatible:</strong> Import quality profiles from TRaSH Guides to get
            optimal settings for combat sports content. Custom Formats can be configured separately.
          </p>
        </div>
      </div>

      {/* Language Profiles */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h3 className="text-xl font-semibold text-white">Language Profiles</h3>
            <p className="text-sm text-gray-400 mt-1">
              Configure language preferences for commentary and audio tracks
            </p>
          </div>
          <button
            onClick={handleAddLangProfile}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Profile
          </button>
        </div>

        <div className="space-y-3">
          {languageProfiles.map((profile) => (
            <div
              key={profile.id}
              className="group bg-black/30 border border-gray-800 hover:border-red-900/50 rounded-lg p-4 transition-all"
            >
              <div className="flex items-center justify-between mb-3">
                <div className="flex-1">
                  <h4 className="text-lg font-semibold text-white mb-1">{profile.name}</h4>
                  <div className="flex items-center space-x-6 text-sm text-gray-400">
                    <div>
                      <span className="text-gray-500">Cutoff:</span>{' '}
                      <span className="text-white">{profile.cutoff}</span>
                    </div>
                    <div>
                      <span className="text-gray-500">Languages:</span>{' '}
                      <span className="text-white">
                        {profile.languages.filter((l) => l.allowed).length} enabled
                      </span>
                    </div>
                  </div>
                </div>
                <div className="flex items-center space-x-2">
                  <button
                    onClick={() => handleEditLangProfile(profile)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                    title="Edit"
                  >
                    <PencilIcon className="w-5 h-5" />
                  </button>
                  <button
                    onClick={() => setShowDeleteConfirm({ type: 'language', id: profile.id })}
                    className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                    title="Delete"
                  >
                    <TrashIcon className="w-5 h-5" />
                  </button>
                </div>
              </div>

              <div className="flex flex-wrap gap-2">
                {profile.languages.map((lang) => (
                  <span
                    key={lang.id}
                    className={`px-3 py-1 rounded text-sm ${
                      lang.allowed
                        ? 'bg-green-950/30 text-green-400 border border-green-900/50'
                        : 'bg-gray-900/50 text-gray-500 border border-gray-800'
                    }`}
                  >
                    {lang.allowed ? '✓' : '×'} {lang.name}
                  </span>
                ))}
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Delay Profiles (Advanced) */}
      {showAdvanced && (
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center justify-between mb-6">
            <div>
              <h3 className="text-xl font-semibold text-white">
                Delay Profiles
                <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                  Advanced
                </span>
              </h3>
              <p className="text-sm text-gray-400 mt-1">
                Delay grabbing a release for a set amount of time
              </p>
            </div>
            <button className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors">
              <PlusIcon className="w-4 h-4 mr-2" />
              Add Delay Profile
            </button>
          </div>

          <div className="bg-black/30 border border-gray-800 rounded-lg p-4">
            <div className="flex items-center justify-between">
              <div>
                <h4 className="text-white font-medium mb-1">Default Delay Profile</h4>
                <p className="text-sm text-gray-400">
                  Usenet: <span className="text-white">60 minutes</span> · Torrent:{' '}
                  <span className="text-white">0 minutes</span>
                </p>
              </div>
              <button className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors">
                <PencilIcon className="w-5 h-5" />
              </button>
            </div>
          </div>

          <div className="mt-4 p-4 bg-blue-950/30 border border-blue-900/50 rounded-lg">
            <p className="text-sm text-blue-300">
              <strong>Tip:</strong> Use delay profiles to wait for better releases or preferred indexers
              before grabbing. Useful for waiting for WEB-DL instead of WEBRip releases.
            </p>
          </div>
        </div>
      )}

      {/* Release Profiles (Advanced) */}
      {showAdvanced && (
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center justify-between mb-6">
            <div>
              <h3 className="text-xl font-semibold text-white">
                Release Profiles
                <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                  Advanced
                </span>
              </h3>
              <p className="text-sm text-gray-400 mt-1">
                Fine-grained control over release names (must contain, must not contain)
              </p>
            </div>
            <button className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors">
              <PlusIcon className="w-4 h-4 mr-2" />
              Add Release Profile
            </button>
          </div>

          <div className="text-center py-8 text-gray-500">
            <p>No release profiles configured</p>
            <p className="text-sm mt-2">Create profiles to filter releases by name patterns</p>
          </div>
        </div>
      )}

      {/* Save Button */}
      <div className="flex justify-end">
        <button className="px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105">
          Save Changes
        </button>
      </div>

      {/* Quality Profile Modal */}
      {showAddQualityModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-4xl w-full my-8">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">
                {editingProfile ? `Edit ${editingProfile.name}` : 'Add Quality Profile'}
              </h3>
              <button
                onClick={() => setShowAddQualityModal(false)}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="space-y-6">
              {/* Basic Settings */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Name *</label>
                <input
                  type="text"
                  value={qualityFormData.name || ''}
                  onChange={(e) => setQualityFormData(prev => ({ ...prev, name: e.target.value }))}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="HD-1080p"
                />
              </div>

              <label className="flex items-center space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={qualityFormData.upgradeAllowed || false}
                  onChange={(e) => setQualityFormData(prev => ({ ...prev, upgradeAllowed: e.target.checked }))}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <span className="text-sm font-medium text-gray-300">Upgrade until cutoff quality is met</span>
              </label>

              {/* Quality Selection */}
              <div>
                <h4 className="text-lg font-semibold text-white mb-3">Select Qualities</h4>
                <div className="grid grid-cols-2 md:grid-cols-3 gap-2 max-h-64 overflow-y-auto p-2 bg-black/30 rounded-lg">
                  {qualityFormData.items?.map((item) => (
                    <button
                      key={item.id}
                      onClick={() => handleToggleQuality(item.id)}
                      className={`px-3 py-2 rounded text-sm text-left transition-all ${
                        item.allowed
                          ? 'bg-green-950/30 text-green-400 border border-green-900/50'
                          : 'bg-gray-900/50 text-gray-500 border border-gray-800 hover:border-gray-700'
                      }`}
                    >
                      {item.allowed ? '✓' : '○'} {item.name}
                    </button>
                  ))}
                </div>
              </div>

              {/* Cutoff */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Cutoff Quality *</label>
                <select
                  value={qualityFormData.cutoff || ''}
                  onChange={(e) => setQualityFormData(prev => ({ ...prev, cutoff: e.target.value }))}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                >
                  <option value="">Select cutoff quality...</option>
                  {qualityFormData.items?.filter(q => q.allowed).map(q => (
                    <option key={q.id} value={q.name}>{q.name}</option>
                  ))}
                </select>
                <p className="text-xs text-gray-500 mt-1">
                  Once this quality is reached, Fightarr will no longer upgrade
                </p>
              </div>

              {/* Format Scores (Advanced) */}
              {showAdvanced && (
                <div className="space-y-4 p-4 bg-yellow-950/10 border border-yellow-900/30 rounded-lg">
                  <h4 className="text-lg font-semibold text-white">Custom Format Scores</h4>
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Minimum Score</label>
                      <input
                        type="number"
                        value={qualityFormData.minFormatScore || 0}
                        onChange={(e) => setQualityFormData(prev => ({ ...prev, minFormatScore: parseInt(e.target.value) }))}
                        className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      />
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Cutoff Score</label>
                      <input
                        type="number"
                        value={qualityFormData.cutoffFormatScore || 0}
                        onChange={(e) => setQualityFormData(prev => ({ ...prev, cutoffFormatScore: parseInt(e.target.value) }))}
                        className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      />
                    </div>
                  </div>
                </div>
              )}
            </div>

            <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowAddQualityModal(false)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveQualityProfile}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Save Profile
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Language Profile Modal */}
      {showAddLangModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-4xl w-full my-8">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">
                {editingLangProfile ? `Edit ${editingLangProfile.name}` : 'Add Language Profile'}
              </h3>
              <button
                onClick={() => setShowAddLangModal(false)}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="space-y-6">
              {/* Basic Settings */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Name *</label>
                <input
                  type="text"
                  value={langFormData.name || ''}
                  onChange={(e) => setLangFormData(prev => ({ ...prev, name: e.target.value }))}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="English"
                />
              </div>

              <label className="flex items-center space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={langFormData.upgradeAllowed || false}
                  onChange={(e) => setLangFormData(prev => ({ ...prev, upgradeAllowed: e.target.checked }))}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <span className="text-sm font-medium text-gray-300">Upgrade until cutoff language is met</span>
              </label>

              {/* Language Selection */}
              <div>
                <h4 className="text-lg font-semibold text-white mb-3">Select Languages</h4>
                <div className="grid grid-cols-2 md:grid-cols-3 gap-2 p-2 bg-black/30 rounded-lg">
                  {langFormData.languages?.map((lang) => (
                    <button
                      key={lang.id}
                      onClick={() => handleToggleLanguage(lang.id)}
                      className={`px-3 py-2 rounded text-sm text-left transition-all ${
                        lang.allowed
                          ? 'bg-green-950/30 text-green-400 border border-green-900/50'
                          : 'bg-gray-900/50 text-gray-500 border border-gray-800 hover:border-gray-700'
                      }`}
                    >
                      {lang.allowed ? '✓' : '○'} {lang.name}
                    </button>
                  ))}
                </div>
              </div>

              {/* Cutoff */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Cutoff Language *</label>
                <select
                  value={langFormData.cutoff || ''}
                  onChange={(e) => setLangFormData(prev => ({ ...prev, cutoff: e.target.value }))}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                >
                  <option value="">Select cutoff language...</option>
                  {langFormData.languages?.filter(l => l.allowed).map(l => (
                    <option key={l.id} value={l.name}>{l.name}</option>
                  ))}
                </select>
                <p className="text-xs text-gray-500 mt-1">
                  Once this language is found, Fightarr will no longer upgrade
                </p>
              </div>
            </div>

            <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowAddLangModal(false)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveLangProfile}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Save Profile
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Delete Profile?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this {showDeleteConfirm.type} profile? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => {
                  if (showDeleteConfirm.type === 'quality') {
                    handleDeleteQualityProfile(showDeleteConfirm.id);
                  } else {
                    handleDeleteLangProfile(showDeleteConfirm.id);
                  }
                }}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
