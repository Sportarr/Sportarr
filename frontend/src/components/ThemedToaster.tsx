import { Toaster } from 'sonner';
import { useResolvedTheme } from '../hooks/useTheme';

/**
 * Applies the theme to the document and renders the toaster in the matching
 * colours. It lives inside the query provider because the saved choice is
 * read through React Query.
 */
export function ThemedToaster() {
  const theme = useResolvedTheme();
  return <Toaster position="top-right" theme={theme} richColors closeButton />;
}
