import type { ReactNode } from 'react';

interface PageHeaderProps {
  title: string;
  subtitle?: string;
  icon?: React.ComponentType<{ className?: string }>;
  actions?: ReactNode;
  children?: ReactNode;
}

/**
 * Consistent page header component for all non-settings pages.
 * Provides standard spacing, typography, and optional icon/actions.
 */
export default function PageHeader({
  title,
  subtitle,
  icon: Icon,
  actions,
  children,
}: PageHeaderProps) {
  return (
    <div className="mb-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          {Icon && <Icon className="w-8 h-8 text-red-500" />}
          <div>
            <h1 className="text-3xl font-bold text-white mb-1">{title}</h1>
            {subtitle && <p className="text-gray-400">{subtitle}</p>}
          </div>
        </div>
        {actions && <div className="flex items-center gap-3">{actions}</div>}
      </div>
      {children}
    </div>
  );
}
