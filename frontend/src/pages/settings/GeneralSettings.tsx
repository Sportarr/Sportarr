interface GeneralSettingsProps {
  showAdvanced: boolean;
}

export default function GeneralSettings({ showAdvanced }: GeneralSettingsProps) {
  return (
    <div className="max-w-4xl">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">General</h2>
        <p className="text-gray-400">General application settings</p>
      </div>
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-12 text-center">
        <p className="text-gray-500">General settings coming soon</p>
        <p className="text-sm text-gray-400 mt-2">Host, port, SSL, authentication, backups, etc.</p>
      </div>
    </div>
  );
}
