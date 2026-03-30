import type { ReactNode } from 'react';

interface CompactTableFrameProps {
  children: ReactNode;
  controls?: ReactNode;
  className?: string;
  tableClassName?: string;
}

export default function CompactTableFrame({
  children,
  controls,
  className = '',
  tableClassName = '',
}: CompactTableFrameProps) {
  return (
    <div className={className}>
      {controls && <div className="mb-1 flex justify-end">{controls}</div>}
      <div className="overflow-x-auto">
        <table className={`w-full ${tableClassName}`.trim()}>{children}</table>
      </div>
    </div>
  );
}
