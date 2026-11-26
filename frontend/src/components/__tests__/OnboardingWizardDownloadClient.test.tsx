import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../test/test-utils';
import apiClient from '../../api/client';
import OnboardingWizard from '../OnboardingWizard';

vi.mock('../../api/client');
vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({ login: vi.fn() }),
}));

async function openClientStep() {
  const user = userEvent.setup();
  renderWithProviders(<OnboardingWizard onClose={vi.fn()} onComplete={vi.fn()} />);

  await user.click(screen.getByRole('button', { name: 'Get started' }));
  for (let step = 0; step < 4; step += 1) {
    await user.click(screen.getByRole('button', { name: 'Skip Step' }));
  }

  expect(screen.getByRole('heading', { name: 'Connect your download client' })).toBeVisible();
  return user;
}

describe('onboarding download client setup', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.get).mockResolvedValue({ data: [] } as never);
    vi.mocked(apiClient.post).mockResolvedValue({ data: { id: 7 } } as never);
  });

  it('saves a client as plain http by default', async () => {
    const user = await openClientStep();

    await user.clear(screen.getByPlaceholderText('localhost'));
    await user.type(screen.getByPlaceholderText('localhost'), 'sab.local');
    await user.click(screen.getByRole('button', { name: 'Save & Next' }));

    await waitFor(() => expect(apiClient.post).toHaveBeenCalledWith(
      '/downloadclient', expect.objectContaining({ host: 'sab.local', useSsl: false })));
  });

  it('sends useSsl when Use SSL is ticked, for a reverse proxied client on port 443', async () => {
    const user = await openClientStep();

    await user.clear(screen.getByPlaceholderText('localhost'));
    await user.type(screen.getByPlaceholderText('localhost'), 'sab.example.com');
    await user.click(screen.getByRole('checkbox', { name: 'Use SSL' }));
    await user.click(screen.getByRole('button', { name: 'Save & Next' }));

    await waitFor(() => expect(apiClient.post).toHaveBeenCalledWith(
      '/downloadclient', expect.objectContaining({ host: 'sab.example.com', useSsl: true })));
  });
});
