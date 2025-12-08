import { useState, useEffect } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, XMarkIcon, Bars3Icon, ChevronUpIcon, ChevronDownIcon, FolderPlusIcon, FolderMinusIcon, DocumentDuplicateIcon } from '@heroicons/react/24/outline';

interface ProfilesSettingsProps {
  showAdvanced?: boolean;
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
  id?: number;
  items?: QualityItem[]; // For quality groups - contains nested qualities
}

// Helper to check if a quality item is a group
const isQualityGroup = (item: QualityItem): boolean => {
  return item.items !== undefined && item.items !== null && item.items.length > 0;
};

// Deep copy quality items (including nested items for groups)
const deepCopyQualityItems = (items: QualityItem[]): QualityItem[] => {
  return items.map(item => ({
    ...item,
    items: item.items ? item.items.map(child => ({ ...child })) : undefined
  }));
};

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

// Available quality items with group structure
// Groups contain multiple equivalent qualities that are treated as equal
const availableQualities: QualityItem[] = [
  {
    name: 'WEB 2160p',
    quality: 19,
    allowed: false,
    id: 1019,
    items: [
      { name: 'WEBDL-2160p', quality: 18, allowed: false, id: 18 },
      { name: 'WEBRip-2160p', quality: 17, allowed: false, id: 17 },
    ]
  },
  { name: 'Bluray-2160p', quality: 16, allowed: false, id: 16 },
  { name: 'Bluray-2160p Remux', quality: 15, allowed: false, id: 15 },
  { name: 'HDTV-2160p', quality: 14, allowed: false, id: 14 },
  {
    name: 'WEB 1080p',
    quality: 13,
    allowed: false,
    id: 1013,
    items: [
      { name: 'WEBDL-1080p', quality: 12, allowed: false, id: 12 },
      { name: 'WEBRip-1080p', quality: 11, allowed: false, id: 11 },
    ]
  },
  { name: 'Bluray-1080p', quality: 10, allowed: false, id: 10 },
  { name: 'Bluray-1080p Remux', quality: 9, allowed: false, id: 9 },
  { name: 'HDTV-1080p', quality: 8, allowed: false, id: 8 },
  {
    name: 'WEB 720p',
    quality: 7,
    allowed: false,
    id: 1007,
    items: [
      { name: 'WEBDL-720p', quality: 6, allowed: false, id: 6 },
      { name: 'WEBRip-720p', quality: 5, allowed: false, id: 5 },
    ]
  },
  { name: 'Bluray-720p', quality: 4, allowed: false, id: 4 },
  { name: 'HDTV-720p', quality: 3, allowed: false, id: 3 },
  { name: 'Raw-HD', quality: 2, allowed: false, id: 2 },
  {
    name: 'WEB 480p',
    quality: 1,
    allowed: false,
    id: 1001,
    items: [
      { name: 'WEBDL-480p', quality: -1, allowed: false, id: -1 },
      { name: 'WEBRip-480p', quality: -2, allowed: false, id: -2 },
    ]
  },
  { name: 'Bluray-480p', quality: -3, allowed: false, id: -3 },
  { name: 'DVD', quality: -4, allowed: false, id: -4 },
  { name: 'SDTV', quality: -5, allowed: false, id: -5 },
  { name: 'Unknown', quality: 0, allowed: false, id: 0 },
];

export default function ProfilesSettings({ showAdvanced = false }: ProfilesSettingsProps) {
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfile[]>([]);
  const [customFormats, setCustomFormats] = useState<CustomFormat[]>([]);
  const [editingProfile, setEditingProfile] = useState<QualityProfile | null>(null);
  const [showAddModal, setShowAddModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [editingGroups, setEditingGroups] = useState(false);
  const [draggedItem, setDraggedItem] = useState<{ index: number; isGroup: boolean; parentIndex?: number } | null>(null);
  const [dropTarget, setDropTarget] = useState<{ index: number; parentIndex?: number; position: 'above' | 'below' } | null>(null);
  const [formatSortOrder, setFormatSortOrder] = useState<'alphabetical' | 'score'>('alphabetical');

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
    items: deepCopyQualityItems(availableQualities),
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
    preferredProtocol: 'Torrent',
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
      items: deepCopyQualityItems(availableQualities),
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
    // Merge formatItems with custom format names (API may not include formatName)
    const formatItemsWithNames = profile.formatItems?.map(item => {
      const format = customFormats.find(f => f.id === item.formatId);
      return {
        ...item,
        formatName: format?.name || item.formatName || `Format ${item.formatId}`
      };
    }) || [];
    setFormData({
      ...profile,
      formatItems: formatItemsWithNames
    });
    setShowAddModal(true);
  };

  const handleDuplicate = (profile: QualityProfile) => {
    // Create a copy with no ID (so it creates a new profile) and a modified name
    setEditingProfile(null); // null means we're creating new, not editing
    const formatItemsWithNames = profile.formatItems?.map(item => {
      const format = customFormats.find(f => f.id === item.formatId);
      return {
        ...item,
        formatName: format?.name || item.formatName || `Format ${item.formatId}`
      };
    }) || [];
    setFormData({
      ...profile,
      id: undefined, // Remove ID so it creates a new profile
      name: `${profile.name} (Copy)`,
      isDefault: false, // Copies shouldn't be default
      items: deepCopyQualityItems(profile.items),
      formatItems: formatItemsWithNames
    });
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
      } else if (response.status === 400) {
        const errorData = await response.json();
        alert(errorData.error || 'A quality profile with this name already exists');
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
        const response = await fetch(`/api/qualityprofile/${profile.id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(profile),
        });

        if (!response.ok) {
          const error = await response.json();
          console.error('Failed to update profile:', error);
          alert(`Failed to set default profile: ${error.error || 'Unknown error'}`);
          return;
        }
      }

      await loadProfiles();
    } catch (error) {
      console.error('Failed to set default quality profile:', error);
      alert('Failed to set default quality profile. Check console for details.');
    }
  };

  const handleToggleQuality = (itemId: number | undefined, isGroup: boolean = false) => {
    setFormData(prev => ({
      ...prev,
      items: prev.items?.map(item => {
        if (item.id === itemId) {
          const newAllowed = !item.allowed;
          // If it's a group, toggle all children too
          if (isGroup && item.items) {
            return {
              ...item,
              allowed: newAllowed,
              items: item.items.map(child => ({ ...child, allowed: newAllowed }))
            };
          }
          return { ...item, allowed: newAllowed };
        }
        // Check if the target is inside a group
        if (item.items) {
          const updatedItems = item.items.map(child =>
            child.id === itemId ? { ...child, allowed: !child.allowed } : child
          );
          // If any child changed, update the group's allowed state
          const anyAllowed = updatedItems.some(child => child.allowed);
          return { ...item, items: updatedItems, allowed: anyAllowed };
        }
        return item;
      })
    }));
  };

  // Move item up in the list
  const handleMoveUp = (index: number) => {
    if (index === 0) return;
    setFormData(prev => {
      const newItems = [...(prev.items || [])];
      [newItems[index - 1], newItems[index]] = [newItems[index], newItems[index - 1]];
      return { ...prev, items: newItems };
    });
  };

  // Move item down in the list
  const handleMoveDown = (index: number) => {
    setFormData(prev => {
      const items = prev.items || [];
      if (index >= items.length - 1) return prev;
      const newItems = [...items];
      [newItems[index], newItems[index + 1]] = [newItems[index + 1], newItems[index]];
      return { ...prev, items: newItems };
    });
  };

  // Create a new group from a quality
  const handleCreateGroup = (index: number) => {
    setFormData(prev => {
      const items = [...(prev.items || [])];
      const item = items[index];
      if (isQualityGroup(item)) return prev; // Already a group

      // Convert to a group with the item as a child
      items[index] = {
        name: item.name,
        quality: item.quality,
        allowed: item.allowed,
        id: item.id ? item.id + 1000 : 1000 + index, // Group ID
        items: [{ ...item }]
      };
      return { ...prev, items };
    });
  };

  // Ungroup a quality group (flatten to individual items)
  const handleUngroup = (index: number) => {
    setFormData(prev => {
      const items = [...(prev.items || [])];
      const group = items[index];
      if (!isQualityGroup(group) || !group.items) return prev;

      // Replace the group with its children
      items.splice(index, 1, ...group.items);
      return { ...prev, items };
    });
  };

  // Handle drag start
  const handleDragStart = (e: React.DragEvent, index: number, isGroup: boolean, parentIndex?: number) => {
    setDraggedItem({ index, isGroup, parentIndex });
    e.dataTransfer.effectAllowed = 'move';
  };

  // Handle drag over
  const handleDragOver = (e: React.DragEvent, targetIndex: number, isGroup: boolean, parentIndex?: number) => {
    e.preventDefault();
    if (!draggedItem) return;

    // Don't allow dropping on itself
    if (draggedItem.index === targetIndex && draggedItem.parentIndex === parentIndex) {
      setDropTarget(null);
      return;
    }

    // Calculate if dropping above or below based on mouse position
    const rect = (e.target as HTMLElement).closest('[data-droppable]')?.getBoundingClientRect();
    if (rect) {
      const midpoint = rect.top + rect.height / 2;
      const position = e.clientY < midpoint ? 'above' : 'below';
      setDropTarget({ index: targetIndex, parentIndex, position });
    }
  };

  // Handle drag leave
  const handleDragLeave = (e: React.DragEvent) => {
    // Only clear if leaving the droppable area entirely
    const relatedTarget = e.relatedTarget as HTMLElement;
    if (!relatedTarget?.closest('[data-droppable]')) {
      setDropTarget(null);
    }
  };

  // Handle drag end
  const handleDragEnd = () => {
    setDraggedItem(null);
    setDropTarget(null);
  };

  // Handle drop
  const handleDrop = (e: React.DragEvent, targetIndex: number, isGroup: boolean, parentIndex?: number) => {
    e.preventDefault();
    if (!draggedItem) return;

    // Adjust target index based on drop position
    let adjustedTargetIndex = targetIndex;
    if (dropTarget?.position === 'below') {
      adjustedTargetIndex = targetIndex + 1;
    }

    setFormData(prev => {
      const items = [...(prev.items || [])];

      // Both at top level
      if (draggedItem.parentIndex === undefined && parentIndex === undefined) {
        const [movedItem] = items.splice(draggedItem.index, 1);
        const finalTarget = adjustedTargetIndex > draggedItem.index ? adjustedTargetIndex - 1 : adjustedTargetIndex;
        items.splice(finalTarget, 0, movedItem);
      }
      // Moving within the same group
      else if (draggedItem.parentIndex !== undefined && parentIndex !== undefined &&
               draggedItem.parentIndex === parentIndex) {
        const group = items[parentIndex];
        if (group.items) {
          const groupItems = [...group.items];
          const [movedItem] = groupItems.splice(draggedItem.index, 1);
          const finalTarget = adjustedTargetIndex > draggedItem.index ? adjustedTargetIndex - 1 : adjustedTargetIndex;
          groupItems.splice(finalTarget, 0, movedItem);
          items[parentIndex] = { ...group, items: groupItems };
        }
      }
      // Moving from top level into a group
      else if (draggedItem.parentIndex === undefined && parentIndex !== undefined) {
        const [movedItem] = items.splice(draggedItem.index, 1);
        // Adjust parent index if needed
        const adjustedParent = parentIndex > draggedItem.index ? parentIndex - 1 : parentIndex;
        const group = items[adjustedParent];
        if (group.items) {
          const groupItems = [...group.items];
          groupItems.splice(adjustedTargetIndex, 0, movedItem);
          items[adjustedParent] = { ...group, items: groupItems };
        }
      }
      // Moving from a group to top level
      else if (draggedItem.parentIndex !== undefined && parentIndex === undefined) {
        const group = items[draggedItem.parentIndex];
        if (group.items) {
          const groupItems = [...group.items];
          const [movedItem] = groupItems.splice(draggedItem.index, 1);
          items[draggedItem.parentIndex] = { ...group, items: groupItems };
          items.splice(adjustedTargetIndex, 0, movedItem);
        }
      }

      return { ...prev, items };
    });

    setDraggedItem(null);
    setDropTarget(null);
  };

  const handleFormatScoreChange = (formatId: number, score: number) => {
    setFormData(prev => ({
      ...prev,
      formatItems: prev.formatItems?.map(item =>
        item.formatId === formatId ? { ...item, score } : item
      )
    }));
  };

  const getQualityName = (qualityOrId: number | null | undefined) => {
    if (!qualityOrId) return 'Not Set';
    // Search in top-level and nested items
    for (const item of availableQualities) {
      if (item.quality === qualityOrId || item.id === qualityOrId) {
        return item.name;
      }
      if (item.items) {
        const child = item.items.find(c => c.quality === qualityOrId || c.id === qualityOrId);
        if (child) return child.name;
      }
    }
    return 'Unknown';
  };

  // Get all qualities for the cutoff dropdown (flattened list)
  const getAllQualitiesForCutoff = (items: QualityItem[] | undefined): QualityItem[] => {
    if (!items) return [];
    const result: QualityItem[] = [];
    for (const item of items) {
      if (item.allowed) {
        result.push(item);
      }
      if (item.items) {
        for (const child of item.items) {
          if (child.allowed) {
            result.push(child);
          }
        }
      }
    }
    return result;
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
      preferredProtocol: 'Torrent',
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
          Quality profiles determine which releases Sportarr will download and upgrade
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
                      onClick={() => handleDuplicate(profile)}
                      className="p-2 text-gray-400 hover:text-blue-400 hover:bg-blue-950/30 rounded transition-colors"
                      title="Clone Profile"
                    >
                      <DocumentDuplicateIcon className="w-5 h-5" />
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
          Delay profiles allow you to reduce the number of releases downloaded by adding a delay while Sportarr continues to watch for better releases
        </p>

        {/* Info Box */}
        <div className="mb-6 bg-gradient-to-br from-blue-950/30 to-blue-900/20 border border-blue-900/50 rounded-lg p-6">
          <h3 className="text-lg font-semibold text-white mb-2">How Delay Profiles Work</h3>
          <ul className="text-sm text-gray-300 space-y-2">
            <li>• Timer begins when Sportarr detects an event has a release available</li>
            <li>• During the delay period, any new releases are noted by Sportarr</li>
            <li>• When the delay timer expires, Sportarr downloads the single release which best matches your quality preferences</li>
            <li>• Timer starts from the releases uploaded time (not when Sportarr sees it)</li>
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
                          <button
                            onClick={() => setShowDelayDeleteConfirm(profile.id)}
                            className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                            title="Delete"
                          >
                            <TrashIcon className="w-5 h-5" />
                          </button>
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
                <div className="flex items-center justify-between mb-3">
                  <div>
                    <h4 className="text-lg font-semibold text-white">Qualities</h4>
                    <p className="text-sm text-gray-400 mt-1">
                      Qualities higher in the list are more preferred. Qualities within the same group are equal. Only checked qualities are wanted.
                    </p>
                  </div>
                  <button
                    onClick={() => setEditingGroups(!editingGroups)}
                    className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
                      editingGroups
                        ? 'bg-green-600 hover:bg-green-700 text-white'
                        : 'bg-gray-700 hover:bg-gray-600 text-white'
                    }`}
                  >
                    {editingGroups ? 'Done Editing Groups' : 'Edit Groups'}
                  </button>
                </div>

                {/* Quality Groups Editor */}
                <div className="bg-black/30 rounded-lg p-2 max-h-80 overflow-y-auto">
                  {formData.items?.map((item, index) => {
                    const isBeingDragged = draggedItem?.index === index && draggedItem?.parentIndex === undefined;
                    const showDropAbove = dropTarget?.index === index && dropTarget?.parentIndex === undefined && dropTarget?.position === 'above';
                    const showDropBelow = dropTarget?.index === index && dropTarget?.parentIndex === undefined && dropTarget?.position === 'below';

                    return (
                    <div key={item.id ?? item.quality} className="mb-1">
                      {/* Drop indicator above */}
                      {showDropAbove && (
                        <div className="h-1 bg-blue-500 rounded-full mx-2 mb-1 animate-pulse" />
                      )}

                      {/* Quality Item or Group Header */}
                      <div
                        data-droppable="true"
                        draggable={editingGroups}
                        onDragStart={(e) => handleDragStart(e, index, isQualityGroup(item))}
                        onDragOver={(e) => handleDragOver(e, index, isQualityGroup(item))}
                        onDragLeave={handleDragLeave}
                        onDragEnd={handleDragEnd}
                        onDrop={(e) => handleDrop(e, index, isQualityGroup(item))}
                        className={`flex items-center px-3 py-2 rounded transition-all ${
                          item.allowed
                            ? 'bg-green-950/30 border border-green-900/50'
                            : 'bg-gray-900/50 border border-gray-800'
                        } ${editingGroups ? 'cursor-grab hover:border-blue-500' : ''} ${
                          isBeingDragged ? 'opacity-50 border-dashed border-blue-500' : ''
                        }`}
                      >
                        {/* Drag Handle (visible in edit mode) */}
                        {editingGroups && (
                          <div className="mr-2 text-gray-500">
                            <Bars3Icon className="w-4 h-4" />
                          </div>
                        )}

                        {/* Checkbox */}
                        <button
                          onClick={() => handleToggleQuality(item.id, isQualityGroup(item))}
                          className={`w-5 h-5 mr-3 rounded border flex items-center justify-center transition-colors ${
                            item.allowed
                              ? 'bg-green-600 border-green-600 text-white'
                              : 'bg-transparent border-gray-600 text-transparent hover:border-gray-500'
                          }`}
                        >
                          {item.allowed && <span className="text-xs">✓</span>}
                        </button>

                        {/* Name and Group Badges (for collapsed view) */}
                        <div className="flex-1 flex items-center flex-wrap gap-1">
                          <span className={`text-sm ${
                            item.allowed ? 'text-green-400' : 'text-gray-400'
                          }`}>
                            {item.name}
                          </span>

                          {/* Show nested items as small badges when not in edit mode */}
                          {isQualityGroup(item) && item.items && !editingGroups && (
                            <div className="flex flex-wrap gap-1 ml-2">
                              {item.items.map((childItem) => (
                                <span
                                  key={childItem.id ?? childItem.quality}
                                  className={`px-2 py-0.5 rounded text-xs ${
                                    childItem.allowed
                                      ? 'bg-green-900/40 text-green-400 border border-green-800/50'
                                      : 'bg-gray-800/60 text-gray-500 border border-gray-700/50'
                                  }`}
                                >
                                  {childItem.name}
                                </span>
                              ))}
                            </div>
                          )}
                        </div>

                        {/* Edit Mode Controls */}
                        {editingGroups && (
                          <div className="flex items-center space-x-1">
                            {/* Move Up/Down */}
                            <button
                              onClick={() => handleMoveUp(index)}
                              disabled={index === 0}
                              className="p-1 text-gray-500 hover:text-white disabled:opacity-30 disabled:cursor-not-allowed"
                              title="Move up"
                            >
                              <ChevronUpIcon className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => handleMoveDown(index)}
                              disabled={index === (formData.items?.length ?? 0) - 1}
                              className="p-1 text-gray-500 hover:text-white disabled:opacity-30 disabled:cursor-not-allowed"
                              title="Move down"
                            >
                              <ChevronDownIcon className="w-4 h-4" />
                            </button>

                            {/* Group/Ungroup */}
                            {isQualityGroup(item) ? (
                              <button
                                onClick={() => handleUngroup(index)}
                                className="p-1 text-gray-500 hover:text-red-400"
                                title="Ungroup"
                              >
                                <FolderMinusIcon className="w-4 h-4" />
                              </button>
                            ) : (
                              <button
                                onClick={() => handleCreateGroup(index)}
                                className="p-1 text-gray-500 hover:text-blue-400"
                                title="Create group"
                              >
                                <FolderPlusIcon className="w-4 h-4" />
                              </button>
                            )}
                          </div>
                        )}
                      </div>

                      {/* Drop indicator below */}
                      {showDropBelow && !isQualityGroup(item) && (
                        <div className="h-1 bg-blue-500 rounded-full mx-2 mt-1 animate-pulse" />
                      )}

                      {/* Nested Items (for groups) - ONLY shown in edit mode */}
                      {editingGroups && isQualityGroup(item) && item.items && (
                        <div className="ml-6 border-l border-gray-700">
                          {item.items.map((childItem, childIndex) => {
                            const isChildDragged = draggedItem?.index === childIndex && draggedItem?.parentIndex === index;
                            const showChildDropAbove = dropTarget?.index === childIndex && dropTarget?.parentIndex === index && dropTarget?.position === 'above';
                            const showChildDropBelow = dropTarget?.index === childIndex && dropTarget?.parentIndex === index && dropTarget?.position === 'below';

                            return (
                            <div key={childItem.id ?? childItem.quality}>
                              {/* Drop indicator above child */}
                              {showChildDropAbove && (
                                <div className="h-1 bg-blue-500 rounded-full mx-2 mb-1 animate-pulse" />
                              )}

                              <div
                                data-droppable="true"
                                draggable={editingGroups}
                                onDragStart={(e) => handleDragStart(e, childIndex, false, index)}
                                onDragOver={(e) => handleDragOver(e, childIndex, false, index)}
                                onDragLeave={handleDragLeave}
                                onDragEnd={handleDragEnd}
                                onDrop={(e) => handleDrop(e, childIndex, false, index)}
                                className={`flex items-center px-3 py-2 ml-2 rounded transition-all ${
                                  childItem.allowed
                                    ? 'bg-green-950/30 border border-green-900/50'
                                    : 'bg-gray-900/50 border border-gray-800'
                                } cursor-grab hover:border-blue-500 ${
                                  isChildDragged ? 'opacity-50 border-dashed border-blue-500' : ''
                                }`}
                              >
                                {/* Drag Handle */}
                                <div className="mr-2 text-gray-500">
                                  <Bars3Icon className="w-4 h-4" />
                                </div>

                                {/* Checkbox */}
                                <button
                                  onClick={() => handleToggleQuality(childItem.id, false)}
                                  className={`w-5 h-5 mr-3 rounded border flex items-center justify-center transition-colors ${
                                    childItem.allowed
                                      ? 'bg-green-600 border-green-600 text-white'
                                      : 'bg-transparent border-gray-600 text-transparent hover:border-gray-500'
                                  }`}
                                >
                                  {childItem.allowed && <span className="text-xs">✓</span>}
                                </button>

                                {/* Name */}
                                <span className={`flex-1 text-sm ${
                                  childItem.allowed ? 'text-green-400' : 'text-gray-400'
                                }`}>
                                  {childItem.name}
                                </span>
                              </div>

                              {/* Drop indicator below child */}
                              {showChildDropBelow && (
                                <div className="h-1 bg-blue-500 rounded-full mx-2 mt-1 animate-pulse" />
                              )}
                            </div>
                          )})}
                        </div>
                      )}

                      {/* Drop indicator below group */}
                      {showDropBelow && isQualityGroup(item) && (
                        <div className="h-1 bg-blue-500 rounded-full mx-2 mt-1 animate-pulse" />
                      )}
                    </div>
                  )})}
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
                  {getAllQualitiesForCutoff(formData.items).map(q => (
                    <option key={q.id ?? q.quality} value={q.id ?? q.quality}>{q.name}</option>
                  ))}
                </select>
                <p className="text-xs text-gray-500 mt-1">
                  Once this quality is reached Sportarr will no longer download episodes
                </p>
              </div>

              {/* Custom Format Scoring */}
              <div className="space-y-4 p-4 bg-purple-950/10 border border-purple-900/30 rounded-lg">
                <h4 className="text-lg font-semibold text-white">Custom Formats</h4>
                <p className="text-sm text-gray-400">
                  Sportarr scores each release using the sum of scores for matching custom formats. If a new release would improve the score, at the same or better quality, then Sportarr will grab it.
                </p>

                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <div className="flex flex-col">
                    <label className="block text-sm font-medium text-gray-300 mb-2 h-10 flex items-end">
                      Minimum Custom Format Score
                    </label>
                    <input
                      type="number"
                      value={formData.minFormatScore || 0}
                      onChange={(e) => setFormData(prev => ({ ...prev, minFormatScore: parseInt(e.target.value) || 0 }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1 min-h-[2.5rem]">
                      Minimum custom format score allowed to download
                    </p>
                  </div>

                  <div className="flex flex-col">
                    <label className="block text-sm font-medium text-gray-300 mb-2 h-10 flex items-end">
                      Upgrade Until Custom Format Score
                    </label>
                    <input
                      type="number"
                      value={formData.cutoffFormatScore || 10000}
                      onChange={(e) => setFormData(prev => ({ ...prev, cutoffFormatScore: parseInt(e.target.value) || 10000 }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1 min-h-[2.5rem]">
                      Once this score is reached Sportarr will no longer grab releases
                    </p>
                  </div>

                  <div className="flex flex-col">
                    <label className="block text-sm font-medium text-gray-300 mb-2 h-10 flex items-end">
                      Minimum Score Increment
                    </label>
                    <input
                      type="number"
                      value={formData.formatScoreIncrement || 1}
                      onChange={(e) => setFormData(prev => ({ ...prev, formatScoreIncrement: parseInt(e.target.value) || 1 }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1 min-h-[2.5rem]">
                      Minimum improvement needed before considering it an upgrade
                    </p>
                  </div>
                </div>

                {/* Custom Formats List */}
                {customFormats.length > 0 ? (
                  <div className="mt-4">
                    <div className="flex items-center justify-between mb-2">
                      <h5 className="text-md font-semibold text-white">Custom Format Scoring</h5>
                      <div className="flex items-center space-x-2">
                        <span className="text-sm text-gray-400">Sort by:</span>
                        <select
                          value={formatSortOrder}
                          onChange={(e) => setFormatSortOrder(e.target.value as 'alphabetical' | 'score')}
                          className="px-3 py-1 bg-gray-800 border border-gray-700 rounded text-sm text-white focus:outline-none focus:border-purple-600"
                        >
                          <option value="alphabetical">Name (A-Z)</option>
                          <option value="score">Score</option>
                        </select>
                      </div>
                    </div>
                    <div className="max-h-[32rem] overflow-y-auto space-y-2 p-3 bg-black/30 rounded-lg">
                      {formData.formatItems?.slice().sort((a, b) => {
                        if (formatSortOrder === 'alphabetical') {
                          return (a.formatName || '').localeCompare(b.formatName || '');
                        } else {
                          // Sort by score descending (highest first), then alphabetically
                          if (b.score !== a.score) return b.score - a.score;
                          return (a.formatName || '').localeCompare(b.formatName || '');
                        }
                      }).map((item) => (
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
