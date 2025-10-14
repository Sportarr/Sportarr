import { useState } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, ArrowsUpDownIcon } from '@heroicons/react/24/outline';

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

export default function ProfilesSettings({ showAdvanced }: ProfilesSettingsProps) {
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfile[]>([
    {
      id: 1,
      name: 'HD-1080p',
      upgradeAllowed: true,
      cutoff: 'Bluray-1080p',
      minFormatScore: 0,
      cutoffFormatScore: 0,
      items: [
        { id: 'bluray-2160p', name: 'Bluray-2160p', allowed: false },
        { id: 'webdl-2160p', name: 'WEBDL-2160p', allowed: false },
        { id: 'webrip-2160p', name: 'WEBRip-2160p', allowed: false },
        { id: 'bluray-1080p', name: 'Bluray-1080p', allowed: true },
        { id: 'webdl-1080p', name: 'WEBDL-1080p', allowed: true },
        { id: 'webrip-1080p', name: 'WEBRip-1080p', allowed: true },
        { id: 'bluray-720p', name: 'Bluray-720p', allowed: true },
        { id: 'webdl-720p', name: 'WEBDL-720p', allowed: true },
        { id: 'webrip-720p', name: 'WEBRip-720p', allowed: true },
      ],
    },
    {
      id: 2,
      name: 'Ultra-HD',
      upgradeAllowed: true,
      cutoff: 'WEBDL-2160p',
      minFormatScore: 0,
      cutoffFormatScore: 10000,
      items: [
        { id: 'bluray-2160p', name: 'Bluray-2160p', allowed: true },
        { id: 'webdl-2160p', name: 'WEBDL-2160p', allowed: true },
        { id: 'webrip-2160p', name: 'WEBRip-2160p', allowed: true },
        { id: 'bluray-1080p', name: 'Bluray-1080p', allowed: true },
        { id: 'webdl-1080p', name: 'WEBDL-1080p', allowed: true },
        { id: 'webrip-1080p', name: 'WEBRip-1080p', allowed: true },
      ],
    },
    {
      id: 3,
      name: 'Any',
      upgradeAllowed: false,
      cutoff: 'WEBDL-1080p',
      minFormatScore: 0,
      cutoffFormatScore: 0,
      items: [
        { id: 'bluray-2160p', name: 'Bluray-2160p', allowed: true },
        { id: 'webdl-2160p', name: 'WEBDL-2160p', allowed: true },
        { id: 'webrip-2160p', name: 'WEBRip-2160p', allowed: true },
        { id: 'bluray-1080p', name: 'Bluray-1080p', allowed: true },
        { id: 'webdl-1080p', name: 'WEBDL-1080p', allowed: true },
        { id: 'webrip-1080p', name: 'WEBRip-1080p', allowed: true },
        { id: 'bluray-720p', name: 'Bluray-720p', allowed: true },
        { id: 'webdl-720p', name: 'WEBDL-720p', allowed: true },
        { id: 'webrip-720p', name: 'WEBRip-720p', allowed: true },
        { id: 'bluray-480p', name: 'Bluray-480p', allowed: true },
        { id: 'webdl-480p', name: 'WEBDL-480p', allowed: true },
        { id: 'dvd', name: 'DVD', allowed: true },
      ],
    },
  ]);

  const [languageProfiles, setLanguageProfiles] = useState<LanguageProfile[]>([
    {
      id: 1,
      name: 'English',
      upgradeAllowed: false,
      cutoff: 'English',
      languages: [
        { id: 'english', name: 'English', allowed: true },
      ],
    },
    {
      id: 2,
      name: 'Any',
      upgradeAllowed: false,
      cutoff: 'English',
      languages: [
        { id: 'english', name: 'English', allowed: true },
        { id: 'spanish', name: 'Spanish', allowed: true },
        { id: 'portuguese', name: 'Portuguese', allowed: true },
        { id: 'japanese', name: 'Japanese', allowed: true },
        { id: 'korean', name: 'Korean', allowed: true },
        { id: 'thai', name: 'Thai', allowed: true },
        { id: 'russian', name: 'Russian', allowed: true },
      ],
    },
  ]);

  const [editingProfile, setEditingProfile] = useState<QualityProfile | null>(null);

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
          <button className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors">
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
                    onClick={() => setEditingProfile(profile)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                  >
                    <PencilIcon className="w-5 h-5" />
                  </button>
                  {profile.id > 3 && (
                    <button className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors">
                      <TrashIcon className="w-5 h-5" />
                    </button>
                  )}
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
          <button className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors">
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
                  <button className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors">
                    <PencilIcon className="w-5 h-5" />
                  </button>
                  {profile.id > 2 && (
                    <button className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors">
                      <TrashIcon className="w-5 h-5" />
                    </button>
                  )}
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
    </div>
  );
}
