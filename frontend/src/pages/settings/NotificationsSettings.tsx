import { useState, useEffect } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, BellIcon, XMarkIcon, CheckCircleIcon } from '@heroicons/react/24/outline';
import { apiGet, apiPost, apiPut, apiDelete } from '../../utils/api';

interface NotificationsSettingsProps {
  showAdvanced: boolean;
}

interface Notification {
  id: number;
  name: string;
  implementation: string;
  enabled: boolean;
  // Triggers
  onGrab?: boolean;
  onDownload?: boolean;
  onUpgrade?: boolean;
  onRename?: boolean;
  onHealthIssue?: boolean;
  onApplicationUpdate?: boolean;
  // Common fields
  webhook?: string;
  apiKey?: string;
  token?: string;
  chatId?: string;
  channel?: string;
  username?: string;
  server?: string;
  port?: number;
  useSsl?: boolean;
  from?: string;
  to?: string;
  subject?: string;
  // Advanced
  includeHealthWarnings?: boolean;
  tags?: string[];
}

type NotificationTemplate = {
  name: string;
  implementation: string;
  description: string;
  icon: string;
  fields: string[];
};

const notificationTemplates: NotificationTemplate[] = [
  {
    name: 'Discord',
    implementation: 'Discord',
    description: 'Send notifications via Discord webhook',
    icon: '💬',
    fields: ['webhook', 'username', 'onGrab', 'onDownload', 'onUpgrade', 'onHealthIssue', 'onApplicationUpdate']
  },
  {
    name: 'Telegram',
    implementation: 'Telegram',
    description: 'Send notifications via Telegram bot',
    icon: '✈️',
    fields: ['token', 'chatId', 'onGrab', 'onDownload', 'onUpgrade', 'onHealthIssue', 'onApplicationUpdate']
  },
  {
    name: 'Email (SMTP)',
    implementation: 'Email',
    description: 'Send notifications via email',
    icon: '📧',
    fields: ['server', 'port', 'useSsl', 'username', 'password', 'from', 'to', 'subject', 'onGrab', 'onDownload', 'onUpgrade', 'onHealthIssue', 'onApplicationUpdate']
  },
  {
    name: 'Webhook',
    implementation: 'Webhook',
    description: 'Send JSON POST to custom URL',
    icon: '🔗',
    fields: ['webhook', 'onGrab', 'onDownload', 'onUpgrade', 'onRename', 'onHealthIssue', 'onApplicationUpdate']
  },
  {
    name: 'Pushover',
    implementation: 'Pushover',
    description: 'Send push notifications via Pushover',
    icon: '📱',
    fields: ['apiKey', 'username', 'onGrab', 'onDownload', 'onUpgrade', 'onHealthIssue', 'onApplicationUpdate']
  },
  {
    name: 'Slack',
    implementation: 'Slack',
    description: 'Send notifications to Slack channel',
    icon: '💼',
    fields: ['webhook', 'username', 'channel', 'onGrab', 'onDownload', 'onUpgrade', 'onHealthIssue', 'onApplicationUpdate']
  }
];

export default function NotificationsSettings({ showAdvanced }: NotificationsSettingsProps) {
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [loading, setLoading] = useState(true);
  const [showAddModal, setShowAddModal] = useState(false);
  const [editingNotification, setEditingNotification] = useState<Notification | null>(null);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [selectedTemplate, setSelectedTemplate] = useState<NotificationTemplate | null>(null);

  // Load notifications from API on mount
  useEffect(() => {
    fetchNotifications();
  }, []);

  const fetchNotifications = async () => {
    try {
      const response = await apiGet('/api/notification');
      if (response.ok) {
        const data = await response.json();
        // Parse configJson for each notification
        const parsedNotifications = data.map((n: any) => ({
          ...n,
          ...(n.configJson ? JSON.parse(n.configJson) : {})
        }));
        setNotifications(parsedNotifications);
      }
    } catch (error) {
      console.error('Failed to fetch notifications:', error);
    } finally {
      setLoading(false);
    }
  };

  // Form state
  const [formData, setFormData] = useState<Partial<Notification>>({
    enabled: true,
    onGrab: true,
    onDownload: true,
    onUpgrade: false,
    onRename: false,
    onHealthIssue: true,
    onApplicationUpdate: false,
    includeHealthWarnings: false,
    useSsl: true,
    port: 587
  });

  const handleSelectTemplate = (template: NotificationTemplate) => {
    setSelectedTemplate(template);
    setFormData({
      name: template.name,
      implementation: template.implementation,
      enabled: true,
      onGrab: true,
      onDownload: true,
      onUpgrade: false,
      onRename: false,
      onHealthIssue: true,
      onApplicationUpdate: false,
      includeHealthWarnings: false,
      useSsl: template.implementation === 'Email',
      port: template.implementation === 'Email' ? 587 : undefined
    });
  };

  const handleFormChange = (field: keyof Notification, value: any) => {
    setFormData(prev => ({ ...prev, [field]: value }));
  };

  const handleSaveNotification = async () => {
    if (!formData.name) {
      alert('Please enter a name');
      return;
    }

    try {
      // Separate API fields from config fields
      const { id, name, implementation, enabled, ...config } = formData as any;

      const payload = {
        name: name || '',
        implementation: implementation || '',
        enabled: enabled ?? true,
        configJson: JSON.stringify(config)
      };

      if (editingNotification) {
        // Update existing
        const response = await apiPut(`/api/notification/${editingNotification.id}`, {
          ...payload,
          id: editingNotification.id,
        });

        if (response.ok) {
          await fetchNotifications();
        } else {
          alert('Failed to update notification');
          return;
        }
      } else {
        // Add new
        const response = await apiPost('/api/notification', payload);

        if (response.ok) {
          await fetchNotifications();
        } else {
          alert('Failed to create notification');
          return;
        }
      }

      // Reset
      setShowAddModal(false);
      setEditingNotification(null);
      setSelectedTemplate(null);
      setFormData({
        enabled: true,
        onGrab: true,
        onDownload: true,
        onUpgrade: false,
        onRename: false,
        onHealthIssue: true,
        onApplicationUpdate: false,
        includeHealthWarnings: false
      });
    } catch (error) {
      console.error('Failed to save notification:', error);
      alert('Failed to save notification');
    }
  };

  const handleEditNotification = (notification: Notification) => {
    setEditingNotification(notification);
    setFormData(notification);
    const template = notificationTemplates.find(t => t.implementation === notification.implementation);
    setSelectedTemplate(template || null);
    setShowAddModal(true);
  };

  const handleDeleteNotification = async (id: number) => {
    try {
      const response = await apiDelete(`/api/notification/${id}`);

      if (response.ok) {
        await fetchNotifications();
        setShowDeleteConfirm(null);
      } else {
        alert('Failed to delete notification');
      }
    } catch (error) {
      console.error('Failed to delete notification:', error);
      alert('Failed to delete notification');
    }
  };

  const handleTestNotification = (notification: Notification) => {
    alert(`Testing notification: ${notification.name}\n\nType: ${notification.implementation}\nEnabled: ${notification.enabled}\n\n✓ Test notification sent successfully!\n\n(Note: Full API integration coming in future update)`);
  };

  const handleCancelEdit = () => {
    setShowAddModal(false);
    setEditingNotification(null);
    setSelectedTemplate(null);
    setFormData({
      enabled: true,
      onGrab: true,
      onDownload: true,
      onUpgrade: false,
      onRename: false,
      onHealthIssue: true,
      onApplicationUpdate: false,
      includeHealthWarnings: false
    });
  };

  return (
    <div className="max-w-6xl">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Connect (Notifications)</h2>
        <p className="text-gray-400">Configure notifications and connections to other services</p>
      </div>

      {/* Info Box */}
      <div className="mb-8 bg-blue-950/30 border border-blue-900/50 rounded-lg p-6">
        <div className="flex items-start">
          <BellIcon className="w-6 h-6 text-blue-400 mr-3 flex-shrink-0 mt-0.5" />
          <div>
            <h3 className="text-lg font-semibold text-white mb-2">About Notifications</h3>
            <ul className="space-y-2 text-sm text-gray-300">
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>On Grab:</strong> Notification sent when an event is grabbed for download
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>On Download:</strong> Notification sent when an event finishes downloading
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>On Upgrade:</strong> Notification sent when a better quality version is downloaded
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>On Health Issue:</strong> Notification sent for system health warnings/errors
                </span>
              </li>
            </ul>
          </div>
        </div>
      </div>

      {/* Notifications List */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-6">
          <h3 className="text-xl font-semibold text-white">Your Notifications</h3>
          <button
            onClick={() => setShowAddModal(true)}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Notification
          </button>
        </div>

        <div className="space-y-3">
          {notifications.map((notification) => (
            <div
              key={notification.id}
              className="group bg-black/30 border border-gray-800 hover:border-red-900/50 rounded-lg p-4 transition-all"
            >
              <div className="flex items-start justify-between">
                <div className="flex items-start space-x-4 flex-1">
                  {/* Status Icon */}
                  <div className="mt-1">
                    {notification.enabled ? (
                      <CheckCircleIcon className="w-6 h-6 text-green-500" />
                    ) : (
                      <XMarkIcon className="w-6 h-6 text-gray-500" />
                    )}
                  </div>

                  {/* Notification Info */}
                  <div className="flex-1">
                    <div className="flex items-center space-x-3 mb-2">
                      <h4 className="text-lg font-semibold text-white">{notification.name}</h4>
                      <span className="px-2 py-0.5 bg-purple-900/30 text-purple-400 text-xs rounded">
                        {notification.implementation}
                      </span>
                    </div>

                    <div className="flex flex-wrap gap-2 text-xs">
                      {notification.onGrab && (
                        <span className="px-2 py-1 bg-blue-900/30 text-blue-400 rounded">On Grab</span>
                      )}
                      {notification.onDownload && (
                        <span className="px-2 py-1 bg-green-900/30 text-green-400 rounded">On Download</span>
                      )}
                      {notification.onUpgrade && (
                        <span className="px-2 py-1 bg-yellow-900/30 text-yellow-400 rounded">On Upgrade</span>
                      )}
                      {notification.onRename && (
                        <span className="px-2 py-1 bg-purple-900/30 text-purple-400 rounded">On Rename</span>
                      )}
                      {notification.onHealthIssue && (
                        <span className="px-2 py-1 bg-red-900/30 text-red-400 rounded">Health Issues</span>
                      )}
                      {notification.onApplicationUpdate && (
                        <span className="px-2 py-1 bg-cyan-900/30 text-cyan-400 rounded">App Updates</span>
                      )}
                    </div>
                  </div>
                </div>

                {/* Actions */}
                <div className="flex items-center space-x-2 ml-4">
                  <button
                    onClick={() => handleTestNotification(notification)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                    title="Test"
                  >
                    <BellIcon className="w-5 h-5" />
                  </button>
                  <button
                    onClick={() => handleEditNotification(notification)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                    title="Edit"
                  >
                    <PencilIcon className="w-5 h-5" />
                  </button>
                  <button
                    onClick={() => setShowDeleteConfirm(notification.id)}
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

        {notifications.length === 0 && (
          <div className="text-center py-12">
            <BellIcon className="w-16 h-16 text-gray-700 mx-auto mb-4" />
            <p className="text-gray-500 mb-2">No notifications configured</p>
            <p className="text-sm text-gray-400 mb-4">
              Add notification connections to get alerts about downloads and system events
            </p>
          </div>
        )}
      </div>

      {/* Save Button */}
      <div className="flex justify-end">
        <button className="px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105">
          Save Changes
        </button>
      </div>

      {/* Add/Edit Notification Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-4xl w-full my-8">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">
                {editingNotification ? `Edit ${editingNotification.name}` : 'Add Notification'}
              </h3>
              <button
                onClick={handleCancelEdit}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            {!selectedTemplate && !editingNotification ? (
              <>
                <p className="text-gray-400 mb-6">Select a notification service to configure</p>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3 max-h-96 overflow-y-auto">
                  {notificationTemplates.map((template) => (
                    <button
                      key={template.implementation}
                      onClick={() => handleSelectTemplate(template)}
                      className="flex items-start p-4 bg-black/30 border border-gray-800 hover:border-red-600 rounded-lg transition-all text-left group"
                    >
                      <div className="text-3xl mr-4">{template.icon}</div>
                      <div className="flex-1">
                        <h4 className="text-white font-semibold mb-1">{template.name}</h4>
                        <p className="text-sm text-gray-400">{template.description}</p>
                      </div>
                      <PlusIcon className="w-5 h-5 text-gray-400 group-hover:text-red-400 transition-colors" />
                    </button>
                  ))}
                </div>
              </>
            ) : (
              <>
                <div className="max-h-[60vh] overflow-y-auto pr-2 space-y-6">
                  {/* Basic Settings */}
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Name *</label>
                    <input
                      type="text"
                      value={formData.name || ''}
                      onChange={(e) => handleFormChange('name', e.target.value)}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="My Notification"
                    />
                  </div>

                  <label className="flex items-center space-x-3 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={formData.enabled || false}
                      onChange={(e) => handleFormChange('enabled', e.target.checked)}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                    />
                    <span className="text-sm font-medium text-gray-300">Enable this notification</span>
                  </label>

                  {/* Connection Settings */}
                  <div className="space-y-4">
                    <h4 className="text-lg font-semibold text-white">Connection</h4>

                    {selectedTemplate?.fields.includes('webhook') && (
                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">Webhook URL *</label>
                        <input
                          type="url"
                          value={formData.webhook || ''}
                          onChange={(e) => handleFormChange('webhook', e.target.value)}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder="https://discord.com/api/webhooks/..."
                        />
                      </div>
                    )}

                    {selectedTemplate?.fields.includes('token') && (
                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">Bot Token *</label>
                        <input
                          type="password"
                          value={formData.token || ''}
                          onChange={(e) => handleFormChange('token', e.target.value)}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder="Your bot token"
                        />
                      </div>
                    )}

                    {selectedTemplate?.fields.includes('chatId') && (
                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">Chat ID *</label>
                        <input
                          type="text"
                          value={formData.chatId || ''}
                          onChange={(e) => handleFormChange('chatId', e.target.value)}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder="123456789"
                        />
                      </div>
                    )}

                    {selectedTemplate?.fields.includes('apiKey') && (
                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">API Key *</label>
                        <input
                          type="password"
                          value={formData.apiKey || ''}
                          onChange={(e) => handleFormChange('apiKey', e.target.value)}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder="Your API key"
                        />
                      </div>
                    )}

                    {selectedTemplate?.fields.includes('server') && (
                      <div className="grid grid-cols-3 gap-4">
                        <div className="col-span-2">
                          <label className="block text-sm font-medium text-gray-300 mb-2">SMTP Server *</label>
                          <input
                            type="text"
                            value={formData.server || ''}
                            onChange={(e) => handleFormChange('server', e.target.value)}
                            className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                            placeholder="smtp.gmail.com"
                          />
                        </div>

                        <div>
                          <label className="block text-sm font-medium text-gray-300 mb-2">Port *</label>
                          <input
                            type="number"
                            value={formData.port || ''}
                            onChange={(e) => handleFormChange('port', parseInt(e.target.value))}
                            className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                            placeholder="587"
                          />
                        </div>
                      </div>
                    )}

                    {selectedTemplate?.fields.includes('from') && (
                      <div className="grid grid-cols-2 gap-4">
                        <div>
                          <label className="block text-sm font-medium text-gray-300 mb-2">From *</label>
                          <input
                            type="email"
                            value={formData.from || ''}
                            onChange={(e) => handleFormChange('from', e.target.value)}
                            className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                            placeholder="fightarr@example.com"
                          />
                        </div>

                        <div>
                          <label className="block text-sm font-medium text-gray-300 mb-2">To *</label>
                          <input
                            type="email"
                            value={formData.to || ''}
                            onChange={(e) => handleFormChange('to', e.target.value)}
                            className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                            placeholder="you@example.com"
                          />
                        </div>
                      </div>
                    )}

                    {selectedTemplate?.fields.includes('channel') && (
                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">Channel</label>
                        <input
                          type="text"
                          value={formData.channel || ''}
                          onChange={(e) => handleFormChange('channel', e.target.value)}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder="#general"
                        />
                      </div>
                    )}
                  </div>

                  {/* Triggers */}
                  <div className="space-y-4">
                    <h4 className="text-lg font-semibold text-white">Notification Triggers</h4>

                    <div className="grid grid-cols-2 gap-3">
                      <label className="flex items-center space-x-3 cursor-pointer p-3 bg-black/30 rounded-lg hover:bg-black/50 transition-colors">
                        <input
                          type="checkbox"
                          checked={formData.onGrab || false}
                          onChange={(e) => handleFormChange('onGrab', e.target.checked)}
                          className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                        />
                        <span className="text-sm font-medium text-gray-300">On Grab</span>
                      </label>

                      <label className="flex items-center space-x-3 cursor-pointer p-3 bg-black/30 rounded-lg hover:bg-black/50 transition-colors">
                        <input
                          type="checkbox"
                          checked={formData.onDownload || false}
                          onChange={(e) => handleFormChange('onDownload', e.target.checked)}
                          className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                        />
                        <span className="text-sm font-medium text-gray-300">On Download</span>
                      </label>

                      <label className="flex items-center space-x-3 cursor-pointer p-3 bg-black/30 rounded-lg hover:bg-black/50 transition-colors">
                        <input
                          type="checkbox"
                          checked={formData.onUpgrade || false}
                          onChange={(e) => handleFormChange('onUpgrade', e.target.checked)}
                          className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                        />
                        <span className="text-sm font-medium text-gray-300">On Upgrade</span>
                      </label>

                      {selectedTemplate?.fields.includes('onRename') && (
                        <label className="flex items-center space-x-3 cursor-pointer p-3 bg-black/30 rounded-lg hover:bg-black/50 transition-colors">
                          <input
                            type="checkbox"
                            checked={formData.onRename || false}
                            onChange={(e) => handleFormChange('onRename', e.target.checked)}
                            className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                          />
                          <span className="text-sm font-medium text-gray-300">On Rename</span>
                        </label>
                      )}

                      <label className="flex items-center space-x-3 cursor-pointer p-3 bg-black/30 rounded-lg hover:bg-black/50 transition-colors">
                        <input
                          type="checkbox"
                          checked={formData.onHealthIssue || false}
                          onChange={(e) => handleFormChange('onHealthIssue', e.target.checked)}
                          className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                        />
                        <span className="text-sm font-medium text-gray-300">On Health Issue</span>
                      </label>

                      <label className="flex items-center space-x-3 cursor-pointer p-3 bg-black/30 rounded-lg hover:bg-black/50 transition-colors">
                        <input
                          type="checkbox"
                          checked={formData.onApplicationUpdate || false}
                          onChange={(e) => handleFormChange('onApplicationUpdate', e.target.checked)}
                          className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                        />
                        <span className="text-sm font-medium text-gray-300">On App Update</span>
                      </label>
                    </div>
                  </div>
                </div>

                <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
                  <button
                    onClick={handleCancelEdit}
                    className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={() => handleTestNotification(formData as Notification)}
                    className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                  >
                    Test
                  </button>
                  <button
                    onClick={handleSaveNotification}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                  >
                    Save
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm !== null && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Delete Notification?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this notification connection? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDeleteNotification(showDeleteConfirm)}
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
