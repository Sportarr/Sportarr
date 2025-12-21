import { createContext, useContext, useState, useEffect, useRef, useCallback } from 'react';
import type { ReactNode } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';

interface AuthContextType {
  isAuthenticated: boolean;
  isAuthRequired: boolean;
  isAuthDisabled: boolean;
  isLoading: boolean;
  login: (username: string, password: string, rememberMe: boolean) => Promise<boolean>;
  logout: () => Promise<void>;
  checkAuth: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [isAuthRequired, setIsAuthRequired] = useState(false); // Default to false (matches Sonarr/Radarr)
  const [isAuthDisabled, setIsAuthDisabled] = useState(true); // Default to true (no auth on fresh install)
  const [isLoading, setIsLoading] = useState(true);
  const navigate = useNavigate();
  const location = useLocation();
  const hasCheckedAuth = useRef(false);
  const lastPathRef = useRef(location.pathname);

  const checkAuth = useCallback(async () => {
    try {
      console.log('[AUTH] Checking authentication status...');
      const response = await fetch('/api/auth/check');
      const currentPath = location.pathname;

      if (response.ok) {
        const data = await response.json();
        console.log('[AUTH] Auth check response:', data);

        setIsAuthenticated(data.authenticated);
        setIsAuthDisabled(data.authDisabled === true);
        setIsAuthRequired(!data.authDisabled && !data.authenticated);

        // If authenticated (either via session or auth disabled), allow access
        if (data.authenticated) {
          console.log('[AUTH] Authenticated, allowing access');
          // If on login/setup page and authenticated, redirect to main app
          if (currentPath === '/login' || currentPath === '/setup') {
            navigate('/leagues', { replace: true });
          }
          return;
        }

        // Not authenticated and auth is required -> redirect to /login
        if (!data.authenticated && !data.authDisabled) {
          console.log('[AUTH] Not authenticated, redirecting to /login');
          if (currentPath !== '/login' && currentPath !== '/setup') {
            navigate(`/login?returnUrl=${encodeURIComponent(currentPath)}`, { replace: true });
          }
          return;
        }
      } else {
        // Error - assume auth disabled to avoid blocking (matches Sonarr behavior)
        console.error('[AUTH] Auth check failed with status:', response.status);
        setIsAuthenticated(true);
        setIsAuthDisabled(true);
        setIsAuthRequired(false);
      }
    } catch (error) {
      // Network error - assume auth disabled to avoid blocking
      console.error('[AUTH] Failed to check authentication:', error);
      setIsAuthenticated(true);
      setIsAuthDisabled(true);
      setIsAuthRequired(false);
    } finally {
      setIsLoading(false);
    }
  }, [navigate, location.pathname]);

  // Initial auth check on mount only
  useEffect(() => {
    if (!hasCheckedAuth.current) {
      console.log('[AUTH] AuthContext mounted, checking auth once');
      hasCheckedAuth.current = true;
      checkAuth();
    }
  }, [checkAuth]);

  // Re-check only when navigating to protected routes from login/setup
  useEffect(() => {
    const previousPath = lastPathRef.current;
    lastPathRef.current = location.pathname;

    // Only re-check when leaving login or setup pages (user just authenticated)
    if ((previousPath === '/login' || previousPath === '/setup') &&
        location.pathname !== '/login' && location.pathname !== '/setup') {
      console.log('[AUTH] Navigated away from auth pages, re-checking');
      checkAuth();
    }
  }, [location.pathname, checkAuth]);

  const login = async (username: string, password: string, rememberMe: boolean): Promise<boolean> => {
    try {
      const response = await fetch('/api/login', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ username, password, rememberMe }),
      });

      if (response.ok) {
        const data = await response.json();
        if (data.success) {
          setIsAuthenticated(true);
          return true;
        }
      }
      return false;
    } catch (error) {
      console.error('Login failed:', error);
      return false;
    }
  };

  const logout = async () => {
    try {
      await fetch('/api/logout', { method: 'POST' });
    } catch (error) {
      console.error('Logout failed:', error);
    } finally {
      setIsAuthenticated(false);
      navigate('/login');
    }
  };

  return (
    <AuthContext.Provider
      value={{
        isAuthenticated,
        isAuthRequired,
        isAuthDisabled,
        isLoading,
        login,
        logout,
        checkAuth,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
