import type { ReactNode } from 'react';

interface PageHeaderProps {
  title: string;
  subtitle?: string;
  icon?: React.ComponentType<{ className?: string }>;
  actions?: ReactNode;
  children?: ReactNode;
  className?: string;
  titleClassName?: string;
  subtitleClassName?: string;
  actionsClassName?: string;
}

export default function PageHeader({
  title,
  subtitle,
  icon: Icon,
  actions,
  children,
  className = '',
  titleClassName = '',
  subtitleClassName = '',
  actionsClassName = '',
}: PageHeaderProps) {
  return (
    <div className={`mb-6 ${className}`.trim()}>
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="flex min-w-0 items-start gap-3">
          {Icon && <Icon className="w-8 h-8 text-red-500" />}
          <div className="min-w-0">
            <h1 className={`text-2xl font-bold text-white md:text-3xl ${titleClassName}`.trim()}>{title}</h1>
            {subtitle && (
              <p className={`mt-1 text-sm text-gray-400 md:text-base ${subtitleClassName}`.trim()}>
                {subtitle}
              </p>
            )}
          </div>
        </div>
        {actions && (
          <div className={`flex flex-wrap items-center gap-2 md:justify-end ${actionsClassName}`.trim()}>
            {actions}
          </div>
        )}
      </div>
      {children}
    </div>
  );
}
