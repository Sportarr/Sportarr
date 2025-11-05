import { useState, useEffect } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, XMarkIcon, Bars3Icon } from '@heroicons/react/24/outline';

interface ProfilesSettingsProps {
  showAdvanced: boolean;
}

interface QualityProfile {
  id?: number;
  name: string;
  isDefault: boolean;
  upgradesAllowed: boolean;
  cutoffQuality?: number | null;
  items: QualityItem[];
  formatItems: ProfileFormatItem[];
  minFormatScore?: number | null;
  cutoffFormatScore?: number | null;
  formatScoreIncrement: number;
  minSize?: number | null;
  maxSize?: number | null;
}

interface QualityItem {
  name: string;
  quality: number;
  allowed: boolean;
}

interface ProfileFormatItem {
  formatId: number;
  formatName: string;
  score: number;
}

interface CustomFormat {
  id: number;
  name: string;
}

interface DelayProfile {
  id: number;
  order: number;
  preferredProtocol: string;
  usenetDelay: number;
  torrentDelay: number;
  bypassIfHighestQuality: boolean;
  bypassIfAboveCustomFormatScore: boolean;
  minimumCustomFormatScore: number;
  tags: number[];
}

interface Tag {
  id: number;
  label: string;
}

interface ReleaseProfile {
  id?: number;
  name: string;
  enabled: boolean;
  required: string;
  ignored: string;
  preferred: PreferredKeyword[];
  includePreferredWhenRenaming: boolean;
  tags: number[];
  indexerId: number[];
}

interface PreferredKeyword {
  key: string;
  value: number;
}

interface Indexer {
  id: number;
  name: string;
}

// Available quality items
const availableQualities: QualityItem[] = [
  { name: 'WEB 2160p', quality: 19, allowed: false },
  { name: 'Bluray-2160p', quality: 18, allowed: false },
  { name: 'Bluray-2160p Remux', quality: 17, allowed: false },
  { name: 'WEB 1080p', quality: 15, allowed: false },
  { name: 'Bluray-1080p', quality: 14, allowed: false },
  { name: 'Bluray-1080p Remux', quality: 13, allowed: false },
  { name: 'HDTV-2160p', quality: 12, allowed: false },
  { name: 'HDTV-1080p', quality: 11, allowed: false },
  { name: 'WEB 720p', quality: 9, allowed: false },
  { name: 'Bluray-720p', quality: 8, allowed: false },
  { name: 'Raw-HD', quality: 7, allowed: false },
  { name: 'WEB 480p', quality: 6, allowed: false },
  { name: 'Bluray-480p', quality: 5, allowed: false },
  { name: 'DVD', quality: 4, allowed: false },
  { name: 'SDTV', quality: 3, allowed: false },
  { name: 'Unknown', quality: 0, allowed: false },
];

export default function ProfilesSettings({ showAdvanced }: ProfilesSettingsProps) {
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfile[]>([]);
  const [customFormats, setCustomFormats] = useState<CustomFormat[]>([]);
  const [editingProfile, setEditingProfile] = useState<QualityProfile | null>(null);
  const [showAddModal, setShowAddModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);

  // Delay Profiles state
  const [delayProfiles, setDelayProfiles] = useState<DelayProfile[]>([]);
  const [tags, setTags] = useState<Tag[]>([]);
  const [editingDelayProfile, setEditingDelayProfile] = useState<DelayProfile | null>(null);
  const [showDelayModal, setShowDelayModal] = useState(false);
  const [showDelayDeleteConfirm, setShowDelayDeleteConfirm] = useState<number | null>(null);

  // Release Profiles state
  const [releaseProfiles, setReleaseProfiles] = useState<ReleaseProfile[]>([]);
  const [indexers, setIndexers] = useState<Indexer[]>([]);
  const [editingReleaseProfile, setEditingReleaseProfile] = useState<ReleaseProfile | null>(null);
  const [showReleaseModal, setShowReleaseModal] = useState(false);
  const [showReleaseDeleteConfirm, setShowReleaseDeleteConfirm] = useState<number | null>(null);

  // Form state
  const [formData, setFormData] = useState<Partial<QualityProfile>>({
    name: '',
    isDefault: false,
    upgradesAllowed: true,
    cutoffQuality: null,
    items: availableQualities.map(q => ({ ...q })),
    formatItems: [],
    minFormatScore: 0,
    cutoffFormatScore: 10000,
    formatScoreIncrement: 1,
    minSize: null,
    maxSize: null,
  });

  // Delay Profile form state
  const [delayFormData, setDelayFormData] = useState<DelayProfile>({
    id: 0,
    order: 1,
    preferredProtocol: 'Usenet',
    usenetDelay: 0,
    torrentDelay: 0,
    bypassIfHighestQuality: false,
    bypassIfAboveCustomFormatScore: false,
    minimumCustomFormatScore: 0,
    tags: []
  });

  // Release Profile form state
  const [releaseFormData, setReleaseFormData] = useState<ReleaseProfile>({
    name: '',
    enabled: true,
    required: '',
    ignored: '',
    preferred: [],
    includePreferredWhenRenaming: false,
    tags: [],
    indexerId: []
  });

  // Load profiles and custom formats
  useEffect(() => {
    loadProfiles();
    loadCustomFormats();
    loadDelayProfiles();
    loadTags();
    loadReleaseProfiles();
    loadIndexers();
  }, []);

  const loadProfiles = async () => {
    try {
      const response = await fetch('/api/qualityprofile');
      if (response.ok) {
        const data = await response.json();
        setQualityProfiles(data);
      }
    } catch (error) {
      console.error('Failed to load quality profiles:', error);
    } finally {
      setLoading(false);
    }
  };

  const loadCustomFormats = async () => {
    try {
      const response = await fetch('/api/customformat');
      if (response.ok) {
        const data = await response.json();
        setCustomFormats(data);
      }
    } catch (error) {
      console.error('Failed to load custom formats:', error);
    }
  };

  const loadDelayProfiles = async () => {
    try {
      const response = await fetch('/api/delayprofile');
      if (response.ok) {
        const data = await response.json();
        setDelayProfiles(data);
      }
    } catch (error) {
      console.error('Error loading delay profiles:', error);
    }
  };

  const loadTags = async () => {
    try {
      const response = await fetch('/api/tag');
      if (response.ok) {
        const data = await response.json();
        setTags(data);
      }
    } catch (error) {
      console.error('Error loading tags:', error);
    }
  };

  const loadReleaseProfiles = async () => {
    try {
      const response = await fetch('/api/releaseprofile');
      if (response.ok) {
        const data = await response.json();
        setReleaseProfiles(data);
      }
    } catch (error) {
      console.error('Error loading release profiles:', error);
    }
  };

  const loadIndexers = async () => {
    try {
      const response = await fetch('/api/indexer');
      if (response.ok) {
        const data = await response.json();
        setIndexers(data);
      }
    } catch (error) {
      console.error('Error loading indexers:', error);
    }
  };

  const handleAdd = () => {
    setEditingProfile(null);
    setFormData({
      name: '',
      isDefault: false,
      upgradesAllowed: true,
      cutoffQuality: null,
      items: availableQualities.map(q => ({ ...q })),
      formatItems: customFormats.map(f => ({ formatId: f.id, formatName: f.name, score: 0 })),
      minFormatScore: 0,
      cutoffFormatScore: 10000,
      formatScoreIncrement: 1,
      minSize: null,
      maxSize: null,
    });
    setShowAddModal(true);
  };

  const handleEdit = (profile: QualityProfile) => {
    setEditingProfile(profile);
    setFormData(profile);
    setShowAddModal(true);
  };

  const handleSave = async () => {
    if (!formData.name) return;

    try {
      const url = editingProfile ? `/api/qualityprofile/${editingProfile.id}` : '/api/qualityprofile';
      const method = editingProfile ? 'PUT' : 'POST';

      const response = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(formData),
      });

      if (response.ok) {
        await loadProfiles();
        setShowAddModal(false);
        setEditingProfile(null);
      }
    } catch (error) {
      console.error('Failed to save quality profile:', error);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      const response = await fetch(`/api/qualityprofile/${id}`, { method: 'DELETE' });
      if (response.ok) {
        await loadProfiles();
        setShowDeleteConfirm(null);
      }
    } catch (error) {
      console.error('Failed to delete quality profile:', error);
    }
  };

  const handleSetDefault = async (id: number) => {
    try {
      // First, unset all profiles as default
      const updatedProfiles = qualityProfiles.map(profile => ({
        ...profile,
        isDefault: profile.id === id
      }));

      // Update each profile
      for (const profile of updatedProfiles) {
        await fetch(`/api/qualityprofile/${profile.id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(profile),
        });
      }

      await loadProfiles();
    } catch (error) {
      console.error('Failed to set default quality profile:', error);
    }
  };

  const handleToggleQuality = (quality: number) => {
    setFormData(prev => ({
      ...prev,
      items: prev.items?.map(item =>
        item.quality === quality ? { ...item, allowed: !item.allowed } : item
      )
    }));
  };

  const handleFormatScoreChange = (formatId: number, score: number) => {
    setFormData(prev => ({
      ...prev,
      formatItems: prev.formatItems?.map(item =>
        item.formatId === formatId ? { ...item, score } : item
      )
    }));
  };

  const getQualityName = (quality: number | null | undefined) => {
    if (!quality) return 'Not Set';
    const item = availableQualities.find(q => q.quality === quality);
    return item?.name || 'Unknown';
  };

  // Delay Profile Handlers
  const handleSaveDelayProfile = async () => {
    try {
      const url = editingDelayProfile ? `/api/delayprofile/${editingDelayProfile.id}` : '/api/delayprofile';
      const method = editingDelayProfile ? 'PUT' : 'POST';

      const response = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(delayFormData),
      });

      if (response.ok) {
        await loadDelayProfiles();
        setShowDelayModal(false);
        setEditingDelayProfile(null);
        resetDelayForm();
      }
    } catch (error) {
      console.error('Error saving delay profile:', error);
    }
  };

  const handleDeleteDelayProfile = async (id: number) => {
    try {
      const response = await fetch(`/api/delayprofile/${id}`, {
        method: 'DELETE',
      });

      if (response.ok) {
        await loadDelayProfiles();
        setShowDelayDeleteConfirm(null);
      }
    } catch (error) {
      console.error('Error deleting delay profile:', error);
    }
  };

  const openEditDelayModal = (profile: DelayProfile) => {
    setEditingDelayProfile(profile);
    setDelayFormData({ ...profile });
    setShowDelayModal(true);
  };

  const openAddDelayModal = () => {
    setEditingDelayProfile(null);
    resetDelayForm();
    setShowDelayModal(true);
  };

  const resetDelayForm = () => {
    setDelayFormData({
      id: 0,
      order: delayProfiles.length + 1,
      preferredProtocol: 'Usenet',
      usenetDelay: 0,
      torrentDelay: 0,
      bypassIfHighestQuality: false,
      bypassIfAboveCustomFormatScore: false,
      minimumCustomFormatScore: 0,
      tags: []
    });
  };

  const toggleTag = (tagId: number) => {
    setDelayFormData(prev => ({
      ...prev,
      tags: prev.tags.includes(tagId)
        ? prev.tags.filter(t => t !== tagId)
        : [...prev.tags, tagId]
    }));
  };

  const getTagNames = (tagIds: number[]) => {
    if (tagIds.length === 0) return 'All Events (Default)';
    return tagIds.map(id => tags.find(t => t.id === id)?.label || 'Unknown').join(', ');
  };

  // Release Profile Handlers
  const handleSaveReleaseProfile = async () => {
    if (!releaseFormData.name) return;

    try {
      const url = editingReleaseProfile ? `/api/releaseprofile/${editingReleaseProfile.id}` : '/api/releaseprofile';
      const method = editingReleaseProfile ? 'PUT' : 'POST';

      const response = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(releaseFormData),
      });

      if (response.ok) {
        await loadReleaseProfiles();
        setShowReleaseModal(false);
        setEditingReleaseProfile(null);
        resetReleaseForm();
      }
    } catch (error) {
      console.error('Error saving release profile:', error);
    }
  };

  const handleDeleteReleaseProfile = async (id: number) => {
    try {
      const response = await fetch(`/api/releaseprofile/${id}`, {
        method: 'DELETE',
      });

      if (response.ok) {
        await loadReleaseProfiles();
        setShowReleaseDeleteConfirm(null);
      }
    } catch (error) {
      console.error('Error deleting release profile:', error);
    }
  };

  const openEditReleaseModal = (profile: ReleaseProfile) => {
    setEditingReleaseProfile(profile);
    setReleaseFormData({ ...profile });
    setShowReleaseModal(true);
  };

  const openAddReleaseModal = () => {
    setEditingReleaseProfile(null);
    resetReleaseForm();
    setShowReleaseModal(true);
  };

  const resetReleaseForm = () => {
    setReleaseFormData({
      name: '',
      enabled: true,
      required: '',
      ignored: '',
      preferred: [],
      includePreferredWhenRenaming: false,
      tags: [],
      indexerId: []
    });
  };

  const toggleReleaseTag = (tagId: number) => {
    setReleaseFormData(prev => ({
      ...prev,
      tags: prev.tags.includes(tagId)
        ? prev.tags.filter(t => t !== tagId)
        : [...prev.tags, tagId]
    }));
  };

  const toggleIndexer = (indexerId: number) => {
    setReleaseFormData(prev => ({
      ...prev,
      indexerId: prev.indexerId.includes(indexerId)
        ? prev.indexerId.filter(i => i !== indexerId)
        : [...prev.indexerId, indexerId]
    }));
  };

  const addPreferredKeyword = () => {
    setReleaseFormData(prev => ({
      ...prev,
      preferred: [...prev.preferred, { key: '', value: 0 }]
    }));
  };

  const updatePreferredKeyword = (index: number, field: 'key' | 'value', value: string | number) => {
    setReleaseFormData(prev => ({
      ...prev,
      preferred: prev.preferred.map((item, i) =>
        i === index ? { ...item, [field]: value } : item
      )
    }));
  };

  const removePreferredKeyword = (index: number) => {
    setReleaseFormData(prev => ({
      ...prev,
      preferred: prev.preferred.filter((_, i) => i !== index)
    }));
  };

  if (loading) {
    return (
      <div className="max-w-6xl mx-auto text-center py-12">
        <div className="text-gray-400">Loading profiles...</div>
      </div>
    );
  }

  return (
    <div className="max-w-6xl mx-auto">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Quality Profiles</h2>
        <p className="text-gray-400">
          Quality profiles determine which releases Fightarr will download and upgrade
        </p>
      </div>

      {/* Quality Profiles List */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h3 className="text-xl font-semibold text-white">Profiles</h3>
            <p className="text-sm text-gray-400 mt-1">
              Configure quality settings and custom format scoring
            </p>
          </div>
          <button
            onClick={handleAdd}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Profile
          </button>
        </div>

        {qualityProfiles.length === 0 ? (
          <div className="text-center py-12 text-gray-500">
            <p className="mb-2">No quality profiles configured</p>
            <p className="text-sm">Create your first profile to get started</p>
          </div>
        ) : (
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
                      {profile.isDefault && (
                        <span className="px-2 py-0.5 bg-blue-900/30 text-blue-400 text-xs rounded font-semibold">
                          ★ Default
                        </span>
                      )}
                      {profile.upgradesAllowed && (
                        <span className="px-2 py-0.5 bg-green-900/30 text-green-400 text-xs rounded">
                          Upgrades Allowed
                        </span>
                      )}
                    </div>
                    <div className="flex items-center space-x-6 text-sm text-gray-400">
                      <div>
                        <span className="text-gray-500">Cutoff:</span>{' '}
                        <span className="text-white">{getQualityName(profile.cutoffQuality)}</span>
                      </div>
                      <div>
                        <span className="text-gray-500">Qualities:</span>{' '}
                        <span className="text-white">
                          {profile.items.filter(q => q.allowed).length} enabled
                        </span>
                      </div>
                      {showAdvanced && (
                        <div>
                          <span className="text-gray-500">Format Score:</span>{' '}
                          <span className="text-white">{profile.minFormatScore} - {profile.cutoffFormatScore}</span>
                        </div>
                      )}
                    </div>
                  </div>
                  <div className="flex items-center space-x-2">
                    {!profile.isDefault && (
                      <button
                        onClick={() => handleSetDefault(profile.id!)}
                        className="px-3 py-1.5 text-sm bg-blue-600 hover:bg-blue-700 text-white rounded transition-colors"
                        title="Set as Default"
                      >
                        Set Default
                      </button>
                    )}
                    <button
                      onClick={() => handleEdit(profile)}
                      className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                      title="Edit"
                    >
                      <PencilIcon className="w-5 h-5" />
                    </button>
                    <button
                      onClick={() => setShowDeleteConfirm(profile.id!)}
                      className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                      title="Delete"
                    >
                      <TrashIcon className="w-5 h-5" />
                    </button>
                  </div>
                </div>

                {/* Quality Items */}
                <div className="mt-4 pt-4 border-t border-gray-800">
                  <div className="grid grid-cols-3 gap-2">
                    {profile.items.filter(i => i.allowed).map((item) => (
                      <div
                        key={item.quality}
                        className="px-3 py-2 rounded text-sm bg-green-950/30 text-green-400 border border-green-900/50"
                      >
                        ✓ {item.name}
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Delay Profiles Section */}
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Delay Profiles</h2>
        <p className="text-gray-400 mb-6">
          Delay profiles allow you to reduce the number of releases downloaded by adding a delay while Fightarr continues to watch for better releases
        </p>

        {/* Info Box */}
        <div className="mb-6 bg-gradient-to-br from-blue-950/30 to-blue-900/20 border border-blue-900/50 rounded-lg p-6">
          <h3 className="text-lg font-semibold text-white mb-2">How Delay Profiles Work</h3>
          <ul className="text-sm text-gray-300 space-y-2">
            <li>• Timer begins when Fightarr detects an event has a release available</li>
            <li>• During the delay period, any new releases are noted by Fightarr</li>
            <li>• When the delay timer expires, Fightarr downloads the single release which best matches your quality preferences</li>
            <li>• Timer starts from the releases uploaded time (not when Fightarr sees it)</li>
            <li>• Manual searches ignore delay profile settings</li>
          </ul>
        </div>

        {/* Delay Profiles List */}
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center justify-between mb-6">
            <h3 className="text-xl font-semibold text-white">Delay Profiles</h3>
            <button
              onClick={openAddDelayModal}
              className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
            >
              <PlusIcon className="w-4 h-4 mr-2" />
              Add Delay Profile
            </button>
          </div>

          {delayProfiles.length === 0 ? (
            <div className="text-center py-12">
              <p className="text-gray-500 mb-4">No delay profiles configured</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full">
                <thead>
                  <tr className="border-b border-gray-800">
                    <th className="text-left py-3 px-4 text-gray-400 font-medium w-12">#</th>
                    <th className="text-left py-3 px-4 text-gray-400 font-medium">Protocol</th>
                    <th className="text-left py-3 px-4 text-gray-400 font-medium">Usenet Delay</th>
                    <th className="text-left py-3 px-4 text-gray-400 font-medium">Torrent Delay</th>
                    <th className="text-left py-3 px-4 text-gray-400 font-medium">Tags</th>
                    <th className="text-right py-3 px-4 text-gray-400 font-medium">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {delayProfiles.map((profile) => (
                    <tr key={profile.id} className="border-b border-gray-800 hover:bg-gray-900/50 transition-colors">
                      <td className="py-3 px-4 text-gray-400">
                        <Bars3Icon className="w-5 h-5" />
                      </td>
                      <td className="py-3 px-4">
                        <span className={`px-2 py-1 rounded text-xs font-medium ${
                          profile.preferredProtocol === 'Usenet'
                            ? 'bg-purple-900/30 text-purple-400'
                            : 'bg-green-900/30 text-green-400'
                        }`}>
                          {profile.preferredProtocol}
                        </span>
                      </td>
                      <td className="py-3 px-4 text-white">{profile.usenetDelay} min</td>
                      <td className="py-3 px-4 text-white">{profile.torrentDelay} min</td>
                      <td className="py-3 px-4 text-gray-400">{getTagNames(profile.tags)}</td>
                      <td className="py-3 px-4">
                        <div className="flex items-center justify-end space-x-2">
                          <button
                            onClick={() => openEditDelayModal(profile)}
                            className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                            title="Edit"
                          >
                            <PencilIcon className="w-5 h-5" />
                          </button>
                          {profile.tags.length > 0 && (
                            <button
                              onClick={() => setShowDelayDeleteConfirm(profile.id)}
                              className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                              title="Delete"
                            >
                              <TrashIcon className="w-5 h-5" />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

      {/* Release Profiles Section */}
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Release Profiles</h2>
        <p className="text-gray-400 mb-6">
          Filter and score releases based on preferred or unwanted keywords using regex patterns
        </p>

        {/* Release Profiles List */}
        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center justify-between mb-6">
            <h3 className="text-xl font-semibold text-white">Release Profiles</h3>
            <button
              onClick={openAddReleaseModal}
              className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
            >
              <PlusIcon className="w-4 h-4 mr-2" />
              Add Release Profile
            </button>
          </div>

          {releaseProfiles.length === 0 ? (
            <div className="text-center py-12">
              <p className="text-gray-500 mb-4">No release profiles configured</p>
            </div>
          ) : (
            <div className="space-y-3">
              {releaseProfiles.map((profile) => (
                <div
                  key={profile.id}
                  className="group bg-black/30 border border-gray-800 hover:border-red-900/50 rounded-lg p-4 transition-all"
                >
                  <div className="flex items-center justify-between">
                    <div className="flex-1">
                      <div className="flex items-center space-x-3 mb-2">
                        <h4 className="text-lg font-semibold text-white">{profile.name}</h4>
                        {profile.enabled ? (
                          <span className="px-2 py-0.5 bg-green-900/30 text-green-400 text-xs rounded">
                            Enabled
                          </span>
                        ) : (
                          <span className="px-2 py-0.5 bg-gray-700 text-gray-400 text-xs rounded">
                            Disabled
                          </span>
                        )}
                      </div>
                      <div className="flex flex-wrap gap-4 text-sm text-gray-400">
                        {profile.required && (
                          <div>
                            <span className="text-gray-500">Required:</span>{' '}
                            <span className="text-green-400">{profile.required.split(',').length} term(s)</span>
                          </div>
                        )}
                        {profile.ignored && (
                          <div>
                            <span className="text-gray-500">Ignored:</span>{' '}
                            <span className="text-red-400">{profile.ignored.split(',').length} term(s)</span>
                          </div>
                        )}
                        {profile.preferred.length > 0 && (
                          <div>
                            <span className="text-gray-500">Preferred:</span>{' '}
                            <span className="text-purple-400">{profile.preferred.length} term(s)</span>
                          </div>
                        )}
                      </div>
                    </div>
                    <div className="flex items-center space-x-2">
                      <button
                        onClick={() => openEditReleaseModal(profile)}
                        className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                        title="Edit"
                      >
                        <PencilIcon className="w-5 h-5" />
                      </button>
                      <button
                        onClick={() => setShowReleaseDeleteConfirm(profile.id!)}
                        className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                        title="Delete"
                      >
                        <TrashIcon className="w-5 h-5" />
                      </button>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Edit/Add Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-4xl w-full my-8">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">
                {editingProfile ? `Edit ${editingProfile.name}` : 'Add Quality Profile'}
              </h3>
              <button
                onClick={() => setShowAddModal(false)}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="space-y-6 max-h-[70vh] overflow-y-auto pr-2">
              {/* Name */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Name *</label>
                <input
                  type="text"
                  value={formData.name || ''}
                  onChange={(e) => setFormData(prev => ({ ...prev, name: e.target.value }))}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="4K Quality"
                />
              </div>

              {/* Upgrades Allowed */}
              <label className="flex items-center space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={formData.upgradesAllowed || false}
                  onChange={(e) => setFormData(prev => ({ ...prev, upgradesAllowed: e.target.checked }))}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <span className="text-sm font-medium text-gray-300">
                  Upgrades Allowed (If disabled qualities will not be upgraded)
                </span>
              </label>

              {/* Quality Selection */}
              <div>
                <h4 className="text-lg font-semibold text-white mb-3">Qualities</h4>
                <p className="text-sm text-gray-400 mb-3">
                  Qualities higher in the list are more preferred. Qualities within the same group are equal. Only checked qualities are wanted.
                </p>
                <div className="grid grid-cols-2 md:grid-cols-3 gap-2 max-h-64 overflow-y-auto p-2 bg-black/30 rounded-lg">
                  {formData.items?.map((item) => (
                    <button
                      key={item.quality}
                      onClick={() => handleToggleQuality(item.quality)}
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

              {/* Upgrade Until */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Upgrade Until</label>
                <select
                  value={formData.cutoffQuality || ''}
                  onChange={(e) => setFormData(prev => ({ ...prev, cutoffQuality: parseInt(e.target.value) || null }))}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                >
                  <option value="">Select upgrade cutoff...</option>
                  {formData.items?.filter(q => q.allowed).map(q => (
                    <option key={q.quality} value={q.quality}>{q.name}</option>
                  ))}
                </select>
                <p className="text-xs text-gray-500 mt-1">
                  Once this quality is reached Fightarr will no longer download episodes
                </p>
              </div>

              {/* Custom Format Scoring */}
              <div className="space-y-4 p-4 bg-purple-950/10 border border-purple-900/30 rounded-lg">
                <h4 className="text-lg font-semibold text-white">Custom Formats</h4>
                <p className="text-sm text-gray-400">
                  Fightarr scores each release using the sum of scores for matching custom formats. If a new release would improve the score, at the same or better quality, then Fightarr will grab it.
                </p>

                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Minimum Custom Format Score
                    </label>
                    <input
                      type="number"
                      value={formData.minFormatScore || 0}
                      onChange={(e) => setFormData(prev => ({ ...prev, minFormatScore: parseInt(e.target.value) || 0 }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Minimum custom format score allowed to download
                    </p>
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Upgrade Until Custom Format Score
                    </label>
                    <input
                      type="number"
                      value={formData.cutoffFormatScore || 10000}
                      onChange={(e) => setFormData(prev => ({ ...prev, cutoffFormatScore: parseInt(e.target.value) || 10000 }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Once this custom format score is reached Fightarr will no longer grab episode releases
                    </p>
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Minimum Custom Format Score Increment
                    </label>
                    <input
                      type="number"
                      value={formData.formatScoreIncrement || 1}
                      onChange={(e) => setFormData(prev => ({ ...prev, formatScoreIncrement: parseInt(e.target.value) || 1 }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Minimum required improvement of the custom format score between existing and new releases before Fightarr considers it an upgrade
                    </p>
                  </div>
                </div>

                {/* Custom Formats List */}
                {customFormats.length > 0 ? (
                  <div className="mt-4">
                    <h5 className="text-md font-semibold text-white mb-2">Custom Format Scoring</h5>
                    <div className="max-h-64 overflow-y-auto space-y-2 p-3 bg-black/30 rounded-lg">
                      {formData.formatItems?.map((item) => (
                        <div key={item.formatId} className="flex items-center justify-between p-2 bg-gray-800/50 rounded hover:bg-gray-800 transition-colors">
                          <span className="text-white font-medium">{item.formatName}</span>
                          <div className="flex items-center space-x-2">
                            <input
                              type="number"
                              value={item.score}
                              onChange={(e) => handleFormatScoreChange(item.formatId, parseInt(e.target.value) || 0)}
                              className="w-24 px-3 py-1 bg-gray-900 border border-gray-700 rounded text-white text-center focus:outline-none focus:border-purple-600"
                              placeholder="0"
                            />
                            <span className={`text-xs px-2 py-1 rounded min-w-[60px] text-center ${
                              item.score > 0
                                ? 'bg-green-900/30 text-green-400'
                                : item.score < 0
                                  ? 'bg-red-900/30 text-red-400'
                                  : 'bg-gray-700 text-gray-400'
                            }`}>
                              {item.score > 0 ? '+' : ''}{item.score}
                            </span>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                ) : (
                  <div className="p-6 bg-black/30 rounded-lg text-center">
                    <p className="text-gray-500 mb-2">No custom formats configured</p>
                    <p className="text-sm text-gray-400">
                      Create custom formats in Settings → Custom Formats to enable scoring
                    </p>
                  </div>
                )}
              </div>
            </div>

            <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowAddModal(false)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSave}
                disabled={!formData.name}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded-lg transition-colors"
              >
                Save
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation */}
      {showDeleteConfirm !== null && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Delete Profile?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this quality profile? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDelete(showDeleteConfirm)}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delay Profile Add/Edit Modal */}
      {showDelayModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-2xl w-full">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">
                {editingDelayProfile ? 'Edit Delay Profile' : 'Add Delay Profile'}
              </h3>
              <button
                onClick={() => {
                  setShowDelayModal(false);
                  setEditingDelayProfile(null);
                }}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="space-y-6">
              {/* Preferred Protocol */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Preferred Protocol</label>
                <select
                  value={delayFormData.preferredProtocol}
                  onChange={(e) => setDelayFormData({ ...delayFormData, preferredProtocol: e.target.value })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                >
                  <option value="Usenet">Usenet</option>
                  <option value="Torrent">Torrent</option>
                </select>
              </div>

              {/* Usenet Delay */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  Usenet Delay (minutes)
                </label>
                <input
                  type="number"
                  min="0"
                  value={delayFormData.usenetDelay}
                  onChange={(e) => setDelayFormData({ ...delayFormData, usenetDelay: parseInt(e.target.value) || 0 })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                />
              </div>

              {/* Torrent Delay */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  Torrent Delay (minutes)
                </label>
                <input
                  type="number"
                  min="0"
                  value={delayFormData.torrentDelay}
                  onChange={(e) => setDelayFormData({ ...delayFormData, torrentDelay: parseInt(e.target.value) || 0 })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                />
              </div>

              {/* Bypass if Highest Quality */}
              <label className="flex items-start space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={delayFormData.bypassIfHighestQuality}
                  onChange={(e) => setDelayFormData({ ...delayFormData, bypassIfHighestQuality: e.target.checked })}
                  className="mt-1 w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <div>
                  <span className="text-sm font-medium text-gray-300">Bypass if Highest Quality</span>
                  <p className="text-xs text-gray-500 mt-1">Bypass delay when release has the highest enabled quality profile with the preferred protocol</p>
                </div>
              </label>

              {showAdvanced && (
                <>
                  {/* Bypass if Above Custom Format Score */}
                  <label className="flex items-start space-x-3 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={delayFormData.bypassIfAboveCustomFormatScore}
                      onChange={(e) => setDelayFormData({ ...delayFormData, bypassIfAboveCustomFormatScore: e.target.checked })}
                      className="mt-1 w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                    />
                    <div>
                      <span className="text-sm font-medium text-gray-300">Bypass if Above Custom Format Score</span>
                      <p className="text-xs text-gray-500 mt-1">Bypass delay when custom format score is above minimum</p>
                    </div>
                  </label>

                  {delayFormData.bypassIfAboveCustomFormatScore && (
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">
                        Minimum Custom Format Score
                      </label>
                      <input
                        type="number"
                        value={delayFormData.minimumCustomFormatScore}
                        onChange={(e) => setDelayFormData({ ...delayFormData, minimumCustomFormatScore: parseInt(e.target.value) || 0 })}
                        className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      />
                    </div>
                  )}
                </>
              )}

              {/* Tags */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  Tags (Leave empty for default profile)
                </label>
                <div className="flex flex-wrap gap-2">
                  {tags.map((tag) => (
                    <button
                      key={tag.id}
                      onClick={() => toggleTag(tag.id)}
                      className={`px-3 py-1 rounded text-sm transition-colors ${
                        delayFormData.tags.includes(tag.id)
                          ? 'bg-red-600 text-white'
                          : 'bg-gray-800 text-gray-400 hover:bg-gray-700'
                      }`}
                    >
                      {tag.label}
                    </button>
                  ))}
                  {tags.length === 0 && (
                    <p className="text-sm text-gray-500">No tags available. Create tags in the Tags settings.</p>
                  )}
                </div>
              </div>
            </div>

            <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
              <button
                onClick={() => {
                  setShowDelayModal(false);
                  setEditingDelayProfile(null);
                }}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveDelayProfile}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Save
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delay Profile Delete Confirmation Modal */}
      {showDelayDeleteConfirm !== null && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Delete Delay Profile?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this delay profile? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowDelayDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDeleteDelayProfile(showDelayDeleteConfirm)}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Release Profile Add/Edit Modal */}
      {showReleaseModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-4xl w-full my-8">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">
                {editingReleaseProfile ? 'Edit Release Profile' : 'Add Release Profile'}
              </h3>
              <button
                onClick={() => {
                  setShowReleaseModal(false);
                  setEditingReleaseProfile(null);
                }}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="space-y-6 max-h-[70vh] overflow-y-auto pr-2">
              {/* Name */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Name *</label>
                <input
                  type="text"
                  value={releaseFormData.name}
                  onChange={(e) => setReleaseFormData({ ...releaseFormData, name: e.target.value })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="HD Releases"
                />
              </div>

              {/* Enabled */}
              <label className="flex items-center space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={releaseFormData.enabled}
                  onChange={(e) => setReleaseFormData({ ...releaseFormData, enabled: e.target.checked })}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <span className="text-sm font-medium text-gray-300">
                  Enable this release profile
                </span>
              </label>

              {/* Required Keywords */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Required Keywords</label>
                <textarea
                  value={releaseFormData.required}
                  onChange={(e) => setReleaseFormData({ ...releaseFormData, required: e.target.value })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 font-mono text-sm"
                  placeholder="1080p, BluRay"
                  rows={3}
                />
                <p className="text-xs text-gray-500 mt-1">
                  Comma-separated regex patterns. Release must contain at least one of these terms.
                </p>
              </div>

              {/* Ignored Keywords */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Ignored Keywords</label>
                <textarea
                  value={releaseFormData.ignored}
                  onChange={(e) => setReleaseFormData({ ...releaseFormData, ignored: e.target.value })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 font-mono text-sm"
                  placeholder="HDCAM, CAM"
                  rows={3}
                />
                <p className="text-xs text-gray-500 mt-1">
                  Comma-separated regex patterns. Release will be rejected if it contains any of these terms.
                </p>
              </div>

              {/* Preferred Keywords */}
              <div>
                <div className="flex items-center justify-between mb-2">
                  <label className="block text-sm font-medium text-gray-300">Preferred Keywords (with scores)</label>
                  <button
                    onClick={addPreferredKeyword}
                    className="px-3 py-1 bg-purple-600 hover:bg-purple-700 text-white text-sm rounded transition-colors"
                  >
                    Add Preferred
                  </button>
                </div>
                {releaseFormData.preferred.length === 0 ? (
                  <div className="p-4 bg-black/30 rounded-lg text-center text-gray-500 text-sm">
                    No preferred keywords. Click "Add Preferred" to add scoring rules.
                  </div>
                ) : (
                  <div className="space-y-2">
                    {releaseFormData.preferred.map((pref, index) => (
                      <div key={index} className="flex items-center space-x-2">
                        <input
                          type="text"
                          value={pref.key}
                          onChange={(e) => updatePreferredKeyword(index, 'key', e.target.value)}
                          className="flex-1 px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-purple-600 font-mono text-sm"
                          placeholder="REPACK|PROPER"
                        />
                        <input
                          type="number"
                          value={pref.value}
                          onChange={(e) => updatePreferredKeyword(index, 'value', parseInt(e.target.value) || 0)}
                          className="w-24 px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-purple-600 text-center"
                          placeholder="Score"
                        />
                        <button
                          onClick={() => removePreferredKeyword(index)}
                          className="p-2 text-red-400 hover:text-red-300 hover:bg-red-950/30 rounded transition-colors"
                        >
                          <TrashIcon className="w-5 h-5" />
                        </button>
                      </div>
                    ))}
                  </div>
                )}
                <p className="text-xs text-gray-500 mt-1">
                  Add score (positive or negative) to releases matching these regex patterns.
                </p>
              </div>

              {/* Include Preferred When Renaming */}
              <label className="flex items-center space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={releaseFormData.includePreferredWhenRenaming}
                  onChange={(e) => setReleaseFormData({ ...releaseFormData, includePreferredWhenRenaming: e.target.checked })}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <span className="text-sm font-medium text-gray-300">
                  Include preferred when renaming
                </span>
              </label>

              {/* Tags */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  Tags (Leave empty to apply to all)
                </label>
                <div className="flex flex-wrap gap-2">
                  {tags.map((tag) => (
                    <button
                      key={tag.id}
                      onClick={() => toggleReleaseTag(tag.id)}
                      className={`px-3 py-1 rounded text-sm transition-colors ${
                        releaseFormData.tags.includes(tag.id)
                          ? 'bg-red-600 text-white'
                          : 'bg-gray-800 text-gray-400 hover:bg-gray-700'
                      }`}
                    >
                      {tag.label}
                    </button>
                  ))}
                  {tags.length === 0 && (
                    <p className="text-sm text-gray-500">No tags available. Create tags in the Tags settings.</p>
                  )}
                </div>
              </div>

              {/* Indexers */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  Indexers (Leave empty to apply to all)
                </label>
                <div className="flex flex-wrap gap-2">
                  {indexers.map((indexer) => (
                    <button
                      key={indexer.id}
                      onClick={() => toggleIndexer(indexer.id)}
                      className={`px-3 py-1 rounded text-sm transition-colors ${
                        releaseFormData.indexerId.includes(indexer.id)
                          ? 'bg-blue-600 text-white'
                          : 'bg-gray-800 text-gray-400 hover:bg-gray-700'
                      }`}
                    >
                      {indexer.name}
                    </button>
                  ))}
                  {indexers.length === 0 && (
                    <p className="text-sm text-gray-500">No indexers configured. Add indexers in the Indexers settings.</p>
                  )}
                </div>
              </div>
            </div>

            <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
              <button
                onClick={() => {
                  setShowReleaseModal(false);
                  setEditingReleaseProfile(null);
                }}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveReleaseProfile}
                disabled={!releaseFormData.name}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded-lg transition-colors"
              >
                Save
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Release Profile Delete Confirmation Modal */}
      {showReleaseDeleteConfirm !== null && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Delete Release Profile?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this release profile? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowReleaseDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDeleteReleaseProfile(showReleaseDeleteConfirm)}
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
