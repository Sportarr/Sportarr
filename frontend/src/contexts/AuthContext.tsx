import { createContext, useContext, useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';

interface AuthContextType {
  isAuthenticated: boolean;
  isAuthRequired: boolean;
  isSetupComplete: boolean;
  isLoading: boolean;
  login: (username: string, password: string, rememberMe: boolean) => Promise<boolean>;
  logout: () => Promise<void>;
  checkAuth: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [isAuthRequired, setIsAuthRequired] = useState(false);
  const [isSetupComplete, setIsSetupComplete] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const navigate = useNavigate();
  const location = useLocation();

  const checkAuth = async () => {
    try {
      console.log('[AUTH] Checking authentication status...');
      const response = await fetch('/api/auth/check');

      if (response.ok) {
        const data = await response.json();
        console.log('[AUTH] Auth check response:', data);

        setIsSetupComplete(data.setupComplete);
        setIsAuthenticated(data.authenticated);

        // THREE STATE FLOW:
        // 1. Setup not complete -> redirect to /setup
        if (!data.setupComplete) {
          console.log('[AUTH] Setup not complete, redirecting to /setup');
          if (location.pathname !== '/setup') {
            navigate('/setup', { replace: true });
          }
          return;
        }

        // 2. Setup complete but not authenticated -> redirect to /login
        if (data.setupComplete && !data.authenticated) {
          console.log('[AUTH] Not authenticated, redirecting to /login');
          if (location.pathname !== '/login' && location.pathname !== '/setup') {
            navigate(`/login?returnUrl=${encodeURIComponent(location.pathname)}`, { replace: true });
          }
          return;
        }

        // 3. Setup complete and authenticated -> allow access
        console.log('[AUTH] Authenticated, allowing access');
      } else {
        // Error - redirect to setup to be safe
        console.error('[AUTH] Auth check failed with status:', response.status);
        setIsSetupComplete(false);
        setIsAuthenticated(false);
        if (location.pathname !== '/setup') {
          navigate('/setup', { replace: true });
        }
      }
    } catch (error) {
      // Network error - redirect to setup
      console.error('[AUTH] Failed to check authentication:', error);
      setIsSetupComplete(false);
      setIsAuthenticated(false);
      if (location.pathname !== '/setup') {
        navigate('/setup', { replace: true });
      }
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    console.log('[AUTH] AuthContext mounted, pathname:', location.pathname);
    checkAuth();
  }, [location.pathname]); // Re-check on navigation

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
        isSetupComplete,
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
