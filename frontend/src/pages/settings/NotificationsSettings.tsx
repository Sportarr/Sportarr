interface NotificationsSettingsProps {
  showAdvanced: boolean;
}

export default function NotificationsSettings({ showAdvanced }: NotificationsSettingsProps) {
  return (
    <div className="max-w-4xl">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Connect (Notifications)</h2>
        <p className="text-gray-400">Configure notifications and connections to other services</p>
      </div>
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-12 text-center">
        <p className="text-gray-500">Notifications settings coming soon</p>
        <p className="text-sm text-gray-400 mt-2">Discord, Slack, Telegram, Plex, etc.</p>
      </div>
    </div>
  );
}
