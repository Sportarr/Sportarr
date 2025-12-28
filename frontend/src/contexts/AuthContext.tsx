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

      // Add timeout to prevent infinite loading on slow/mobile networks
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 10000); // 10 second timeout

      const response = await fetch('/api/auth/check', {
        signal: controller.signal,
      });
      clearTimeout(timeoutId);

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
          // If on login page and authenticated, redirect to main app
          if (currentPath === '/login') {
            navigate('/leagues', { replace: true });
          }
          return;
        }

        // Not authenticated and auth is required -> redirect to /login
        if (!data.authenticated && !data.authDisabled) {
          console.log('[AUTH] Not authenticated, redirecting to /login');
          if (currentPath !== '/login') {
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
      // Network error or timeout - assume auth disabled to avoid blocking
      if (error instanceof Error && error.name === 'AbortError') {
        console.error('[AUTH] Auth check timed out after 10 seconds');
      } else {
        console.error('[AUTH] Failed to check authentication:', error);
      }
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

  // Re-check only when navigating to protected routes from login
  useEffect(() => {
    const previousPath = lastPathRef.current;
    lastPathRef.current = location.pathname;

    // Only re-check when leaving login page (user just authenticated)
    if (previousPath === '/login' && location.pathname !== '/login') {
      console.log('[AUTH] Navigated away from login page, re-checking');
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
