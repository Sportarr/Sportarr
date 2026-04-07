import type { ReactNode } from 'react';
import { PAGE_PADDING } from '../utils/designTokens';

interface PageShellProps {
  children: ReactNode;
  className?: string;
}

interface PageLoadingStateProps {
  label?: string;
  className?: string;
}

interface PageErrorStateProps {
  title?: string;
  message: string;
  action?: ReactNode;
  className?: string;
}

export default function PageShell({ children, className = '' }: PageShellProps) {
  return <div className={`${PAGE_PADDING} ${className}`.trim()}>{children}</div>;
}

export function PageLoadingState({
  label = 'Loading...',
  className = '',
}: PageLoadingStateProps) {
  return (
    <PageShell className={className}>
      <div className="flex min-h-64 items-center justify-center gap-3 text-gray-400">
        <div className="h-10 w-10 animate-spin rounded-full border-b-2 border-red-600" />
        <span>{label}</span>
      </div>
    </PageShell>
  );
}

export function PageErrorState({
  title = 'Error',
  message,
  action,
  className = '',
}: PageErrorStateProps) {
  return (
    <PageShell className={className}>
      <div className="rounded-lg border border-red-800 bg-red-950/40 px-4 py-4 text-red-100">
        <p className="font-semibold">{title}</p>
        <p className="mt-1 text-sm text-red-200">{message}</p>
        {action && <div className="mt-4">{action}</div>}
      </div>
    </PageShell>
  );
}
