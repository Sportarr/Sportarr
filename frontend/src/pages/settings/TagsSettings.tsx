import { useState } from 'react';
import { TagIcon, PlusIcon, XMarkIcon, PencilIcon, TrashIcon } from '@heroicons/react/24/outline';
import { useLocalStorage } from '../../hooks/useLocalStorage';

interface TagsSettingsProps {
  showAdvanced: boolean;
}

interface Tag {
  id: number;
  label: string;
  color: string;
}

export default function TagsSettings({ showAdvanced }: TagsSettingsProps) {
  const [tags, setTags] = useLocalStorage<Tag[]>('fightarr_tags', []);
  const [showAddModal, setShowAddModal] = useState(false);
  const [showEditModal, setShowEditModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [editingTag, setEditingTag] = useState<Tag | null>(null);

  // Form state
  const [tagLabel, setTagLabel] = useState('');
  const [tagColor, setTagColor] = useState('#3b82f6');

  const predefinedColors = [
    { name: 'Blue', value: '#3b82f6' },
    { name: 'Red', value: '#ef4444' },
    { name: 'Green', value: '#10b981' },
    { name: 'Yellow', value: '#f59e0b' },
    { name: 'Purple', value: '#8b5cf6' },
    { name: 'Pink', value: '#ec4899' },
    { name: 'Indigo', value: '#6366f1' },
    { name: 'Orange', value: '#f97316' },
    { name: 'Teal', value: '#14b8a6' },
    { name: 'Cyan', value: '#06b6d4' },
    { name: 'Lime', value: '#84cc16' },
    { name: 'Emerald', value: '#059669' },
  ];

  const handleAddTag = () => {
    if (!tagLabel.trim()) {
      alert('Please enter a tag label');
      return;
    }

    // Check for duplicate label
    if (tags.some(t => t.label.toLowerCase() === tagLabel.trim().toLowerCase())) {
      alert('A tag with this label already exists');
      return;
    }

    const newTag: Tag = {
      id: Date.now(),
      label: tagLabel.trim(),
      color: tagColor,
    };

    setTags(prev => [...prev, newTag]);
    setShowAddModal(false);
    setTagLabel('');
    setTagColor('#3b82f6');
  };

  const handleEditTag = () => {
    if (!editingTag) return;

    if (!tagLabel.trim()) {
      alert('Please enter a tag label');
      return;
    }

    // Check for duplicate label (excluding the current tag)
    if (tags.some(t => t.id !== editingTag.id && t.label.toLowerCase() === tagLabel.trim().toLowerCase())) {
      alert('A tag with this label already exists');
      return;
    }

    setTags(prev => prev.map(t =>
      t.id === editingTag.id
        ? { ...t, label: tagLabel.trim(), color: tagColor }
        : t
    ));

    setShowEditModal(false);
    setEditingTag(null);
    setTagLabel('');
    setTagColor('#3b82f6');
  };

  const handleDeleteTag = (id: number) => {
    setTags(prev => prev.filter(t => t.id !== id));
    setShowDeleteConfirm(null);
  };

  const openEditModal = (tag: Tag) => {
    setEditingTag(tag);
    setTagLabel(tag.label);
    setTagColor(tag.color);
    setShowEditModal(true);
  };

  const closeAddModal = () => {
    setShowAddModal(false);
    setTagLabel('');
    setTagColor('#3b82f6');
  };

  const closeEditModal = () => {
    setShowEditModal(false);
    setEditingTag(null);
    setTagLabel('');
    setTagColor('#3b82f6');
  };

  return (
    <div className="max-w-4xl">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Tags</h2>
        <p className="text-gray-400">Manage tags for organizing events, profiles, and indexers</p>
      </div>

      {/* Add Tag Button */}
      <div className="mb-6">
        <button
          onClick={() => setShowAddModal(true)}
          className="flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
        >
          <PlusIcon className="w-5 h-5" />
          Add Tag
        </button>
      </div>

      {/* Tags List */}
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden">
        {tags.length === 0 ? (
          <div className="text-center py-12">
            <TagIcon className="w-16 h-16 text-gray-700 mx-auto mb-4" />
            <p className="text-gray-500 mb-2">No tags configured</p>
            <p className="text-sm text-gray-600">Create tags to organize your events and settings</p>
          </div>
        ) : (
          <div className="divide-y divide-gray-800">
            {tags.map((tag) => (
              <div key={tag.id} className="p-4 hover:bg-gray-800/50 transition-colors flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div
                    className="w-4 h-4 rounded-full"
                    style={{ backgroundColor: tag.color }}
                  />
                  <span className="text-white font-medium">{tag.label}</span>
                  <span className="text-sm text-gray-500" style={{ color: tag.color }}>
                    {tag.color}
                  </span>
                </div>
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => openEditModal(tag)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-700 rounded transition-colors"
                    title="Edit tag"
                  >
                    <PencilIcon className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => setShowDeleteConfirm(tag.id)}
                    className="p-2 text-gray-400 hover:text-red-500 hover:bg-gray-700 rounded transition-colors"
                    title="Delete tag"
                  >
                    <TrashIcon className="w-4 h-4" />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {showAdvanced && tags.length > 0 && (
        <div className="mt-6 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <h3 className="text-xl font-semibold text-white mb-4">Tag Usage</h3>
          <p className="text-gray-400 text-sm mb-4">
            Tags can be applied to:
          </p>
          <ul className="list-disc list-inside text-gray-400 text-sm space-y-1">
            <li>Events to categorize by promotion, organization, or event type</li>
            <li>Quality Profiles to apply different quality settings to different tags</li>
            <li>Custom Formats to restrict formats to specific content</li>
            <li>Indexers to control which indexers are used for tagged content</li>
            <li>Download Clients to route downloads to specific clients</li>
            <li>Notifications to send alerts only for tagged content</li>
          </ul>
        </div>
      )}

      {/* Add Tag Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50 p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg max-w-md w-full p-6">
            <div className="flex justify-between items-center mb-6">
              <h3 className="text-xl font-semibold text-white">Add Tag</h3>
              <button
                onClick={closeAddModal}
                className="text-gray-400 hover:text-white transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  Label *
                </label>
                <input
                  type="text"
                  value={tagLabel}
                  onChange={(e) => setTagLabel(e.target.value)}
                  placeholder="e.g., UFC, Bellator, High Priority"
                  className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-500"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  Color
                </label>
                <div className="grid grid-cols-6 gap-2 mb-3">
                  {predefinedColors.map((color) => (
                    <button
                      key={color.value}
                      onClick={() => setTagColor(color.value)}
                      className={`w-10 h-10 rounded-lg transition-all ${
                        tagColor === color.value
                          ? 'ring-2 ring-white ring-offset-2 ring-offset-gray-900'
                          : 'hover:scale-110'
                      }`}
                      style={{ backgroundColor: color.value }}
                      title={color.name}
                    />
                  ))}
                </div>
                <div className="flex items-center gap-2">
                  <input
                    type="color"
                    value={tagColor}
                    onChange={(e) => setTagColor(e.target.value)}
                    className="w-12 h-10 bg-gray-800 border border-gray-700 rounded cursor-pointer"
                  />
                  <input
                    type="text"
                    value={tagColor}
                    onChange={(e) => setTagColor(e.target.value)}
                    className="flex-1 px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-500"
                  />
                </div>
              </div>

              <div className="flex items-center gap-2 p-3 bg-gray-800/50 rounded-lg">
                <div
                  className="w-6 h-6 rounded-full flex-shrink-0"
                  style={{ backgroundColor: tagColor }}
                />
                <span className="text-white font-medium">
                  {tagLabel || 'Tag Preview'}
                </span>
              </div>
            </div>

            <div className="flex justify-end gap-3 mt-6">
              <button
                onClick={closeAddModal}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleAddTag}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Add Tag
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Edit Tag Modal */}
      {showEditModal && editingTag && (
        <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50 p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg max-w-md w-full p-6">
            <div className="flex justify-between items-center mb-6">
              <h3 className="text-xl font-semibold text-white">Edit Tag</h3>
              <button
                onClick={closeEditModal}
                className="text-gray-400 hover:text-white transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  Label *
                </label>
                <input
                  type="text"
                  value={tagLabel}
                  onChange={(e) => setTagLabel(e.target.value)}
                  placeholder="e.g., UFC, Bellator, High Priority"
                  className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-500"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  Color
                </label>
                <div className="grid grid-cols-6 gap-2 mb-3">
                  {predefinedColors.map((color) => (
                    <button
                      key={color.value}
                      onClick={() => setTagColor(color.value)}
                      className={`w-10 h-10 rounded-lg transition-all ${
                        tagColor === color.value
                          ? 'ring-2 ring-white ring-offset-2 ring-offset-gray-900'
                          : 'hover:scale-110'
                      }`}
                      style={{ backgroundColor: color.value }}
                      title={color.name}
                    />
                  ))}
                </div>
                <div className="flex items-center gap-2">
                  <input
                    type="color"
                    value={tagColor}
                    onChange={(e) => setTagColor(e.target.value)}
                    className="w-12 h-10 bg-gray-800 border border-gray-700 rounded cursor-pointer"
                  />
                  <input
                    type="text"
                    value={tagColor}
                    onChange={(e) => setTagColor(e.target.value)}
                    className="flex-1 px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-500"
                  />
                </div>
              </div>

              <div className="flex items-center gap-2 p-3 bg-gray-800/50 rounded-lg">
                <div
                  className="w-6 h-6 rounded-full flex-shrink-0"
                  style={{ backgroundColor: tagColor }}
                />
                <span className="text-white font-medium">
                  {tagLabel || 'Tag Preview'}
                </span>
              </div>
            </div>

            <div className="flex justify-end gap-3 mt-6">
              <button
                onClick={closeEditModal}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleEditTag}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Save Changes
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm !== null && (
        <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50 p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg max-w-md w-full p-6">
            <h3 className="text-xl font-semibold text-white mb-4">Delete Tag</h3>
            <p className="text-gray-300 mb-6">
              Are you sure you want to delete the tag "<span className="font-semibold text-white">
                {tags.find(t => t.id === showDeleteConfirm)?.label}
              </span>"?
            </p>
            <p className="text-sm text-gray-400 mb-6">
              This will remove the tag from all items it's currently assigned to.
            </p>
            <div className="flex justify-end gap-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDeleteTag(showDeleteConfirm)}
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
