import { Component } from 'react';
import type { ReactNode } from 'react';
import { HomeIcon, ArrowPathIcon } from '@heroicons/react/24/outline';

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
  error: Error | null;
  errorInfo: React.ErrorInfo | null;
}

export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = {
      hasError: false,
      error: null,
      errorInfo: null,
    };
  }

  static getDerivedStateFromError(error: Error): Partial<State> {
    return { hasError: true };
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
    console.error('ErrorBoundary caught an error:', error, errorInfo);
    this.setState({
      error,
      errorInfo,
    });
  }

  handleReload = () => {
    window.location.reload();
  };

  handleGoHome = () => {
    window.location.href = '/events';
  };

  render() {
    if (this.state.hasError) {
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
            <h2 className="text-3xl font-semibold text-red-500 mb-4">
              Application Error
            </h2>
            <p className="text-xl text-gray-400 mb-8">
              {this.state.error?.message || 'An unexpected error occurred'}
            </p>

            {/* Action Buttons */}
            <div className="flex items-center justify-center space-x-4">
              <button
                onClick={this.handleReload}
                className="flex items-center px-6 py-3 bg-gray-800 hover:bg-gray-700 text-white font-semibold rounded-lg transition-colors"
              >
                <ArrowPathIcon className="w-5 h-5 mr-2" />
                Reload Page
              </button>
              <button
                onClick={this.handleGoHome}
                className="flex items-center px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105"
              >
                <HomeIcon className="w-5 h-5 mr-2" />
                Go to Events
              </button>
            </div>

            {/* Error Details */}
            {this.state.error && (
              <details className="mt-8 text-left">
                <summary className="cursor-pointer text-gray-400 hover:text-white mb-2">
                  Technical Details (click to expand)
                </summary>
                <div className="p-4 bg-gray-900/80 border border-gray-800 rounded-lg text-xs">
                  <div className="mb-4">
                    <span className="text-red-400 font-semibold">Error:</span>
                    <pre className="text-gray-400 mt-1 overflow-x-auto">
                      {this.state.error.toString()}
                    </pre>
                  </div>
                  {this.state.errorInfo && (
                    <div>
                      <span className="text-red-400 font-semibold">
                        Component Stack:
                      </span>
                      <pre className="text-gray-400 mt-1 overflow-x-auto">
                        {this.state.errorInfo.componentStack}
                      </pre>
                    </div>
                  )}
                </div>
              </details>
            )}

            {/* Help Text */}
            <div className="mt-12 p-6 bg-gray-900/50 border border-gray-800 rounded-lg">
              <h3 className="text-lg font-semibold text-white mb-3">
                Need Help?
              </h3>
              <p className="text-sm text-gray-400 mb-4">
                If this error persists, try the following:
              </p>
              <ul className="text-sm text-gray-400 space-y-2 text-left max-w-md mx-auto">
                <li>• Clear your browser cache and reload the page</li>
                <li>• Check the browser console for additional error details</li>
                <li>• Disable browser extensions that might interfere</li>
                <li>• Try accessing the application in an incognito window</li>
                <li>• Report the issue on GitHub with the error details above</li>
              </ul>
            </div>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
