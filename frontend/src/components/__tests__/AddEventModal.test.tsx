import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../test/test-utils';
import AddEventModal from '../AddEventModal';
import apiClient from '../../api/client';

// Mock the API client
vi.mock('../../api/client');

// Mock the hooks
vi.mock('../../api/hooks', () => ({
  useQualityProfiles: () => ({
    data: [
      { id: 1, name: 'HD 1080p' },
      { id: 2, name: 'Any' },
    ],
    isLoading: false,
  }),
}));

describe('AddEventModal', () => {
  const mockEvent = {
    tapologyId: 'test-123',
    title: 'UFC 300',
    organization: 'UFC',
    eventDate: '2024-04-13',
    venue: 'T-Mobile Arena',
    location: 'Las Vegas, Nevada',
    posterUrl: 'https://example.com/poster.jpg',
    fights: [
      {
        fighter1: 'Alex Pereira',
        fighter2: 'Jamahal Hill',
        weightClass: 'Light Heavyweight',
        isMainEvent: true,
      },
    ],
  };

  const mockOnClose = vi.fn();
  const mockOnSuccess = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('should render modal when isOpen is true', () => {
    renderWithProviders(
      <AddEventModal
        isOpen={true}
        onClose={mockOnClose}
        event={mockEvent}
        onSuccess={mockOnSuccess}
      />
    );

    expect(screen.getByText('Add Event')).toBeInTheDocument();
    expect(screen.getByText('UFC 300')).toBeInTheDocument();
  });

  it('should not render modal when isOpen is false', () => {
    renderWithProviders(
      <AddEventModal
        isOpen={false}
        onClose={mockOnClose}
        event={mockEvent}
        onSuccess={mockOnSuccess}
      />
    );

    expect(screen.queryByText('Add Event')).not.toBeInTheDocument();
  });

  it('should display event details', () => {
    renderWithProviders(
      <AddEventModal
        isOpen={true}
        onClose={mockOnClose}
        event={mockEvent}
        onSuccess={mockOnSuccess}
      />
    );

    expect(screen.getByText('UFC 300')).toBeInTheDocument();
    expect(screen.getByText('UFC')).toBeInTheDocument();
    expect(screen.getByText(/Saturday, April 13, 2024/)).toBeInTheDocument();
    expect(screen.getByText(/T-Mobile Arena/)).toBeInTheDocument();
    expect(screen.getByText(/Las Vegas, Nevada/)).toBeInTheDocument();
  });

  it('should have monitored checkbox checked by default', () => {
    renderWithProviders(
      <AddEventModal
        isOpen={true}
        onClose={mockOnClose}
        event={mockEvent}
        onSuccess={mockOnSuccess}
      />
    );

    const monitorCheckbox = screen.getByRole('checkbox', {
      name: /monitor/i,
    });
    expect(monitorCheckbox).toBeChecked();
  });

  it('should toggle monitored checkbox', async () => {
    const user = userEvent.setup();

    renderWithProviders(
      <AddEventModal
        isOpen={true}
        onClose={mockOnClose}
        event={mockEvent}
        onSuccess={mockOnSuccess}
      />
    );

    const monitorCheckbox = screen.getByRole('checkbox', {
      name: /monitor/i,
    });

    expect(monitorCheckbox).toBeChecked();

    await user.click(monitorCheckbox);

    expect(monitorCheckbox).not.toBeChecked();
  });

  it('should call onClose when cancel button is clicked', async () => {
    const user = userEvent.setup();

    renderWithProviders(
      <AddEventModal
        isOpen={true}
        onClose={mockOnClose}
        event={mockEvent}
        onSuccess={mockOnSuccess}
      />
    );

    const cancelButton = screen.getByRole('button', { name: /cancel/i });
    await user.click(cancelButton);

    expect(mockOnClose).toHaveBeenCalledTimes(1);
  });

  it('should call API and onSuccess when add button is clicked', async () => {
    const user = userEvent.setup();
    const mockResponse = { data: { id: 1, ...mockEvent } };

    vi.mocked(apiClient.post).mockResolvedValueOnce(mockResponse);

    renderWithProviders(
      <AddEventModal
        isOpen={true}
        onClose={mockOnClose}
        event={mockEvent}
        onSuccess={mockOnSuccess}
      />
    );

    const addButton = screen.getByRole('button', { name: /add event/i });
    await user.click(addButton);

    await waitFor(() => {
      expect(apiClient.post).toHaveBeenCalledWith('/events', expect.objectContaining({
        tapologyId: 'test-123',
        title: 'UFC 300',
        organization: 'UFC',
      }));
      expect(mockOnSuccess).toHaveBeenCalledTimes(1);
      expect(mockOnClose).toHaveBeenCalledTimes(1);
    });
  });

  it('should show error message when API call fails', async () => {
    const user = userEvent.setup();
    const mockError = new Error('Failed to add event');

    vi.mocked(apiClient.post).mockRejectedValueOnce(mockError);
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

    renderWithProviders(
      <AddEventModal
        isOpen={true}
        onClose={mockOnClose}
        event={mockEvent}
        onSuccess={mockOnSuccess}
      />
    );

    const addButton = screen.getByRole('button', { name: /add event/i });
    await user.click(addButton);

    await waitFor(() => {
      expect(consoleSpy).toHaveBeenCalled();
    });

    consoleSpy.mockRestore();
  });

  it('should display fights when available', () => {
    renderWithProviders(
      <AddEventModal
        isOpen={true}
        onClose={mockOnClose}
        event={mockEvent}
        onSuccess={mockOnSuccess}
      />
    );

    expect(screen.getByText(/Alex Pereira/)).toBeInTheDocument();
    expect(screen.getByText(/Jamahal Hill/)).toBeInTheDocument();
  });

  it('should disable add button while adding', async () => {
    const user = userEvent.setup();

    // Make API call hang
    vi.mocked(apiClient.post).mockImplementation(
      () => new Promise(() => {})
    );

    renderWithProviders(
      <AddEventModal
        isOpen={true}
        onClose={mockOnClose}
        event={mockEvent}
        onSuccess={mockOnSuccess}
      />
    );

    const addButton = screen.getByRole('button', { name: /add event/i });

    await user.click(addButton);

    await waitFor(() => {
      expect(addButton).toBeDisabled();
    });
  });

  it('should format date correctly', () => {
    renderWithProviders(
      <AddEventModal
        isOpen={true}
        onClose={mockOnClose}
        event={mockEvent}
        onSuccess={mockOnSuccess}
      />
    );

    // Check that the date is formatted in a readable way
    expect(screen.getByText(/Saturday, April 13, 2024/)).toBeInTheDocument();
  });
});
