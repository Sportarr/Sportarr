import React, { useState, useEffect } from 'react';
import { toast } from 'sonner';
import { XMarkIcon } from '@heroicons/react/24/outline';
import type { Event } from '../types';

interface QualityProfile {
  id: number;
  name: string;
}

interface Tag {
  id: number;
  label: string;
}

interface BulkEditModalProps {
  isOpen: boolean;
  onClose: () => void;
  selectedEvents: Event[];
  onSaveSuccess: () => void;
}

const BulkEditModal: React.FC<BulkEditModalProps> = ({
  isOpen,
  onClose,
  selectedEvents,
  onSaveSuccess,
}) => {
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfile[]>([]);
  const [tags, setTags] = useState<Tag[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  // Edit options
  const [changeMonitored, setChangeMonitored] = useState(false);
  const [newMonitored, setNewMonitored] = useState(false);
  const [changeQualityProfile, setChangeQualityProfile] = useState(false);
  const [newQualityProfileId, setNewQualityProfileId] = useState<number>(0);
  const [changeTags, setChangeTags] = useState(false);
  const [tagsAction, setTagsAction] = useState<'add' | 'remove' | 'replace'>('add');
  const [selectedTags, setSelectedTags] = useState<number[]>([]);
  const [changeFightCards, setChangeFightCards] = useState(false);
  const [fightCardSettings, setFightCardSettings] = useState({
    'Early Prelims': true,
    'Prelims': true,
    'Main Card': true,
  });

  useEffect(() => {
    if (isOpen) {
      loadData();
    }
  }, [isOpen]);

  const loadData = async () => {
    try {
      const [profilesRes, tagsRes] = await Promise.all([
        fetch('/api/qualityprofile'),
        fetch('/api/tag')
      ]);

      if (profilesRes.ok) {
        const profilesData = await profilesRes.json();
        setQualityProfiles(profilesData);
        if (profilesData.length > 0) {
          setNewQualityProfileId(profilesData[0].id);
        }
      }

      if (tagsRes.ok) {
        const tagsData = await tagsRes.json();
        setTags(tagsData);
      }
    } catch (error) {
      console.error('Error loading data:', error);
    } finally {
      setLoading(false);
    }
  };

  const toggleTag = (tagId: number) => {
    if (selectedTags.includes(tagId)) {
      setSelectedTags(selectedTags.filter(id => id !== tagId));
    } else {
      setSelectedTags([...selectedTags, tagId]);
    }
  };

  const toggleFightCard = (cardType: string) => {
    setFightCardSettings(prev => ({
      ...prev,
      [cardType]: !prev[cardType as keyof typeof prev]
    }));
  };

  const handleSave = async () => {
    if (!changeMonitored && !changeQualityProfile && !changeTags && !changeFightCards) {
      toast.info('No Changes Selected', {
        description: 'Please select at least one property to change.',
      });
      return;
    }

    setSaving(true);

    try {
      for (const event of selectedEvents) {
        const updates: Partial<Event> = {};

        if (changeMonitored) {
          updates.monitored = newMonitored;
        }

        if (changeQualityProfile) {
          updates.qualityProfileId = newQualityProfileId;
        }

        if (changeTags) {
          let newTags = [...(event.tags || [])];

          if (tagsAction === 'add') {
            // Add tags that aren't already present
            selectedTags.forEach(tagId => {
              if (!newTags.includes(tagId)) {
                newTags.push(tagId);
              }
            });
          } else if (tagsAction === 'remove') {
            // Remove selected tags
            newTags = newTags.filter(tagId => !selectedTags.includes(tagId));
          } else if (tagsAction === 'replace') {
            // Replace all tags with selected tags
            newTags = [...selectedTags];
          }

          updates.tags = newTags;
        }

        // Update event
        const response = await fetch(`/api/event/${event.id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ...event, ...updates })
        });

        if (!response.ok) {
          throw new Error(`Failed to update event: ${event.title}`);
        }

        // Update fight cards if enabled
        if (changeFightCards && event.fightCards && event.fightCards.length > 0) {
          for (const card of event.fightCards) {
            const shouldMonitor = fightCardSettings[card.cardType as keyof typeof fightCardSettings];

            // Only update if the monitoring status needs to change
            if (card.monitored !== shouldMonitor) {
              const cardResponse = await fetch(`/api/fightcards/${card.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ monitored: shouldMonitor })
              });

              if (!cardResponse.ok) {
                console.error(`Failed to update fight card ${card.cardType} for event: ${event.title}`);
              }
            }
          }
        }
      }

      toast.success('Events Updated', {
        description: `Successfully updated ${selectedEvents.length} event${selectedEvents.length !== 1 ? 's' : ''}.`,
      });

      // Reset form
      setChangeMonitored(false);
      setChangeQualityProfile(false);
      setChangeTags(false);
      setSelectedTags([]);
      setChangeFightCards(false);
      setFightCardSettings({
        'Early Prelims': true,
        'Prelims': true,
        'Main Card': true,
      });

      onSaveSuccess();
      onClose();
    } catch (error) {
      console.error('Error saving changes:', error);
      toast.error('Update Failed', {
        description: 'Error saving changes. Some updates may have failed.',
      });
    } finally {
      setSaving(false);
    }
  };

  const handleCancel = () => {
    // Reset form
    setChangeMonitored(false);
    setChangeQualityProfile(false);
    setChangeTags(false);
    setSelectedTags([]);
    setChangeFightCards(false);
    setFightCardSettings({
      'Early Prelims': true,
      'Prelims': true,
      'Main Card': true,
    });
    onClose();
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4">
      <div className="bg-gray-900 rounded-lg shadow-2xl border border-red-900/30 max-w-2xl w-full max-h-[90vh] overflow-y-auto">
        {/* Header */}
        <div className="sticky top-0 bg-gray-900 border-b border-red-900/30 px-6 py-4 flex items-center justify-between z-10">
          <h2 className="text-2xl font-bold text-white">Edit Selected Events</h2>
          <button
            onClick={handleCancel}
            className="text-gray-400 hover:text-white transition-colors"
          >
            <XMarkIcon className="w-6 h-6" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6">
          {loading ? (
            <div className="flex items-center justify-center py-8">
              <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
            </div>
          ) : (
            <div className="space-y-6">
              {/* Selected Count */}
              <div className="bg-gray-800 border border-gray-700 rounded-lg p-4">
                <p className="text-gray-300">
                  <span className="font-semibold text-white">{selectedEvents.length}</span> event{selectedEvents.length !== 1 ? 's' : ''} selected
                </p>
              </div>

              {/* Monitored */}
              <div className="bg-gray-800 border border-gray-700 rounded-lg p-4">
                <div className="flex items-start space-x-3">
                  <input
                    type="checkbox"
                    id="changeMonitored"
                    checked={changeMonitored}
                    onChange={(e) => setChangeMonitored(e.target.checked)}
                    className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
                  />
                  <div className="flex-1">
                    <label htmlFor="changeMonitored" className="block text-sm font-semibold text-white mb-3">
                      Monitored
                    </label>
                    <select
                      value={newMonitored ? 'true' : 'false'}
                      onChange={(e) => setNewMonitored(e.target.value === 'true')}
                      disabled={!changeMonitored}
                      className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
                    >
                      <option value="true">Monitored</option>
                      <option value="false">Unmonitored</option>
                    </select>
                  </div>
                </div>
              </div>

              {/* Quality Profile */}
              <div className="bg-gray-800 border border-gray-700 rounded-lg p-4">
                <div className="flex items-start space-x-3">
                  <input
                    type="checkbox"
                    id="changeQualityProfile"
                    checked={changeQualityProfile}
                    onChange={(e) => setChangeQualityProfile(e.target.checked)}
                    className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
                  />
                  <div className="flex-1">
                    <label htmlFor="changeQualityProfile" className="block text-sm font-semibold text-white mb-3">
                      Quality Profile
                    </label>
                    <select
                      value={newQualityProfileId}
                      onChange={(e) => setNewQualityProfileId(parseInt(e.target.value))}
                      disabled={!changeQualityProfile}
                      className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
                    >
                      {qualityProfiles.map((profile) => (
                        <option key={profile.id} value={profile.id}>
                          {profile.name}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
              </div>

              {/* Tags */}
              <div className="bg-gray-800 border border-gray-700 rounded-lg p-4">
                <div className="flex items-start space-x-3">
                  <input
                    type="checkbox"
                    id="changeTags"
                    checked={changeTags}
                    onChange={(e) => setChangeTags(e.target.checked)}
                    className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
                  />
                  <div className="flex-1">
                    <label htmlFor="changeTags" className="block text-sm font-semibold text-white mb-3">
                      Tags
                    </label>

                    <select
                      value={tagsAction}
                      onChange={(e) => setTagsAction(e.target.value as any)}
                      disabled={!changeTags}
                      className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50 mb-4"
                    >
                      <option value="add">Add Tags</option>
                      <option value="remove">Remove Tags</option>
                      <option value="replace">Replace All Tags</option>
                    </select>

                    <div className="flex flex-wrap gap-2">
                      {tags.map((tag) => (
                        <button
                          key={tag.id}
                          onClick={() => toggleTag(tag.id)}
                          disabled={!changeTags}
                          className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                            selectedTags.includes(tag.id)
                              ? 'bg-red-600 text-white'
                              : 'bg-gray-700 text-gray-300 hover:bg-gray-600'
                          } disabled:opacity-50 disabled:cursor-not-allowed`}
                        >
                          {tag.label}
                        </button>
                      ))}
                      {tags.length === 0 && (
                        <span className="text-sm text-gray-500">No tags available</span>
                      )}
                    </div>
                  </div>
                </div>
              </div>

              {/* Fight Cards */}
              <div className="bg-gray-800 border border-gray-700 rounded-lg p-4">
                <div className="flex items-start space-x-3">
                  <input
                    type="checkbox"
                    id="changeFightCards"
                    checked={changeFightCards}
                    onChange={(e) => setChangeFightCards(e.target.checked)}
                    className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
                  />
                  <div className="flex-1">
                    <label htmlFor="changeFightCards" className="block text-sm font-semibold text-white mb-3">
                      Monitor Fight Cards
                    </label>
                    <p className="text-xs text-gray-400 mb-4">
                      Choose which portions of each event to monitor for automatic downloads
                    </p>

                    <div className="space-y-3">
                      {/* Early Prelims */}
                      <div className="flex items-center justify-between bg-gray-700/50 rounded-lg p-3">
                        <div>
                          <p className="text-sm font-medium text-white">Early Prelims</p>
                          <p className="text-xs text-gray-400">First fights of the event</p>
                        </div>
                        <button
                          onClick={() => toggleFightCard('Early Prelims')}
                          disabled={!changeFightCards}
                          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${
                            fightCardSettings['Early Prelims'] ? 'bg-red-600' : 'bg-gray-600'
                          }`}
                        >
                          <span
                            className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                              fightCardSettings['Early Prelims'] ? 'translate-x-6' : 'translate-x-1'
                            }`}
                          />
                        </button>
                      </div>

                      {/* Prelims */}
                      <div className="flex items-center justify-between bg-gray-700/50 rounded-lg p-3">
                        <div>
                          <p className="text-sm font-medium text-white">Prelims</p>
                          <p className="text-xs text-gray-400">Preliminary card fights</p>
                        </div>
                        <button
                          onClick={() => toggleFightCard('Prelims')}
                          disabled={!changeFightCards}
                          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${
                            fightCardSettings['Prelims'] ? 'bg-red-600' : 'bg-gray-600'
                          }`}
                        >
                          <span
                            className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                              fightCardSettings['Prelims'] ? 'translate-x-6' : 'translate-x-1'
                            }`}
                          />
                        </button>
                      </div>

                      {/* Main Card */}
                      <div className="flex items-center justify-between bg-gray-700/50 rounded-lg p-3">
                        <div>
                          <p className="text-sm font-medium text-white">Main Card</p>
                          <p className="text-xs text-gray-400">Main event and featured fights</p>
                        </div>
                        <button
                          onClick={() => toggleFightCard('Main Card')}
                          disabled={!changeFightCards}
                          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${
                            fightCardSettings['Main Card'] ? 'bg-red-600' : 'bg-gray-600'
                          }`}
                        >
                          <span
                            className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                              fightCardSettings['Main Card'] ? 'translate-x-6' : 'translate-x-1'
                            }`}
                          />
                        </button>
                      </div>
                    </div>

                    <div className="mt-4 p-3 bg-blue-900/20 border border-blue-700/30 rounded-lg">
                      <p className="text-xs text-blue-300">
                        💡 <span className="font-semibold">Tip:</span> Toggle these settings to monitor only specific portions of events. For example, uncheck Early Prelims to only download Main Card and Prelims.
                      </p>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="sticky bottom-0 bg-gray-900 border-t border-red-900/30 px-6 py-4 flex items-center justify-end gap-3">
          <button
            onClick={handleCancel}
            disabled={saving}
            className="px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            disabled={saving || selectedEvents.length === 0}
            className="px-6 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg font-semibold transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {saving ? 'Saving...' : 'Apply Changes'}
          </button>
        </div>
      </div>
    </div>
  );
};

export default BulkEditModal;
