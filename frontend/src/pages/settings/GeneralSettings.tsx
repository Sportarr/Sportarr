import { useState } from 'react';
import { ServerIcon, ShieldCheckIcon, FolderArrowDownIcon, ArrowPathIcon, ChartBarIcon } from '@heroicons/react/24/outline';

interface GeneralSettingsProps {
  showAdvanced: boolean;
}

export default function GeneralSettings({ showAdvanced }: GeneralSettingsProps) {
  // Host
  const [bindAddress, setBindAddress] = useState('*');
  const [port, setPort] = useState(7878);
  const [urlBase, setUrlBase] = useState('');
  const [instanceName, setInstanceName] = useState('Fightarr');
  const [enableSsl, setEnableSsl] = useState(false);
  const [sslPort, setSslPort] = useState(9898);
  const [sslCertPath, setSslCertPath] = useState('');
  const [sslCertPassword, setSslCertPassword] = useState('');

  // Security
  const [authenticationMethod, setAuthenticationMethod] = useState('none');
  const [authenticationRequired, setAuthenticationRequired] = useState('disabled');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [apiKey, setApiKey] = useState('d290f1ee-6c54-4b01-90e6-d701748f0851');
  const [certificateValidation, setCertificateValidation] = useState('enabled');

  // Proxy
  const [useProxy, setUseProxy] = useState(false);
  const [proxyType, setProxyType] = useState('http');
  const [proxyHostname, setProxyHostname] = useState('');
  const [proxyPort, setProxyPort] = useState(8080);
  const [proxyUsername, setProxyUsername] = useState('');
  const [proxyPassword, setProxyPassword] = useState('');
  const [proxyBypassFilter, setProxyBypassFilter] = useState('');
  const [proxyBypassLocalAddresses, setProxyBypassLocalAddresses] = useState(true);

  // Logging
  const [logLevel, setLogLevel] = useState('info');

  // Analytics
  const [sendAnonymousUsageData, setSendAnonymousUsageData] = useState(false);

  // Backups
  const [backupFolder, setBackupFolder] = useState('');
  const [backupInterval, setBackupInterval] = useState(7);
  const [backupRetention, setBackupRetention] = useState(28);

  // Updates
  const [branch, setBranch] = useState('main');
  const [automatic, setAutomatic] = useState(false);
  const [mechanism, setMechanism] = useState('docker');
  const [scriptPath, setScriptPath] = useState('');

  const generateNewApiKey = () => {
    const newKey = 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = Math.random() * 16 | 0;
      const v = c === 'x' ? r : (r & 0x3 | 0x8);
      return v.toString(16);
    });
    setApiKey(newKey);
  };

  return (
    <div className="max-w-4xl">
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
                value={bindAddress}
                onChange={(e) => setBindAddress(e.target.value)}
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
                value={port}
                onChange={(e) => setPort(Number(e.target.value))}
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
              value={urlBase}
              onChange={(e) => setUrlBase(e.target.value)}
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
              value={instanceName}
              onChange={(e) => setInstanceName(e.target.value)}
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
                  checked={enableSsl}
                  onChange={(e) => setEnableSsl(e.target.checked)}
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

              {enableSsl && (
                <div className="ml-8 space-y-4 p-4 bg-black/30 rounded-lg">
                  <div>
                    <label className="block text-white font-medium mb-2">SSL Port</label>
                    <input
                      type="number"
                      value={sslPort}
                      onChange={(e) => setSslPort(Number(e.target.value))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                  </div>

                  <div>
                    <label className="block text-white font-medium mb-2">SSL Certificate Path</label>
                    <input
                      type="text"
                      value={sslCertPath}
                      onChange={(e) => setSslCertPath(e.target.value)}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="/path/to/cert.pfx"
                    />
                  </div>

                  <div>
                    <label className="block text-white font-medium mb-2">SSL Certificate Password</label>
                    <input
                      type="password"
                      value={sslCertPassword}
                      onChange={(e) => setSslCertPassword(e.target.value)}
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
          <div>
            <label className="block text-white font-medium mb-2">Authentication</label>
            <select
              value={authenticationMethod}
              onChange={(e) => setAuthenticationMethod(e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            >
              <option value="none">None</option>
              <option value="basic">Basic (Browser Popup)</option>
              <option value="forms">Forms (Login Page)</option>
            </select>
          </div>

          {authenticationMethod !== 'none' && (
            <>
              <div>
                <label className="block text-white font-medium mb-2">Authentication Required</label>
                <select
                  value={authenticationRequired}
                  onChange={(e) => setAuthenticationRequired(e.target.value)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                >
                  <option value="disabled">Disabled For Local Addresses</option>
                  <option value="enabled">Enabled</option>
                </select>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-white font-medium mb-2">Username</label>
                  <input
                    type="text"
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    autoComplete="username"
                  />
                </div>

                <div>
                  <label className="block text-white font-medium mb-2">Password</label>
                  <input
                    type="password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    autoComplete="new-password"
                  />
                </div>
              </div>
            </>
          )}

          <div>
            <label className="block text-white font-medium mb-2">API Key</label>
            <div className="flex items-center space-x-2">
              <input
                type="text"
                value={apiKey}
                readOnly
                className="flex-1 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none"
              />
              <button
                onClick={generateNewApiKey}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Regenerate
              </button>
            </div>
            <p className="text-xs text-gray-500 mt-1">
              Used by apps and scripts to access Fightarr API
            </p>
          </div>

          {showAdvanced && (
            <div>
              <label className="block text-white font-medium mb-2">Certificate Validation</label>
              <select
                value={certificateValidation}
                onChange={(e) => setCertificateValidation(e.target.value)}
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
                checked={useProxy}
                onChange={(e) => setUseProxy(e.target.checked)}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-white font-medium">Use Proxy</span>
                <p className="text-sm text-gray-400 mt-1">
                  Route requests through a proxy server
                </p>
              </div>
            </label>

            {useProxy && (
              <div className="ml-8 space-y-4 p-4 bg-black/30 rounded-lg">
                <div>
                  <label className="block text-white font-medium mb-2">Proxy Type</label>
                  <select
                    value={proxyType}
                    onChange={(e) => setProxyType(e.target.value)}
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
                      value={proxyHostname}
                      onChange={(e) => setProxyHostname(e.target.value)}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="proxy.example.com"
                    />
                  </div>

                  <div>
                    <label className="block text-white font-medium mb-2">Port</label>
                    <input
                      type="number"
                      value={proxyPort}
                      onChange={(e) => setProxyPort(Number(e.target.value))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-white font-medium mb-2">Username</label>
                    <input
                      type="text"
                      value={proxyUsername}
                      onChange={(e) => setProxyUsername(e.target.value)}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="Optional"
                    />
                  </div>

                  <div>
                    <label className="block text-white font-medium mb-2">Password</label>
                    <input
                      type="password"
                      value={proxyPassword}
                      onChange={(e) => setProxyPassword(e.target.value)}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="Optional"
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-white font-medium mb-2">Ignored Addresses</label>
                  <input
                    type="text"
                    value={proxyBypassFilter}
                    onChange={(e) => setProxyBypassFilter(e.target.value)}
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
                    checked={proxyBypassLocalAddresses}
                    onChange={(e) => setProxyBypassLocalAddresses(e.target.checked)}
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
            value={logLevel}
            onChange={(e) => setLogLevel(e.target.value)}
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
            checked={sendAnonymousUsageData}
            onChange={(e) => setSendAnonymousUsageData(e.target.checked)}
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
              value={backupFolder}
              onChange={(e) => setBackupFolder(e.target.value)}
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
                  value={backupInterval}
                  onChange={(e) => setBackupInterval(Number(e.target.value))}
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
                  value={backupRetention}
                  onChange={(e) => setBackupRetention(Number(e.target.value))}
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
              value={branch}
              onChange={(e) => setBranch(e.target.value)}
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
              checked={automatic}
              onChange={(e) => setAutomatic(e.target.checked)}
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
              value={mechanism}
              onChange={(e) => setMechanism(e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            >
              <option value="built-in">Built-in</option>
              <option value="script">Script</option>
              <option value="docker">Docker</option>
              <option value="apt">Apt</option>
              <option value="external">External</option>
            </select>
          </div>

          {mechanism === 'script' && (
            <div>
              <label className="block text-white font-medium mb-2">Script Path</label>
              <input
                type="text"
                value={scriptPath}
                onChange={(e) => setScriptPath(e.target.value)}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                placeholder="/path/to/update/script.sh"
              />
            </div>
          )}
        </div>
      </div>

      {/* Save Button */}
      <div className="flex justify-end">
        <button className="px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105">
          Save Changes
        </button>
      </div>
    </div>
  );
}
