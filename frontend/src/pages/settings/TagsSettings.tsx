interface TagsSettingsProps {
  showAdvanced: boolean;
}

export default function TagsSettings({ showAdvanced }: TagsSettingsProps) {
  return (
    <div className="max-w-4xl">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Tags</h2>
        <p className="text-gray-400">Manage tags for organizing events, profiles, and indexers</p>
      </div>
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-12 text-center">
        <p className="text-gray-500">Tags management coming soon</p>
        <p className="text-sm text-gray-400 mt-2">Create and manage tags for better organization</p>
      </div>
    </div>
  );
}
