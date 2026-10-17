import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { Toaster } from 'sonner';
import { renderWithProviders, userEvent } from '../../test/test-utils';
import apiClient from '../../api/client';
import OnboardingWizard from '../OnboardingWizard';

vi.mock('../../api/client');
vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({ login: vi.fn() }),
}));

const indexerFields = [
  { name: 'baseUrl', value: 'https://indexer.example.com' },
  { name: 'apiPath', value: '/api' },
  { name: 'apiKey', value: 'secret-key' },
  { name: 'categories', value: '' },
  { name: 'minimumSeeders', value: '1' },
  { name: 'queryLimit', value: '' },
  { name: 'grabLimit', value: '' },
  { name: 'requestDelayMs', value: '0' },
  { name: 'earlyReleaseLimit', value: '' },
  { name: 'disableSslCertificateValidation', value: 'false' },
  { name: 'failDownloads', value: '' },
];

async function openIndexerStep(fillForm = true) {
  const user = userEvent.setup();
  renderWithProviders(
    <>
      <OnboardingWizard onClose={vi.fn()} onComplete={vi.fn()} />
      <Toaster />
    </>,
  );

  await user.click(screen.getByRole('button', { name: 'Get started' }));
  for (let step = 0; step < 5; step += 1) {
    await user.click(screen.getByRole('button', { name: 'Skip Step' }));
  }

  expect(screen.getByRole('heading', { name: 'Add your indexers' })).toBeVisible();
  if (fillForm) {
    await user.type(screen.getByPlaceholderText('https://indexer.example.com'), 'https://indexer.example.com');
    await user.type(screen.getByRole('textbox', { name: 'API Key' }), 'secret-key');
  }

  return user;
}

describe('onboarding indexer setup', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.get).mockResolvedValue({ data: [] } as never);
    vi.mocked(apiClient.post).mockResolvedValue({ data: { id: 12 } } as never);
  });

  it('tests the indexer with the API field-array contract', async () => {
    const user = await openIndexerStep();

    await user.click(screen.getByRole('button', { name: 'Test connection' }));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith('/indexer/test', {
        name: 'Newznab',
        implementation: 'Newznab',
        enable: true,
        enableRss: true,
        enableAutomaticSearch: true,
        enableInteractiveSearch: true,
        priority: 25,
        fields: indexerFields,
        tags: [],
      });
    });
  }, 10000);

  it('saves the indexer with the API field-array contract', async () => {
    const user = await openIndexerStep();

    await user.click(screen.getByRole('button', { name: '+ Save & add another' }));

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith('/indexer', expect.objectContaining({
        fields: indexerFields,
      }));
    });
  }, 10000);

  it('shows the backend message when a connection test fails', async () => {
    vi.mocked(apiClient.post).mockRejectedValue({
      response: { data: { message: 'Could not reach the indexer' } },
    });
    const user = await openIndexerStep();

    await user.click(screen.getByRole('button', { name: 'Test connection' }));

    expect(await screen.findByText(/Could not reach the indexer/)).toBeVisible();
  }, 10000);

  it('shows the backend message when saving fails', async () => {
    vi.mocked(apiClient.post).mockRejectedValue({
      response: { data: { message: 'Indexer URL is required' } },
    });
    const user = await openIndexerStep();

    await user.click(screen.getByRole('button', { name: '+ Save & add another' }));

    expect(await screen.findByText('Indexer URL is required')).toBeVisible();
  }, 10000);

  it('updates only the indexer fields exposed by the wizard', async () => {
    const existingFields = [
      { name: 'baseUrl', value: 'https://old.example.com' },
      { name: 'apiPath', value: '/custom/api' },
      { name: 'apiKey', value: 'old-key' },
      { name: 'categories', value: '5040,5060' },
      { name: 'minimumSeeders', value: '5' },
      { name: 'queryLimit', value: '100' },
      { name: 'grabLimit', value: '50' },
      { name: 'requestDelayMs', value: '2000' },
      { name: 'failDownloads', value: '1,2' },
    ];
    vi.mocked(apiClient.get).mockImplementation(async (url) => {
      if (url === '/indexer') {
        return {
          data: [{
            id: 42,
            name: 'Existing RSS',
            implementation: 'Rss',
            configContract: 'TorznabSettings',
            enable: false,
            enableRss: false,
            enableAutomaticSearch: false,
            enableInteractiveSearch: true,
            priority: 10,
            fields: existingFields,
            tags: [7],
          }],
        } as never;
      }
      return { data: [] } as never;
    });
    vi.mocked(apiClient.put).mockResolvedValue({ data: { id: 42 } } as never);
    const user = await openIndexerStep(false);

    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    expect(screen.getByPlaceholderText('https://indexer.example.com')).toHaveValue('https://old.example.com');
    expect(screen.getByRole('textbox', { name: 'API Key' })).toHaveValue('old-key');

    await user.clear(screen.getByPlaceholderText('https://indexer.example.com'));
    await user.type(screen.getByPlaceholderText('https://indexer.example.com'), 'https://new.example.com');
    await user.click(screen.getByRole('button', { name: 'Update indexer' }));

    await waitFor(() => {
      expect(apiClient.put).toHaveBeenCalledWith('/indexer/42', {
        id: 42,
        name: 'Existing RSS',
        implementation: 'Rss',
        configContract: 'TorznabSettings',
        enable: false,
        enableRss: false,
        enableAutomaticSearch: false,
        enableInteractiveSearch: true,
        priority: 10,
        fields: [
          { name: 'baseUrl', value: 'https://new.example.com' },
          { name: 'apiPath', value: '/custom/api' },
          { name: 'apiKey', value: 'old-key' },
          { name: 'categories', value: '5040,5060' },
          { name: 'minimumSeeders', value: '5' },
          { name: 'queryLimit', value: '100' },
          { name: 'grabLimit', value: '50' },
          { name: 'requestDelayMs', value: '2000' },
          { name: 'failDownloads', value: '1,2' },
        ],
        tags: [7],
      });
    });
  }, 10000);
});
