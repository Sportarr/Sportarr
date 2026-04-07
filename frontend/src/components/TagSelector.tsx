import { useState, useEffect } from 'react';
import { apiGet } from '../utils/api';

interface Tag {
  id: number;
  label: string;
  color?: string;
}

interface TagSelectorProps {
  selectedTags: number[];
  onChange: (tags: number[]) => void;
  label?: string;
  helpText?: string;
}

export default function TagSelector({ selectedTags, onChange, label = 'Tags', helpText }: TagSelectorProps) {
  const [tags, setTags] = useState<Tag[]>([]);

  useEffect(() => {
    const loadTags = async () => {
      try {
        const res = await apiGet('/api/tag');
        const data = await res.json();
        setTags(data);
      } catch {
        // Tags are optional — don't block the UI
      }
    };
    loadTags();
  }, []);

  const toggleTag = (tagId: number) => {
    if (selectedTags.includes(tagId)) {
      onChange(selectedTags.filter(t => t !== tagId));
    } else {
      onChange([...selectedTags, tagId]);
    }
  };

  return (
    <div>
      <label className="block text-sm font-medium text-gray-300 mb-2">
        {label}
      </label>
      {helpText && (
        <p className="text-xs text-gray-500 mb-2">{helpText}</p>
      )}
      <div className="flex flex-wrap gap-2">
        {tags.length === 0 && (
          <p className="text-xs text-gray-500">No tags exist yet. Create tags in Settings to assign them here.</p>
        )}
        {tags.map((tag) => (
          <button
            key={tag.id}
            type="button"
            onClick={() => toggleTag(tag.id)}
            className={`px-3 py-1 rounded text-sm transition-colors ${
              selectedTags.includes(tag.id)
                ? 'text-white'
                : 'bg-gray-800 text-gray-400 hover:bg-gray-700'
            }`}
            style={selectedTags.includes(tag.id) ? { backgroundColor: tag.color || '#3b82f6' } : undefined}
          >
            {tag.label}
          </button>
        ))}
      </div>
    </div>
  );
}
