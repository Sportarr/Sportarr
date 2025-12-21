import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';

interface ProtectedRouteProps {
  children: ReactNode;
}

export default function ProtectedRoute({ children }: ProtectedRouteProps) {
  const { isAuthenticated, isAuthDisabled, isLoading } = useAuth();
  const location = useLocation();

  // Show loading spinner while checking authentication
  if (isLoading) {
    return (
      <div className="min-h-screen bg-gradient-to-br from-gray-900 via-black to-gray-900 flex items-center justify-center">
        <div className="text-center">
          <div className="inline-block animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-red-600 mb-4"></div>
          <p className="text-gray-400">Loading...</p>
        </div>
      </div>
    );
  }

  // If auth is disabled, allow access (matches Sonarr/Radarr behavior)
  if (isAuthDisabled) {
    console.log('[PROTECTED ROUTE] Authentication disabled, allowing access');
    return <>{children}</>;
  }

  // If not authenticated and auth is required, redirect to login
  if (!isAuthenticated) {
    console.log('[PROTECTED ROUTE] Not authenticated, redirecting to /login');
    return <Navigate to={`/login?returnUrl=${encodeURIComponent(location.pathname)}`} replace />;
  }

  // User is authenticated - allow access
  console.log('[PROTECTED ROUTE] User authenticated, allowing access to:', location.pathname);
  return <>{children}</>;
}
