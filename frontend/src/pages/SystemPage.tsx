import { useSystemStatus } from '../api/hooks';

export default function SystemPage() {
  const { data: status, isLoading, error } = useSystemStatus();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-500"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-red-900 border border-red-700 text-red-100 px-4 py-3 rounded">
        <p className="font-bold">Error loading system status</p>
        <p className="text-sm">{(error as Error).message}</p>
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
    <div>
      <h1 className="text-3xl font-bold mb-6">System Status</h1>

      <div className="bg-gray-800 rounded-lg shadow-lg overflow-hidden">
        <div className="px-6 py-4 bg-gray-700 border-b border-gray-600">
          <h2 className="text-xl font-semibold">{status.appName}</h2>
        </div>
        <div className="divide-y divide-gray-700">
          {infoItems.map((item) => (
            <div
              key={item.label}
              className="px-6 py-4 flex justify-between items-center hover:bg-gray-750"
            >
              <span className="text-gray-400">{item.label}</span>
              <span className="font-mono text-sm">{item.value}</span>
            </div>
          ))}
        </div>
      </div>

      <div className="mt-6 grid grid-cols-1 md:grid-cols-3 gap-4">
        <div className="bg-gray-800 rounded-lg p-6">
          <h3 className="text-sm font-medium text-gray-400 mb-2">Status</h3>
          <p className="text-2xl font-bold text-green-400">Running</p>
        </div>
        <div className="bg-gray-800 rounded-lg p-6">
          <h3 className="text-sm font-medium text-gray-400 mb-2">Mode</h3>
          <p className="text-2xl font-bold">
            {status.isProduction ? 'Production' : 'Development'}
          </p>
        </div>
        <div className="bg-gray-800 rounded-lg p-6">
          <h3 className="text-sm font-medium text-gray-400 mb-2">
            Migration Version
          </h3>
          <p className="text-2xl font-bold">{status.migrationVersion}</p>
        </div>
      </div>
    </div>
  );
}
