import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import ExcludedChannelsSection, { type LeagueChannelExclusion } from '../ExcludedChannelsSection';

const exclusions: LeagueChannelExclusion[] = [
  { mappingId: 11, channelId: 2, channelName: 'NZ: SKY Sport 1', sourceName: 'Provider', country: 'NZ' },
  { mappingId: 12, channelId: 3, channelName: 'NZ: SKY Sport 2', sourceName: null, country: null },
];

const noop = () => {};

describe('ExcludedChannelsSection', () => {
  it('renders nothing when no channel is excluded', () => {
    const { container } = render(
      <ExcludedChannelsSection exclusions={[]} busy={false} onMapAgain={noop} onRemove={noop} />,
    );
    expect(container).toBeEmptyDOMElement();
    const { container: loading } = render(
      <ExcludedChannelsSection exclusions={undefined} busy={false} onMapAgain={noop} onRemove={noop} />,
    );
    expect(loading).toBeEmptyDOMElement();
  });

  it('starts closed and lists the channels when opened', () => {
    render(<ExcludedChannelsSection exclusions={exclusions} busy={false} onMapAgain={noop} onRemove={noop} />);
    const toggle = screen.getByRole('button', { name: /Excluded channels \(2\)/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByText('NZ: SKY Sport 1')).not.toBeInTheDocument();

    fireEvent.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText('NZ: SKY Sport 1')).toBeInTheDocument();
    expect(screen.getByText('Provider · NZ')).toBeInTheDocument();
    expect(screen.getByText('Unknown source')).toBeInTheDocument();
    expect(screen.getByText(/Remove exclusion lets auto-mapping decide/)).toBeInTheDocument();
  });

  it('acts on the clicked channel', () => {
    const onRemove = vi.fn();
    const onMapAgain = vi.fn();
    render(<ExcludedChannelsSection exclusions={exclusions} busy={false} onMapAgain={onMapAgain} onRemove={onRemove} />);
    fireEvent.click(screen.getByRole('button', { name: /Excluded channels/ }));

    fireEvent.click(screen.getByRole('button', { name: /Remove exclusion for NZ: SKY Sport 2/ }));
    fireEvent.click(screen.getByRole('button', { name: /Map NZ: SKY Sport 1 again/ }));

    expect(onRemove).toHaveBeenCalledWith(3);
    expect(onMapAgain).toHaveBeenCalledWith(2);
  });

  it('disables the buttons while a request runs', () => {
    render(<ExcludedChannelsSection exclusions={exclusions} busy onMapAgain={noop} onRemove={noop} />);
    fireEvent.click(screen.getByRole('button', { name: /Excluded channels/ }));
    for (const button of screen.getAllByRole('button', { name: /Remove exclusion for|again$/ })) {
      expect(button).toBeDisabled();
    }
    expect(screen.getAllByRole('button', { name: /Remove exclusion for|again$/ })).toHaveLength(4);
  });

  it('shows a load error in place of the list', () => {
    render(
      <ExcludedChannelsSection exclusions={undefined} error="Network Error" busy={false} onMapAgain={noop} onRemove={noop} />,
    );
    expect(screen.getByText(/Could not load the excluded channels\. Network Error/)).toBeInTheDocument();
  });
});
