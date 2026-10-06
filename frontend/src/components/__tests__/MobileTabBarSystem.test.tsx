import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { expect, it, vi } from 'vitest';
import { renderWithProviders } from '../../test/test-utils';
import MobileTabBar from '../MobileTabBar';

vi.mock('../../api/hooks', () => ({
  useActivityCounts: () => ({ data: undefined }),
}));
vi.mock('../../hooks/useNavTarget', () => ({
  useNavTarget: () => '/system/status',
  setNavTarget: vi.fn(),
  setNavTargetFromClick: vi.fn(),
}));
vi.mock('../NavIcon', () => ({ default: () => null }));

it('opens system pages from a mobile tab labeled System', async () => {
  renderWithProviders(<MobileTabBar />);
  const user = userEvent.setup();

  await user.click(screen.getByRole('button', { name: 'System' }));

  expect(screen.getByRole('button', { name: 'Status' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Health' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Support' })).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Events' })).not.toBeInTheDocument();

  await user.click(screen.getByRole('button', { name: 'Status' }));
  expect(window.location.pathname).toBe('/system/status');

  await user.click(screen.getByRole('button', { name: 'System' }));
  await user.click(screen.getByRole('button', { name: 'Health' }));
  expect(window.location.pathname).toBe('/system/health');
});
