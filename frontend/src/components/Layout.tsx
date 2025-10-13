import { Link, Outlet, useLocation } from 'react-router-dom';
import { useSystemStatus } from '../api/hooks';

export default function Layout() {
  const location = useLocation();
  const { data: systemStatus } = useSystemStatus();

  const navItems = [
    { path: '/events', label: 'Events' },
    { path: '/system', label: 'System' },
  ];

  return (
    <div className="min-h-screen bg-gray-900 text-gray-100">
      {/* Header */}
      <header className="bg-gray-800 border-b border-gray-700">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between h-16">
            <div className="flex items-center">
              <img src="/logo.svg" alt="Fightarr" className="h-8 w-auto" />
              <span className="ml-3 text-xl font-bold">Fightarr</span>
              {systemStatus && (
                <span className="ml-3 text-sm text-gray-400">
                  v{systemStatus.version}
                </span>
              )}
            </div>
            <nav className="flex space-x-4">
              {navItems.map((item) => (
                <Link
                  key={item.path}
                  to={item.path}
                  className={`px-3 py-2 rounded-md text-sm font-medium ${
                    location.pathname === item.path
                      ? 'bg-gray-900 text-white'
                      : 'text-gray-300 hover:bg-gray-700 hover:text-white'
                  }`}
                >
                  {item.label}
                </Link>
              ))}
            </nav>
          </div>
        </div>
      </header>

      {/* Main content */}
      <main className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
        <Outlet />
      </main>
    </div>
  );
}
