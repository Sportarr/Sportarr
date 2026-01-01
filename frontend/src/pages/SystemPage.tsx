import { useState } from 'react';
import { HeartIcon, ClipboardDocumentIcon, CheckIcon } from '@heroicons/react/24/outline';
import { useSystemStatus } from '../api/hooks';

export default function SystemPage() {
  const { data: status, isLoading, error } = useSystemStatus();
  const [btcCopied, setBtcCopied] = useState(false);
  const [infoCopied, setInfoCopied] = useState(false);

  const btcAddress = 'bc1qtruhe6ffsa6gmcjd46kgufggqvgfx3tvq6ykwq';

  const copyBtcAddress = async () => {
    try {
      await navigator.clipboard.writeText(btcAddress);
      setBtcCopied(true);
      setTimeout(() => setBtcCopied(false), 2000);
    } catch (err) {
      // Fallback for older browsers or non-secure contexts
      const textArea = document.createElement('textarea');
      textArea.value = btcAddress;
      textArea.style.position = 'fixed';
      textArea.style.left = '-999999px';
      document.body.appendChild(textArea);
      textArea.select();
      document.execCommand('copy');
      document.body.removeChild(textArea);
      setBtcCopied(true);
      setTimeout(() => setBtcCopied(false), 2000);
    }
  };

  const copySystemInfo = async () => {
    if (!status) return;

    const systemInfo = `### System Information
| Property | Value |
|----------|-------|
| Version | ${status.version} |
| Branch | ${status.branch} |
| OS | ${status.osName} ${status.osVersion} |
| Runtime | ${status.runtimeVersion} |
| Database | ${status.databaseType} ${status.databaseVersion} |
| Migration Version | ${status.migrationVersion} |
| Docker | ${status.isDocker ? 'Yes' : 'No'} |
| Production | ${status.isProduction ? 'Yes' : 'No'} |
| Authentication | ${status.authentication} |
| Start Time | ${new Date(status.startTime).toISOString()} |
| Build Time | ${new Date(status.buildTime).toISOString()} |
`;

    try {
      await navigator.clipboard.writeText(systemInfo);
      setInfoCopied(true);
      setTimeout(() => setInfoCopied(false), 2000);
    } catch (err) {
      // Fallback for older browsers or non-secure contexts
      const textArea = document.createElement('textarea');
      textArea.value = systemInfo;
      textArea.style.position = 'fixed';
      textArea.style.left = '-999999px';
      document.body.appendChild(textArea);
      textArea.select();
      document.execCommand('copy');
      document.body.removeChild(textArea);
      setInfoCopied(true);
      setTimeout(() => setInfoCopied(false), 2000);
    }
  };

  if (isLoading) {
    return (
      <div className="p-8">
        <div className="flex items-center justify-center h-64">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-8">
        <div className="bg-red-900 border border-red-700 text-red-100 px-4 py-3 rounded">
          <p className="font-bold">Error loading system status</p>
          <p className="text-sm">{(error as Error).message}</p>
        </div>
      </div>
    );
  }

  if (!status) {
    return null;
  }

  const infoItems = [
    { label: 'Version', value: status.version },
    { label: 'Build Time', value: new Date(status.buildTime).toLocaleString() },
    { label: 'Start Time', value: new Date(status.startTime).toLocaleString() },
    { label: 'Runtime', value: status.runtimeVersion },
    { label: 'Database', value: `${status.databaseType} ${status.databaseVersion}` },
    { label: 'OS', value: `${status.osName} ${status.osVersion}` },
    { label: 'Branch', value: status.branch },
    { label: 'Authentication', value: status.authentication },
    { label: 'Is Docker', value: status.isDocker ? 'Yes' : 'No' },
    { label: 'Is Production', value: status.isProduction ? 'Yes' : 'No' },
    { label: 'Data Directory', value: status.appData },
  ];

  return (
    <div className="p-8">
      <div className="max-w-6xl mx-auto">
        <div className="mb-6 flex justify-between items-start">
          <div>
            <h1 className="text-3xl font-bold text-white mb-2">System Status</h1>
            <p className="text-gray-400">View system information and application status</p>
          </div>
          <button
            onClick={copySystemInfo}
            className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white font-medium rounded-lg transition-colors flex items-center gap-2"
            title="Copy system info for GitHub issues"
          >
            {infoCopied ? (
              <>
                <CheckIcon className="w-5 h-5 text-green-400" />
                Copied!
              </>
            ) : (
              <>
                <ClipboardDocumentIcon className="w-5 h-5" />
                Copy for GitHub Issue
              </>
            )}
          </button>
        </div>

        <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg shadow-xl overflow-hidden">
          <div className="px-6 py-4 bg-red-950/30 border-b border-red-900/30">
            <h2 className="text-xl font-semibold text-white">{status.appName}</h2>
          </div>
          <div className="divide-y divide-red-900/20">
            {infoItems.map((item) => (
              <div
                key={item.label}
                className="px-6 py-4 flex justify-between items-center hover:bg-red-900/10 transition-colors"
              >
                <span className="text-gray-400 font-medium">{item.label}</span>
                <span className="font-mono text-sm text-white">{item.value}</span>
              </div>
            ))}
          </div>
        </div>

        <div className="mt-8 grid grid-cols-1 md:grid-cols-3 gap-6">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6 shadow-xl">
            <h3 className="text-sm font-medium text-gray-400 mb-2">Status</h3>
            <p className="text-2xl font-bold text-green-400">Running</p>
          </div>
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6 shadow-xl">
            <h3 className="text-sm font-medium text-gray-400 mb-2">Mode</h3>
            <p className="text-2xl font-bold text-white">
              {status.isProduction ? 'Production' : 'Development'}
            </p>
          </div>
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6 shadow-xl">
            <h3 className="text-sm font-medium text-gray-400 mb-2">
              Migration Version
            </h3>
            <p className="text-2xl font-bold text-white">{status.migrationVersion}</p>
          </div>
        </div>

        {/* Support Section */}
        <div className="mt-12 mb-8">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-8 shadow-xl text-center">
            <p className="text-gray-300 text-lg mb-6">
              Sportarr is a free, open-source project made for sports fans, by sports fans.
              <br />
              If it's helped you catch every game, consider throwing some support our way!
            </p>
            <div className="flex flex-col sm:flex-row items-center justify-center gap-4 mb-6">
              {/* GitHub Sponsors Button */}
              <a
                href="#" // TODO: Add GitHub Sponsors URL
                target="_blank"
                rel="noopener noreferrer"
                className="group relative px-6 py-3 bg-red-600 hover:bg-red-500 text-white font-semibold rounded-lg transition-all duration-300 flex items-center gap-2 shadow-lg shadow-red-600/30 hover:shadow-red-500/50 hover:scale-105 animate-pulse hover:animate-none"
                style={{
                  boxShadow: '0 0 20px rgba(220, 38, 38, 0.4), 0 4px 6px -1px rgba(0, 0, 0, 0.1)',
                }}
              >
                <HeartIcon className="w-5 h-5" />
                Sponsor the Team
              </a>

              {/* Ko-fi Button */}
              <a
                href="#" // TODO: Add Ko-fi URL
                target="_blank"
                rel="noopener noreferrer"
                className="group relative px-6 py-3 bg-red-600 hover:bg-red-500 text-white font-semibold rounded-lg transition-all duration-300 flex items-center gap-2 shadow-lg shadow-red-600/30 hover:shadow-red-500/50 hover:scale-105 animate-pulse hover:animate-none"
                style={{
                  boxShadow: '0 0 20px rgba(220, 38, 38, 0.4), 0 4px 6px -1px rgba(0, 0, 0, 0.1)',
                }}
              >
                <span className="text-lg">☕</span>
                Buy Me a Coffee
              </a>

              {/* Bitcoin Button */}
              <button
                onClick={copyBtcAddress}
                className="group relative px-6 py-3 bg-red-600 hover:bg-red-500 text-white font-semibold rounded-lg transition-all duration-300 flex items-center gap-2 shadow-lg shadow-red-600/30 hover:shadow-red-500/50 hover:scale-105 animate-pulse hover:animate-none"
                style={{
                  boxShadow: '0 0 20px rgba(220, 38, 38, 0.4), 0 4px 6px -1px rgba(0, 0, 0, 0.1)',
                }}
                title="Click to copy BTC address"
              >
                <span className="text-lg">₿</span>
                {btcCopied ? (
                  <>
                    <CheckIcon className="w-5 h-5 text-green-300" />
                    Copied!
                  </>
                ) : (
                  <>
                    Send Bitcoin
                    <ClipboardDocumentIcon className="w-4 h-4 opacity-70" />
                  </>
                )}
              </button>
            </div>

            {/* BTC Address Display */}
            <div className="bg-gray-800/50 rounded-lg p-3 max-w-lg mx-auto">
              <p className="text-xs text-gray-500 mb-1">BTC Wallet Address</p>
              <p className="text-sm text-gray-300 font-mono break-all select-all">{btcAddress}</p>
            </div>

            <p className="text-gray-500 text-sm mt-6">
              Every contribution helps keep the scoreboard running. Thanks for being part of the team!
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
