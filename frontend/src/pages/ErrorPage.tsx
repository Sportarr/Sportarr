import { useNavigate, useRouteError, isRouteErrorResponse } from 'react-router-dom';
import { HomeIcon, ArrowPathIcon } from '@heroicons/react/24/outline';

export default function ErrorPage() {
  const navigate = useNavigate();
  const error = useRouteError();

  let errorMessage = 'An unexpected error occurred';
  let errorDetails = '';

  if (isRouteErrorResponse(error)) {
    errorMessage = error.statusText || errorMessage;
    errorDetails = error.data?.message || '';
  } else if (error instanceof Error) {
    errorMessage = error.message;
    errorDetails = error.stack || '';
  }

  const handleReload = () => {
    window.location.reload();
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-900 via-black to-gray-900 flex items-center justify-center p-4">
      <div className="max-w-2xl w-full text-center">
        {/* Error Image */}
        <div className="mb-8">
          <img
            src="/error.png"
            alt="Error"
            className="w-full max-w-md mx-auto"
          />
        </div>

        {/* Error Message */}
        <h1 className="text-6xl font-bold text-white mb-4">Oops!</h1>
        <h2 className="text-3xl font-semibold text-red-500 mb-4">Something Went Wrong</h2>
        <p className="text-xl text-gray-400 mb-8">
          {errorMessage}
        </p>

        {/* Action Buttons */}
        <div className="flex items-center justify-center space-x-4">
          <button
            onClick={handleReload}
            className="flex items-center px-6 py-3 bg-gray-800 hover:bg-gray-700 text-white font-semibold rounded-lg transition-colors"
          >
            <ArrowPathIcon className="w-5 h-5 mr-2" />
            Reload Page
          </button>
          <button
            onClick={() => navigate('/leagues')}
            className="flex items-center px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105"
          >
            <HomeIcon className="w-5 h-5 mr-2" />
            Go to Leagues
          </button>
        </div>

        {/* Error Details (for development) */}
        {errorDetails && (
          <details className="mt-8 text-left">
            <summary className="cursor-pointer text-gray-400 hover:text-white mb-2">
              Technical Details (click to expand)
            </summary>
            <pre className="p-4 bg-gray-900/80 border border-gray-800 rounded-lg text-xs text-gray-400 overflow-x-auto">
              {errorDetails}
            </pre>
          </details>
        )}

        {/* Help Text */}
        <div className="mt-12 p-6 bg-gray-900/50 border border-gray-800 rounded-lg">
          <h3 className="text-lg font-semibold text-white mb-3">Need Help?</h3>
          <p className="text-sm text-gray-400 mb-4">
            If this error persists, try the following:
          </p>
          <ul className="text-sm text-gray-400 space-y-2 text-left max-w-md mx-auto">
            <li>• Clear your browser cache and reload the page</li>
            <li>• Check the browser console for additional error details</li>
            <li>• Verify your network connection is stable</li>
            <li>• Check System → Logs for server-side errors</li>
            <li>• Report the issue on GitHub if the problem continues</li>
          </ul>
        </div>
      </div>
    </div>
  );
}
