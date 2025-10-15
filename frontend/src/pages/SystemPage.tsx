import { useSystemStatus } from '../api/hooks';

export default function SystemPage() {
  const { data: status, isLoading, error } = useSystemStatus();

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
        <div className="mb-8">
          <h1 className="text-3xl font-bold text-white mb-2">System Status</h1>
          <p className="text-gray-400">View system information and application status</p>
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
      </div>
    </div>
  );
}
