import { useState, useEffect } from 'react';
import { ServerIcon, ShieldCheckIcon, FolderArrowDownIcon, ArrowPathIcon, ChartBarIcon, DocumentDuplicateIcon, CheckIcon, ExclamationTriangleIcon } from '@heroicons/react/24/outline';
import { apiGet, apiPost, apiPut, apiDelete } from '../../utils/api';

interface GeneralSettingsProps {
  showAdvanced: boolean;
}

// Settings interfaces matching backend models
interface HostSettings {
  bindAddress: string;
  port: number;
  urlBase: string;
  instanceName: string;
  enableSsl: boolean;
  sslPort: number;
  sslCertPath: string;
  sslCertPassword: string;
}

interface SecuritySettings {
  authenticationMethod: string;
  authenticationRequired: string;
  username: string;
  password: string;
  apiKey: string;
  certificateValidation: string;
}

interface ProxySettings {
  useProxy: boolean;
  proxyType: string;
  proxyHostname: string;
  proxyPort: number;
  proxyUsername: string;
  proxyPassword: string;
  proxyBypassFilter: string;
  proxyBypassLocalAddresses: boolean;
}

interface LoggingSettings {
  logLevel: string;
}

interface AnalyticsSettings {
  sendAnonymousUsageData: boolean;
}

interface BackupSettings {
  backupFolder: string;
  backupInterval: number;
  backupRetention: number;
}

interface UpdateSettings {
  branch: string;
  automatic: boolean;
  mechanism: string;
  scriptPath: string;
}

export default function GeneralSettings({ showAdvanced }: GeneralSettingsProps) {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [apiKeyCopied, setApiKeyCopied] = useState(false);
  const [apiKeyRegenerated, setApiKeyRegenerated] = useState(false);

  // Host Settings
  const [hostSettings, setHostSettings] = useState<HostSettings>({
    bindAddress: '*',
    port: 7878,
    urlBase: '',
    instanceName: 'Fightarr',
    enableSsl: false,
    sslPort: 9898,
    sslCertPath: '',
    sslCertPassword: '',
  });

  // Security Settings
  const [securitySettings, setSecuritySettings] = useState<SecuritySettings>({
    authenticationMethod: 'none',
    authenticationRequired: 'disabledForLocalAddresses',
    username: '',
    password: '',
    apiKey: 'd290f1ee-6c54-4b01-90e6-d701748f0851',
    certificateValidation: 'enabled',
  });

  // Proxy Settings
  const [proxySettings, setProxySettings] = useState<ProxySettings>({
    useProxy: false,
    proxyType: 'http',
    proxyHostname: '',
    proxyPort: 8080,
    proxyUsername: '',
    proxyPassword: '',
    proxyBypassFilter: '',
    proxyBypassLocalAddresses: true,
  });

  // Logging Settings
  const [loggingSettings, setLoggingSettings] = useState<LoggingSettings>({
    logLevel: 'info',
  });

  // Analytics Settings
  const [analyticsSettings, setAnalyticsSettings] = useState<AnalyticsSettings>({
    sendAnonymousUsageData: false,
  });

  // Backup Settings
  const [backupSettings, setBackupSettings] = useState<BackupSettings>({
    backupFolder: '',
    backupInterval: 7,
    backupRetention: 28,
  });

  // Update Settings
  const [updateSettings, setUpdateSettings] = useState<UpdateSettings>({
    branch: 'main',
    automatic: false,
    mechanism: 'docker',
    scriptPath: '',
  });

  // Load settings from API on mount
  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    try {
      const response = await apiGet('/api/settings');
      if (response.ok) {
        const data = await response.json();

        // Parse each settings category from JSON
        if (data.hostSettings) {
          setHostSettings(JSON.parse(data.hostSettings));
        }
        if (data.securitySettings) {
          setSecuritySettings(JSON.parse(data.securitySettings));
        }
        if (data.proxySettings) {
          setProxySettings(JSON.parse(data.proxySettings));
        }
        if (data.loggingSettings) {
          setLoggingSettings(JSON.parse(data.loggingSettings));
        }
        if (data.analyticsSettings) {
          setAnalyticsSettings(JSON.parse(data.analyticsSettings));
        }
        if (data.backupSettings) {
          setBackupSettings(JSON.parse(data.backupSettings));
        }
        if (data.updateSettings) {
          setUpdateSettings(JSON.parse(data.updateSettings));
        }
      }
    } catch (error) {
      console.error('Failed to load settings:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      // First fetch current settings
      const response = await apiGet('/api/settings');
      if (!response.ok) throw new Error('Failed to fetch current settings');

      const currentSettings = await response.json();

      // Update with new values
      const updatedSettings = {
        ...currentSettings,
        hostSettings: JSON.stringify(hostSettings),
        securitySettings: JSON.stringify(securitySettings),
        proxySettings: JSON.stringify(proxySettings),
        loggingSettings: JSON.stringify(loggingSettings),
        analyticsSettings: JSON.stringify(analyticsSettings),
        backupSettings: JSON.stringify(backupSettings),
        updateSettings: JSON.stringify(updateSettings),
      };

      // Save to API
      await apiPut('/api/settings', updatedSettings);

      // Note: We intentionally keep the apiKeyRegenerated warning visible even after saving
      // because the user still needs to restart Fightarr for the new API key to take effect
    } catch (error) {
      console.error('Failed to save settings:', error);
    } finally {
      setSaving(false);
    }
  };

  const copyApiKey = async () => {
    try {
      if (!securitySettings.apiKey) {
        console.error('No API key to copy');
        return;
      }
      if (!navigator.clipboard) {
        console.error('Clipboard API not available');
        return;
      }
      await navigator.clipboard.writeText(securitySettings.apiKey);
      setApiKeyCopied(true);
      setTimeout(() => setApiKeyCopied(false), 2000);
    } catch (err) {
      console.error('Failed to copy API key:', err);
    }
  };

  const generateNewApiKey = () => {
    const newKey = 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = Math.random() * 16 | 0;
      const v = c === 'x' ? r : (r & 0x3 | 0x8);
      return v.toString(16);
    });
    setSecuritySettings(prev => ({ ...prev, apiKey: newKey }));
    setApiKeyRegenerated(true);
  };

  if (loading) {
    return (
      <div className="max-w-4xl mx-auto">
        <div className="mb-8">
          <h2 className="text-3xl font-bold text-white mb-2">General</h2>
          <p className="text-gray-400">General application settings</p>
        </div>
        <div className="text-center py-12">
          <p className="text-gray-500">Loading settings...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">General</h2>
        <p className="text-gray-400">General application settings</p>
      </div>

      {/* Host */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center mb-4">
          <ServerIcon className="w-6 h-6 text-red-400 mr-3" />
          <h3 className="text-xl font-semibold text-white">Host</h3>
        </div>

        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-white font-medium mb-2">Bind Address</label>
              <input
                type="text"
                value={hostSettings.bindAddress}
                onChange={(e) => setHostSettings(prev => ({ ...prev, bindAddress: e.target.value }))}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                placeholder="*"
              />
              <p className="text-xs text-gray-500 mt-1">
                * = all interfaces, localhost = local only
              </p>
            </div>

            <div>
              <label className="block text-white font-medium mb-2">Port Number</label>
              <input
                type="number"
                value={hostSettings.port}
                onChange={(e) => setHostSettings(prev => ({ ...prev, port: Number(e.target.value) }))}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              />
              <p className="text-xs text-gray-500 mt-1">
                Restart required to apply
              </p>
            </div>
          </div>

          <div>
            <label className="block text-white font-medium mb-2">URL Base</label>
            <input
              type="text"
              value={hostSettings.urlBase}
              onChange={(e) => setHostSettings(prev => ({ ...prev, urlBase: e.target.value }))}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              placeholder="/fightarr"
            />
            <p className="text-xs text-gray-500 mt-1">
              For reverse proxy support, leave empty if not using a reverse proxy
            </p>
          </div>

          <div>
            <label className="block text-white font-medium mb-2">Instance Name</label>
            <input
              type="text"
              value={hostSettings.instanceName}
              onChange={(e) => setHostSettings(prev => ({ ...prev, instanceName: e.target.value }))}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            />
            <p className="text-xs text-gray-500 mt-1">
              Instance name in browser tab and notifications
            </p>
          </div>

          {showAdvanced && (
            <>
              <label className="flex items-start space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={hostSettings.enableSsl}
                  onChange={(e) => setHostSettings(prev => ({ ...prev, enableSsl: e.target.checked }))}
                  className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <div>
                  <span className="text-white font-medium">Enable SSL</span>
                  <p className="text-sm text-gray-400 mt-1">
                    Requires restart. Use SSL to access Fightarr (https://)
                  </p>
                  <span className="inline-block mt-1 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                    Advanced
                  </span>
                </div>
              </label>

              {hostSettings.enableSsl && (
                <div className="ml-8 space-y-4 p-4 bg-black/30 rounded-lg">
                  <div>
                    <label className="block text-white font-medium mb-2">SSL Port</label>
                    <input
                      type="number"
                      value={hostSettings.sslPort}
                      onChange={(e) => setHostSettings(prev => ({ ...prev, sslPort: Number(e.target.value) }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                  </div>

                  <div>
                    <label className="block text-white font-medium mb-2">SSL Certificate Path</label>
                    <input
                      type="text"
                      value={hostSettings.sslCertPath}
                      onChange={(e) => setHostSettings(prev => ({ ...prev, sslCertPath: e.target.value }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="/path/to/cert.pfx"
                    />
                  </div>

                  <div>
                    <label className="block text-white font-medium mb-2">SSL Certificate Password</label>
                    <input
                      type="password"
                      value={hostSettings.sslCertPassword}
                      onChange={(e) => setHostSettings(prev => ({ ...prev, sslCertPassword: e.target.value }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {/* Security */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center mb-4">
          <ShieldCheckIcon className="w-6 h-6 text-red-400 mr-3" />
          <h3 className="text-xl font-semibold text-white">Security</h3>
        </div>

        <div className="space-y-4">
          <div className="p-4 bg-blue-900/20 border border-blue-900/30 rounded-lg">
            <p className="text-blue-300 text-sm">
              <strong>Authentication is always required.</strong> Change your username and password below, then save. You'll need to login again with the new credentials.
            </p>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-white font-medium mb-2">Username</label>
              <input
                type="text"
                value={securitySettings.username}
                onChange={(e) => setSecuritySettings(prev => ({ ...prev, username: e.target.value }))}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                autoComplete="username"
                placeholder="Enter new username"
              />
              <p className="text-xs text-gray-500 mt-1">
                Current username will be replaced when you save
              </p>
            </div>

            <div>
              <label className="block text-white font-medium mb-2">Password</label>
              <input
                type="password"
                value={securitySettings.password}
                onChange={(e) => setSecuritySettings(prev => ({ ...prev, password: e.target.value }))}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                autoComplete="new-password"
                placeholder="Enter new password (min 6 chars)"
              />
              <p className="text-xs text-gray-500 mt-1">
                Leave blank to keep current password
              </p>
            </div>
          </div>

          <div>
            <label className="block text-white font-medium mb-2">API Key</label>
            <div className="flex items-center space-x-2">
              <input
                type="text"
                value={securitySettings.apiKey}
                readOnly
                className="flex-1 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none font-mono text-sm"
              />
              <button
                onClick={copyApiKey}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors flex items-center space-x-2"
                title="Copy API Key"
              >
                {apiKeyCopied ? (
                  <>
                    <CheckIcon className="w-5 h-5" />
                    <span>Copied!</span>
                  </>
                ) : (
                  <>
                    <DocumentDuplicateIcon className="w-5 h-5" />
                    <span>Copy</span>
                  </>
                )}
              </button>
              <button
                onClick={generateNewApiKey}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Regenerate
              </button>
            </div>
            {apiKeyRegenerated && (
              <div className="mt-2 bg-yellow-950/30 border border-yellow-900/50 rounded-lg p-3 flex items-start">
                <ExclamationTriangleIcon className="w-5 h-5 text-yellow-400 mr-2 flex-shrink-0 mt-0.5" />
                <p className="text-sm text-yellow-300">
                  <strong>Restart Required:</strong> You must restart Fightarr for the new API key to take effect. Save your settings first, then restart the application.
                </p>
              </div>
            )}
            <p className="text-xs text-gray-500 mt-1">
              Used by apps and scripts to access Fightarr API
            </p>
          </div>

          {showAdvanced && (
            <div>
              <label className="block text-white font-medium mb-2">Certificate Validation</label>
              <select
                value={securitySettings.certificateValidation}
                onChange={(e) => setSecuritySettings(prev => ({ ...prev, certificateValidation: e.target.value }))}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              >
                <option value="enabled">Enabled</option>
                <option value="disabledForLocalAddresses">Disabled For Local Addresses</option>
                <option value="disabled">Disabled</option>
              </select>
              <p className="text-xs text-gray-500 mt-1">
                Change how strict HTTPS certificate validation is
              </p>
              <span className="inline-block mt-1 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                Advanced
              </span>
            </div>
          )}
        </div>
      </div>

      {/* Proxy */}
      {showAdvanced && (
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-yellow-900/30 rounded-lg p-6">
          <h3 className="text-xl font-semibold text-white mb-4">
            Proxy Settings
            <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
              Advanced
            </span>
          </h3>

          <div className="space-y-4">
            <label className="flex items-start space-x-3 cursor-pointer">
              <input
                type="checkbox"
                checked={proxySettings.useProxy}
                onChange={(e) => setProxySettings(prev => ({ ...prev, useProxy: e.target.checked }))}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-white font-medium">Use Proxy</span>
                <p className="text-sm text-gray-400 mt-1">
                  Route requests through a proxy server
                </p>
              </div>
            </label>

            {proxySettings.useProxy && (
              <div className="ml-8 space-y-4 p-4 bg-black/30 rounded-lg">
                <div>
                  <label className="block text-white font-medium mb-2">Proxy Type</label>
                  <select
                    value={proxySettings.proxyType}
                    onChange={(e) => setProxySettings(prev => ({ ...prev, proxyType: e.target.value }))}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  >
                    <option value="http">HTTP(S)</option>
                    <option value="socks4">SOCKS4</option>
                    <option value="socks5">SOCKS5</option>
                  </select>
                </div>

                <div className="grid grid-cols-3 gap-4">
                  <div className="col-span-2">
                    <label className="block text-white font-medium mb-2">Hostname</label>
                    <input
                      type="text"
                      value={proxySettings.proxyHostname}
                      onChange={(e) => setProxySettings(prev => ({ ...prev, proxyHostname: e.target.value }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="proxy.example.com"
                    />
                  </div>

                  <div>
                    <label className="block text-white font-medium mb-2">Port</label>
                    <input
                      type="number"
                      value={proxySettings.proxyPort}
                      onChange={(e) => setProxySettings(prev => ({ ...prev, proxyPort: Number(e.target.value) }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-white font-medium mb-2">Username</label>
                    <input
                      type="text"
                      value={proxySettings.proxyUsername}
                      onChange={(e) => setProxySettings(prev => ({ ...prev, proxyUsername: e.target.value }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="Optional"
                    />
                  </div>

                  <div>
                    <label className="block text-white font-medium mb-2">Password</label>
                    <input
                      type="password"
                      value={proxySettings.proxyPassword}
                      onChange={(e) => setProxySettings(prev => ({ ...prev, proxyPassword: e.target.value }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="Optional"
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-white font-medium mb-2">Ignored Addresses</label>
                  <input
                    type="text"
                    value={proxySettings.proxyBypassFilter}
                    onChange={(e) => setProxySettings(prev => ({ ...prev, proxyBypassFilter: e.target.value }))}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    placeholder="localhost,127.0.0.1"
                  />
                  <p className="text-xs text-gray-500 mt-1">
                    Comma separated list of addresses that bypass the proxy
                  </p>
                </div>

                <label className="flex items-center space-x-3 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={proxySettings.proxyBypassLocalAddresses}
                    onChange={(e) => setProxySettings(prev => ({ ...prev, proxyBypassLocalAddresses: e.target.checked }))}
                    className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                  />
                  <span className="text-sm text-gray-300">Bypass Proxy for Local Addresses</span>
                </label>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Logging */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <h3 className="text-xl font-semibold text-white mb-4">Logging</h3>

        <div>
          <label className="block text-white font-medium mb-2">Log Level</label>
          <select
            value={loggingSettings.logLevel}
            onChange={(e) => setLoggingSettings(prev => ({ ...prev, logLevel: e.target.value }))}
            className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
          >
            <option value="trace">Trace</option>
            <option value="debug">Debug</option>
            <option value="info">Info</option>
            <option value="warn">Warn</option>
            <option value="error">Error</option>
          </select>
          <p className="text-xs text-gray-500 mt-1">
            Trace/Debug logs should only be enabled when troubleshooting issues
          </p>
        </div>
      </div>

      {/* Analytics */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center mb-4">
          <ChartBarIcon className="w-6 h-6 text-red-400 mr-3" />
          <h3 className="text-xl font-semibold text-white">Analytics</h3>
        </div>

        <label className="flex items-start space-x-3 cursor-pointer">
          <input
            type="checkbox"
            checked={analyticsSettings.sendAnonymousUsageData}
            onChange={(e) => setAnalyticsSettings(prev => ({ ...prev, sendAnonymousUsageData: e.target.checked }))}
            className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
          />
          <div>
            <span className="text-white font-medium">Send Anonymous Usage Data</span>
            <p className="text-sm text-gray-400 mt-1">
              Help improve Fightarr by sending anonymous usage statistics. No personal information is collected.
            </p>
          </div>
        </label>
      </div>

      {/* Backups */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center mb-4">
          <FolderArrowDownIcon className="w-6 h-6 text-red-400 mr-3" />
          <h3 className="text-xl font-semibold text-white">Backups</h3>
        </div>

        <div className="space-y-4">
          <div>
            <label className="block text-white font-medium mb-2">Backup Folder</label>
            <input
              type="text"
              value={backupSettings.backupFolder}
              onChange={(e) => setBackupSettings(prev => ({ ...prev, backupFolder: e.target.value }))}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              placeholder="/config/backups"
            />
            <p className="text-xs text-gray-500 mt-1">
              Folder to store backup files (relative to AppData directory)
            </p>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-white font-medium mb-2">Backup Interval</label>
              <div className="flex items-center space-x-2">
                <input
                  type="number"
                  value={backupSettings.backupInterval}
                  onChange={(e) => setBackupSettings(prev => ({ ...prev, backupInterval: Number(e.target.value) }))}
                  className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  min="1"
                />
                <span className="text-gray-400">days</span>
              </div>
            </div>

            <div>
              <label className="block text-white font-medium mb-2">Backup Retention</label>
              <div className="flex items-center space-x-2">
                <input
                  type="number"
                  value={backupSettings.backupRetention}
                  onChange={(e) => setBackupSettings(prev => ({ ...prev, backupRetention: Number(e.target.value) }))}
                  className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  min="1"
                />
                <span className="text-gray-400">days</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Updates */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center mb-4">
          <ArrowPathIcon className="w-6 h-6 text-red-400 mr-3" />
          <h3 className="text-xl font-semibold text-white">Updates</h3>
        </div>

        <div className="space-y-4">
          <div>
            <label className="block text-white font-medium mb-2">Branch</label>
            <select
              value={updateSettings.branch}
              onChange={(e) => setUpdateSettings(prev => ({ ...prev, branch: e.target.value }))}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            >
              <option value="main">Main (Stable)</option>
              <option value="develop">Develop (Beta)</option>
            </select>
            <p className="text-xs text-gray-500 mt-1">
              Branch to receive updates from
            </p>
          </div>

          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={updateSettings.automatic}
              onChange={(e) => setUpdateSettings(prev => ({ ...prev, automatic: e.target.checked }))}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Automatic Updates</span>
              <p className="text-sm text-gray-400 mt-1">
                Automatically download and install updates
              </p>
            </div>
          </label>

          <div>
            <label className="block text-white font-medium mb-2">Mechanism</label>
            <select
              value={updateSettings.mechanism}
              onChange={(e) => setUpdateSettings(prev => ({ ...prev, mechanism: e.target.value }))}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            >
              <option value="built-in">Built-in</option>
              <option value="script">Script</option>
              <option value="docker">Docker</option>
              <option value="apt">Apt</option>
              <option value="external">External</option>
            </select>
          </div>

          {updateSettings.mechanism === 'script' && (
            <div>
              <label className="block text-white font-medium mb-2">Script Path</label>
              <input
                type="text"
                value={updateSettings.scriptPath}
                onChange={(e) => setUpdateSettings(prev => ({ ...prev, scriptPath: e.target.value }))}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                placeholder="/path/to/update/script.sh"
              />
            </div>
          )}
        </div>
      </div>

      {/* Save Button */}
      <div className="flex justify-end">
        <button
          onClick={handleSave}
          disabled={saving}
          className="px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {saving ? 'Saving...' : 'Save Changes'}
        </button>
      </div>
    </div>
  );
}
