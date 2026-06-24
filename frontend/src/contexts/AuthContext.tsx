import { createContext, useContext, useState, useEffect, useRef, useCallback } from 'react';
import type { ReactNode } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { createRequestUrl } from '../utils/request';
import { primeApiKey, clearPrimedApiKey } from '../api/client';

interface AuthState {
  isAuthenticated: boolean;
  isAuthRequired: boolean;
  isAuthDisabled: boolean;
  isLoading: boolean;
}

interface AuthContextType extends AuthState {
  login: (username: string, password: string, rememberMe: boolean) => Promise<boolean>;
  logout: () => Promise<void>;
  checkAuth: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

// Initial state: loading=true prevents any routing decisions until auth is checked
const initialAuthState: AuthState = {
  isAuthenticated: false,
  isAuthRequired: false,
  isAuthDisabled: false, // Changed to false - safer default while loading
  isLoading: true,
};

function normalizeAppPath(path: string): string {
  const urlBase = window.Sportarr?.urlBase || '';
  if (!path) {
    return '/leagues';
  }

  if (urlBase && path.startsWith(urlBase + '/')) {
    return path.slice(urlBase.length) || '/';
  }

  return path.startsWith('/') ? path : `/${path}`;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  // Use a single state object to ensure atomic updates and prevent flash
  const [authState, setAuthState] = useState<AuthState>(initialAuthState);
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

      const response = await fetch(createRequestUrl('/api/auth/check'), {
        signal: controller.signal,
      });
      clearTimeout(timeoutId);

      // Extract relative path by removing UrlBase prefix (window.location.pathname includes it)
      // This ensures /login comparison works correctly when deployed at any subpath
      const urlBase = window.Sportarr?.urlBase || '';
      const currentPath = window.location.pathname.startsWith(urlBase)
        ? window.location.pathname.slice(urlBase.length) || '/'
        : window.location.pathname;
      console.log('[AUTH] Current path:', currentPath);

      if (response.ok) {
        const data = await response.json();
        console.log('[AUTH] Auth check response:', data);

        // Calculate all state values first
        const authenticated = data.authenticated === true;
        const authDisabled = data.authDisabled === true;
        const authRequired = !authDisabled && !authenticated;

        // Update all state atomically in a single setState call
        // This prevents any intermediate state that could cause a flash
        setAuthState({
          isAuthenticated: authenticated,
          isAuthDisabled: authDisabled,
          isAuthRequired: authRequired,
          isLoading: false,
        });

        // If on login page and authenticated, redirect to returnUrl or main app
        // This is the ONLY navigation we do - ProtectedRoute handles redirects TO login
        if (authenticated && currentPath === '/login') {
          const searchParams = new URLSearchParams(window.location.search);
          const returnUrl = normalizeAppPath(searchParams.get('returnUrl') || '/leagues');
          console.log('[AUTH] Already authenticated on login page, redirecting to:', returnUrl);
          navigate(returnUrl, { replace: true });
        }
      } else {
        // Error - assume auth disabled to avoid blocking (matches Sonarr behavior)
        console.error('[AUTH] Auth check failed with status:', response.status);
        setAuthState({
          isAuthenticated: true,
          isAuthDisabled: true,
          isAuthRequired: false,
          isLoading: false,
        });
      }
    } catch (error) {
      // Network error or timeout - assume auth disabled to avoid blocking
      if (error instanceof Error && error.name === 'AbortError') {
        console.error('[AUTH] Auth check timed out after 10 seconds');
      } else {
        console.error('[AUTH] Failed to check authentication:', error);
      }
      setAuthState({
        isAuthenticated: true,
        isAuthDisabled: true,
        isAuthRequired: false,
        isLoading: false,
      });
    }
  }, [navigate]);

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
      const response = await fetch(createRequestUrl('/api/login'), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ username, password, rememberMe }),
      });

      if (response.ok) {
        const data = await response.json();
        if (data.success) {
          // Prime API key immediately after login to avoid first-page 401 race
          // before a hard reload or auth re-check updates in-memory window.Sportarr.
          await primeApiKey();
          setAuthState(prev => ({ ...prev, isAuthenticated: true }));
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
      await fetch(createRequestUrl('/api/logout'), { method: 'POST', credentials: 'include' });
    } catch (error) {
      console.error('Logout failed:', error);
    } finally {
      clearPrimedApiKey();
      setAuthState(prev => ({ ...prev, isAuthenticated: false }));
      navigate('/login');
    }
  };

  return (
    <AuthContext.Provider
      value={{
        ...authState,
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
