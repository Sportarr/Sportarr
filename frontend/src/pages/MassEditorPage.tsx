import React, { useState, useEffect } from 'react';
import { CheckIcon, XMarkIcon, MagnifyingGlassIcon } from '@heroicons/react/24/outline';

interface Event {
  id: number;
  title: string;
  organization: string;
  eventDate: string;
  monitored: boolean;
  hasFile: boolean;
  qualityProfileId?: number;
  tags?: number[];
}

interface QualityProfile {
  id: number;
  name: string;
}

interface Tag {
  id: number;
  label: string;
}

const MassEditorPage: React.FC = () => {
  const [events, setEvents] = useState<Event[]>([]);
  const [filteredEvents, setFilteredEvents] = useState<Event[]>([]);
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfile[]>([]);
  const [tags, setTags] = useState<Tag[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  const [selectedEventIds, setSelectedEventIds] = useState<Set<number>>(new Set());
  const [searchQuery, setSearchQuery] = useState('');

  // Filter options
  const [filterMonitored, setFilterMonitored] = useState<'all' | 'monitored' | 'unmonitored'>('all');
  const [filterHasFile, setFilterHasFile] = useState<'all' | 'hasFile' | 'missing'>('all');

  // Edit options
  const [changeMonitored, setChangeMonitored] = useState(false);
  const [newMonitored, setNewMonitored] = useState(false);
  const [changeQualityProfile, setChangeQualityProfile] = useState(false);
  const [newQualityProfileId, setNewQualityProfileId] = useState<number>(0);
  const [changeTags, setChangeTags] = useState(false);
  const [tagsAction, setTagsAction] = useState<'add' | 'remove' | 'replace'>('add');
  const [selectedTags, setSelectedTags] = useState<number[]>([]);

  useEffect(() => {
    loadData();
  }, []);

  useEffect(() => {
    applyFilters();
  }, [events, searchQuery, filterMonitored, filterHasFile]);

  const loadData = async () => {
    try {
      const [eventsRes, profilesRes, tagsRes] = await Promise.all([
        fetch('/api/event'),
        fetch('/api/qualityprofile'),
        fetch('/api/tag')
      ]);

      if (eventsRes.ok) {
        const eventsData = await eventsRes.json();
        setEvents(eventsData);
      }

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

  const applyFilters = () => {
    let filtered = [...events];

    // Search filter
    if (searchQuery.trim()) {
      const query = searchQuery.toLowerCase();
      filtered = filtered.filter(e =>
        e.title.toLowerCase().includes(query) ||
        e.organization.toLowerCase().includes(query)
      );
    }

    // Monitored filter
    if (filterMonitored === 'monitored') {
      filtered = filtered.filter(e => e.monitored);
    } else if (filterMonitored === 'unmonitored') {
      filtered = filtered.filter(e => !e.monitored);
    }

    // Has file filter
    if (filterHasFile === 'hasFile') {
      filtered = filtered.filter(e => e.hasFile);
    } else if (filterHasFile === 'missing') {
      filtered = filtered.filter(e => !e.hasFile);
    }

    setFilteredEvents(filtered);
  };

  const toggleEventSelection = (eventId: number) => {
    const newSelected = new Set(selectedEventIds);
    if (newSelected.has(eventId)) {
      newSelected.delete(eventId);
    } else {
      newSelected.add(eventId);
    }
    setSelectedEventIds(newSelected);
  };

  const selectAll = () => {
    setSelectedEventIds(new Set(filteredEvents.map(e => e.id)));
  };

  const deselectAll = () => {
    setSelectedEventIds(new Set());
  };

  const handleSave = async () => {
    if (selectedEventIds.size === 0) {
      alert('Please select at least one event');
      return;
    }

    if (!changeMonitored && !changeQualityProfile && !changeTags) {
      alert('Please select at least one property to change');
      return;
    }

    setSaving(true);

    try {
      const selectedEvents = events.filter(e => selectedEventIds.has(e.id));

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
      }

      // Reload data
      await loadData();

      // Reset selections and options
      setSelectedEventIds(new Set());
      setChangeMonitored(false);
      setChangeQualityProfile(false);
      setChangeTags(false);
      setSelectedTags([]);

      alert(`Successfully updated ${selectedEvents.length} event(s)`);
    } catch (error) {
      console.error('Error saving changes:', error);
      alert('Error saving changes. Some updates may have failed.');
    } finally {
      setSaving(false);
    }
  };

  const toggleTag = (tagId: number) => {
    if (selectedTags.includes(tagId)) {
      setSelectedTags(selectedTags.filter(id => id !== tagId));
    } else {
      setSelectedTags([...selectedTags, tagId]);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-gray-400">Loading events...</div>
      </div>
    );
  }

  return (
    <div className="p-6">
      <div className="mb-6">
        <h1 className="text-3xl font-bold text-white mb-2">Mass Editor</h1>
        <p className="text-gray-400">
          Select multiple events and edit their properties in bulk
        </p>
      </div>

      {/* Filters */}
      <div className="mb-6 bg-gray-800 rounded-lg p-4 border border-gray-700">
        <h2 className="text-lg font-semibold text-white mb-4">Filters</h2>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          {/* Search */}
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">
              Search
            </label>
            <div className="relative">
              <MagnifyingGlassIcon className="absolute left-3 top-1/2 transform -translate-y-1/2 w-5 h-5 text-gray-500" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder="Search events..."
                className="w-full pl-10 pr-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
          </div>

          {/* Monitored Filter */}
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">
              Monitored
            </label>
            <select
              value={filterMonitored}
              onChange={(e) => setFilterMonitored(e.target.value as any)}
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <option value="all">All</option>
              <option value="monitored">Monitored Only</option>
              <option value="unmonitored">Unmonitored Only</option>
            </select>
          </div>

          {/* Has File Filter */}
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">
              File Status
            </label>
            <select
              value={filterHasFile}
              onChange={(e) => setFilterHasFile(e.target.value as any)}
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <option value="all">All</option>
              <option value="hasFile">Has File</option>
              <option value="missing">Missing File</option>
            </select>
          </div>
        </div>
      </div>

      {/* Selection Controls */}
      <div className="mb-4 flex items-center justify-between">
        <div className="flex items-center space-x-3">
          <button
            onClick={selectAll}
            className="px-3 py-1 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded transition-colors"
          >
            Select All
          </button>
          <button
            onClick={deselectAll}
            className="px-3 py-1 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded transition-colors"
          >
            Deselect All
          </button>
          <span className="text-sm text-gray-400">
            {selectedEventIds.size} of {filteredEvents.length} selected
          </span>
        </div>
      </div>

      {/* Events List */}
      <div className="mb-6 bg-gray-800 rounded-lg border border-gray-700 overflow-hidden">
        <div className="max-h-96 overflow-y-auto">
          <table className="w-full">
            <thead className="bg-gray-900 sticky top-0">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-400 uppercase tracking-wider w-12">
                  Select
                </th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-400 uppercase tracking-wider">
                  Event
                </th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-400 uppercase tracking-wider">
                  Organization
                </th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-400 uppercase tracking-wider">
                  Date
                </th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-400 uppercase tracking-wider">
                  Monitored
                </th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-400 uppercase tracking-wider">
                  File
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-700">
              {filteredEvents.map((event) => (
                <tr
                  key={event.id}
                  className={`hover:bg-gray-700/50 transition-colors ${
                    selectedEventIds.has(event.id) ? 'bg-blue-900/20' : ''
                  }`}
                >
                  <td className="px-4 py-3">
                    <input
                      type="checkbox"
                      checked={selectedEventIds.has(event.id)}
                      onChange={() => toggleEventSelection(event.id)}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-blue-600 focus:ring-blue-500"
                    />
                  </td>
                  <td className="px-4 py-3 text-white">{event.title}</td>
                  <td className="px-4 py-3 text-gray-400">{event.organization}</td>
                  <td className="px-4 py-3 text-gray-400">
                    {new Date(event.eventDate).toLocaleDateString()}
                  </td>
                  <td className="px-4 py-3">
                    {event.monitored ? (
                      <CheckIcon className="w-5 h-5 text-green-400" />
                    ) : (
                      <XMarkIcon className="w-5 h-5 text-gray-600" />
                    )}
                  </td>
                  <td className="px-4 py-3">
                    {event.hasFile ? (
                      <CheckIcon className="w-5 h-5 text-green-400" />
                    ) : (
                      <XMarkIcon className="w-5 h-5 text-gray-600" />
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Edit Options */}
      <div className="bg-gray-800 rounded-lg p-6 border border-gray-700">
        <h2 className="text-lg font-semibold text-white mb-4">Edit Selected Events</h2>

        <div className="space-y-4">
          {/* Monitored */}
          <div className="flex items-start space-x-4">
            <input
              type="checkbox"
              id="changeMonitored"
              checked={changeMonitored}
              onChange={(e) => setChangeMonitored(e.target.checked)}
              className="mt-1 w-4 h-4 rounded border-gray-600 bg-gray-700 text-blue-600 focus:ring-blue-500"
            />
            <div className="flex-1">
              <label htmlFor="changeMonitored" className="block text-sm font-medium text-gray-300 mb-2">
                Change Monitored Status
              </label>
              <select
                value={newMonitored ? 'true' : 'false'}
                onChange={(e) => setNewMonitored(e.target.value === 'true')}
                disabled={!changeMonitored}
                className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
              >
                <option value="true">Monitored</option>
                <option value="false">Unmonitored</option>
              </select>
            </div>
          </div>

          {/* Quality Profile */}
          <div className="flex items-start space-x-4">
            <input
              type="checkbox"
              id="changeQualityProfile"
              checked={changeQualityProfile}
              onChange={(e) => setChangeQualityProfile(e.target.checked)}
              className="mt-1 w-4 h-4 rounded border-gray-600 bg-gray-700 text-blue-600 focus:ring-blue-500"
            />
            <div className="flex-1">
              <label htmlFor="changeQualityProfile" className="block text-sm font-medium text-gray-300 mb-2">
                Change Quality Profile
              </label>
              <select
                value={newQualityProfileId}
                onChange={(e) => setNewQualityProfileId(parseInt(e.target.value))}
                disabled={!changeQualityProfile}
                className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
              >
                {qualityProfiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>
                    {profile.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* Tags */}
          <div className="flex items-start space-x-4">
            <input
              type="checkbox"
              id="changeTags"
              checked={changeTags}
              onChange={(e) => setChangeTags(e.target.checked)}
              className="mt-1 w-4 h-4 rounded border-gray-600 bg-gray-700 text-blue-600 focus:ring-blue-500"
            />
            <div className="flex-1">
              <label htmlFor="changeTags" className="block text-sm font-medium text-gray-300 mb-2">
                Change Tags
              </label>

              <select
                value={tagsAction}
                onChange={(e) => setTagsAction(e.target.value as any)}
                disabled={!changeTags}
                className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50 mb-3"
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
                    className={`px-3 py-1 rounded text-sm transition-colors ${
                      selectedTags.includes(tag.id)
                        ? 'bg-blue-600 text-white'
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

        {/* Save Button */}
        <div className="mt-6 pt-6 border-t border-gray-700 flex items-center justify-end">
          <button
            onClick={handleSave}
            disabled={saving || selectedEventIds.size === 0}
            className="px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {saving ? 'Saving...' : `Update ${selectedEventIds.size} Event(s)`}
          </button>
        </div>
      </div>
    </div>
  );
};

export default MassEditorPage;
