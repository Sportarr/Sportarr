import { useNavigate } from 'react-router-dom';
import { HomeIcon, ArrowLeftIcon } from '@heroicons/react/24/outline';

export default function NotFoundPage() {
  const navigate = useNavigate();

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-900 via-black to-gray-900 flex items-center justify-center p-4">
      <div className="max-w-2xl w-full text-center">
        {/* 404 Image */}
        <div className="mb-8">
          <img
            src="/404.png"
            alt="404 - Page Not Found"
            className="w-full max-w-md mx-auto"
          />
        </div>

        {/* Error Message */}
        <h1 className="text-6xl font-bold text-white mb-4">404</h1>
        <h2 className="text-3xl font-semibold text-red-500 mb-4">Page Not Found</h2>
        <p className="text-xl text-gray-400 mb-8">
          The page you're looking for doesn't exist or has been moved.
        </p>

        {/* Action Buttons */}
        <div className="flex items-center justify-center space-x-4">
          <button
            onClick={() => navigate(-1)}
            className="flex items-center px-6 py-3 bg-gray-800 hover:bg-gray-700 text-white font-semibold rounded-lg transition-colors"
          >
            <ArrowLeftIcon className="w-5 h-5 mr-2" />
            Go Back
          </button>
          <button
            onClick={() => navigate('/leagues')}
            className="flex items-center px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105"
          >
            <HomeIcon className="w-5 h-5 mr-2" />
            Go to Leagues
          </button>
        </div>

        {/* Additional Info */}
        <div className="mt-12 p-6 bg-gray-900/50 border border-gray-800 rounded-lg">
          <h3 className="text-lg font-semibold text-white mb-3">Common Pages</h3>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-3 text-sm">
            <button
              onClick={() => navigate('/leagues')}
              className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-gray-300 hover:text-white rounded transition-colors"
            >
              Leagues
            </button>
            <button
              onClick={() => navigate('/calendar')}
              className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-gray-300 hover:text-white rounded transition-colors"
            >
              Calendar
            </button>
            <button
              onClick={() => navigate('/activity')}
              className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-gray-300 hover:text-white rounded transition-colors"
            >
              Activity
            </button>
            <button
              onClick={() => navigate('/settings/general')}
              className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-gray-300 hover:text-white rounded transition-colors"
            >
              Settings
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
