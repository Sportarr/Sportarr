import { useEffect, useCallback } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';

/**
 * Hook to detect and warn about unsaved changes when navigating away
 */
export function useUnsavedChanges(hasUnsavedChanges: boolean) {
  const navigate = useNavigate();
  const location = useLocation();

  // Warn before page unload (browser refresh/close)
  useEffect(() => {
    const handleBeforeUnload = (e: BeforeUnloadEvent) => {
      if (hasUnsavedChanges) {
        e.preventDefault();
        e.returnValue = ''; // Chrome requires returnValue to be set
        return '';
      }
    };

    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [hasUnsavedChanges]);

  // Block navigation within the app
  const blockNavigation = useCallback(() => {
    if (hasUnsavedChanges) {
      return window.confirm(
        'You have unsaved changes. Are you sure you want to leave this page? Your changes will be lost.'
      );
    }
    return true;
  }, [hasUnsavedChanges]);

  return { blockNavigation };
}
