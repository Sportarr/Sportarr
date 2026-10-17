import { PlusIcon } from '@heroicons/react/24/outline';
import { Link } from 'react-router-dom';
import { BUTTON_SECONDARY, BUTTON_TAB_BASE, BUTTON_TOOLBAR_ACTIVE, BUTTON_TOOLBAR_INACTIVE } from '../utils/designTokens';

interface Props {
  current: 'browse' | 'yours';
}

export default function SupportNavigation({ current }: Props) {
  return <nav aria-label="Support sections" className="mb-5 flex min-w-0 items-center gap-2 border-b border-gray-800 pb-3">
    <Link to="/support" aria-current={current === 'browse' ? 'page' : undefined}
      className={`${BUTTON_TAB_BASE} ${current === 'browse' ? BUTTON_TOOLBAR_ACTIVE : BUTTON_TOOLBAR_INACTIVE}`}>
      <span className="sm:hidden">Browse</span><span className="hidden sm:inline">Browse issues</span>
    </Link>
    <Link to="/support/yours" aria-current={current === 'yours' ? 'page' : undefined}
      className={`${BUTTON_TAB_BASE} ${current === 'yours' ? BUTTON_TOOLBAR_ACTIVE : BUTTON_TOOLBAR_INACTIVE}`}>
      <span className="sm:hidden">Yours</span><span className="hidden sm:inline">Your issues</span>
    </Link>
    <Link to="/support/new" className={`${BUTTON_SECONDARY} ml-auto min-h-11 shrink-0 whitespace-nowrap border border-gray-600`}>
      <PlusIcon className="h-4 w-4 text-red-400" aria-hidden="true" />New issue
    </Link>
  </nav>;
}
