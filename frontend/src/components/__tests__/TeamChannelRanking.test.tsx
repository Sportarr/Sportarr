import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import TeamChannelRanking, { type TeamChannelEntry } from '../TeamChannelRanking';
import { moveItem } from '../../utils/moveItem';

const transport = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn() }));
vi.mock('../../api/client', () => ({ default: transport }));
vi.mock('sonner', () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

function entry(channelId: number, name: string, priority: number): TeamChannelEntry {
  return {
    id: channelId * 10,
    channelId,
    channelName: name,
    sourceName: 'trilo',
    isEnabled: true,
    status: 'Online',
    priority,
    isPreferred: priority === 0,
  };
}

const LIST = [entry(1, 'US: MSG', 0), entry(2, 'US: MSGSN', 1), entry(3, 'US: ESPN', 2)];

function ranks(team = 'New York Rangers') {
  const list = screen.getByRole('list', { name: `${team} DVR channels` });
  return within(list).getAllByRole('listitem').map((item) => item.textContent ?? '');
}

beforeEach(() => {
  transport.get.mockReset();
  transport.put.mockReset();
  transport.get.mockImplementation(async (path: string) => {
    if (path === '/teams') return { data: [{ id: 7 }] };
    if (path === '/iptv/teams/7/channel-mappings') return { data: LIST };
    if (path === '/iptv/channels') return { data: [{ id: 4, name: 'US: TNT' }, { id: 1, name: 'US: MSG' }] };
    throw new Error('Unconfigured request ' + path);
  });
  transport.put.mockImplementation(async (_path: string, body: { channelIds: number[] }) => ({
    data: body.channelIds.map((id, index) => {
      const known = [...LIST, entry(4, 'US: TNT', 0)].find((e) => e.channelId === id)!;
      return { ...known, priority: index, isPreferred: index === 0 };
    }),
  }));
});

describe('moveItem', () => {
  it('moves an item and leaves the list alone for an impossible move', () => {
    expect(moveItem(['a', 'b', 'c'], 2, 0)).toEqual(['c', 'a', 'b']);
    const list = ['a', 'b'];
    expect(moveItem(list, 0, -1)).toBe(list);
    expect(moveItem(list, 1, 2)).toBe(list);
  });
});

describe('TeamChannelRanking', () => {
  it('shows the channels with their rank', async () => {
    render(<TeamChannelRanking teamExternalId="134838" teamName="New York Rangers" />);

    expect(await screen.findByText('US: MSGSN')).toBeInTheDocument();
    expect(ranks()).toEqual([
      expect.stringContaining('US: MSG'),
      expect.stringContaining('US: MSGSN'),
      expect.stringContaining('US: ESPN'),
    ]);
    expect(ranks()[2]).toContain('Rank3');
    expect(transport.get).toHaveBeenCalledWith('/teams', { params: { externalId: '134838' } });
  });

  it('saves the new order when a channel moves up', async () => {
    render(<TeamChannelRanking teamExternalId="134838" teamName="New York Rangers" />);

    fireEvent.click(await screen.findByRole('button', { name: 'Move US: ESPN up' }));

    await waitFor(() => expect(transport.put).toHaveBeenCalledWith(
      '/iptv/teams/7/channel-mappings', { channelIds: [1, 3, 2] }));
    expect(ranks()[1]).toContain('US: ESPN');
    expect(screen.getByRole('button', { name: 'Move US: MSG up' })).toBeDisabled();
  });

  it('saves the new order when a channel is dropped on another row', async () => {
    render(<TeamChannelRanking teamExternalId="134838" teamName="New York Rangers" />);
    await screen.findByText('US: ESPN');
    const items = within(screen.getByRole('list')).getAllByRole('listitem');

    fireEvent.dragStart(items[2], { dataTransfer: { effectAllowed: '' } });
    fireEvent.dragOver(items[0]);
    fireEvent.drop(items[0]);

    await waitFor(() => expect(transport.put).toHaveBeenCalledWith(
      '/iptv/teams/7/channel-mappings', { channelIds: [3, 1, 2] }));
  });

  it('removes a channel and adds one at the end', async () => {
    render(<TeamChannelRanking teamExternalId="134838" teamName="New York Rangers" />);

    fireEvent.click(await screen.findByRole('button', { name: 'Remove US: MSG' }));
    await waitFor(() => expect(transport.put).toHaveBeenLastCalledWith(
      '/iptv/teams/7/channel-mappings', { channelIds: [2, 3] }));

    fireEvent.change(screen.getByLabelText('Search channels to add'), { target: { value: 'US:' } });
    fireEvent.click(await screen.findByRole('button', { name: 'US: TNT' }));

    await waitFor(() => expect(transport.put).toHaveBeenLastCalledWith(
      '/iptv/teams/7/channel-mappings', { channelIds: [2, 3, 4] }));
  });

  it('asks for a league first when the library has no row for the team', async () => {
    transport.get.mockImplementation(async (path: string) => {
      if (path === '/teams') return { data: [] };
      throw new Error('Unconfigured request ' + path);
    });

    render(<TeamChannelRanking teamExternalId="134838" teamName="New York Rangers" />);

    expect(await screen.findByText(/Add one of this team's leagues first/)).toBeInTheDocument();
  });

  it('keeps focus on the moved row and announces the new rank', async () => {
    render(<TeamChannelRanking teamExternalId="134838" teamName="New York Rangers" />);

    const up = await screen.findByRole('button', { name: 'Move US: ESPN up' });
    up.focus();
    fireEvent.click(up);

    expect(screen.getByRole('button', { name: 'Move US: ESPN up' })).toHaveFocus();
    expect(screen.getByText('US: ESPN moved to rank 2 of 3.')).toBeInTheDocument();

    // At the top the up button is disabled, so the down button takes the focus.
    fireEvent.click(screen.getByRole('button', { name: 'Move US: ESPN up' }));
    expect(screen.getByRole('button', { name: 'Move US: ESPN down' })).toHaveFocus();
    await waitFor(() => expect(transport.put).toHaveBeenCalledTimes(2));
  });

  it('sends saves one at a time and shows the newest list', async () => {
    const replies: Array<() => void> = [];
    transport.put.mockImplementation((_path: string, body: { channelIds: number[] }) => new Promise((resolve) => {
      replies.push(() => resolve({
        data: body.channelIds.map((id, index) => ({
          ...LIST.find((e) => e.channelId === id)!, priority: index, isPreferred: index === 0,
        })),
      }));
    }));
    render(<TeamChannelRanking teamExternalId="134838" teamName="New York Rangers" />);

    fireEvent.click(await screen.findByRole('button', { name: 'Move US: ESPN up' }));
    fireEvent.click(screen.getByRole('button', { name: 'Move US: ESPN up' }));

    // The second save waits for the first reply.
    await waitFor(() => expect(transport.put).toHaveBeenCalledTimes(1));
    replies[0]();
    await waitFor(() => expect(transport.put).toHaveBeenCalledTimes(2));
    expect(transport.put).toHaveBeenLastCalledWith('/iptv/teams/7/channel-mappings', { channelIds: [3, 1, 2] });
    // The first reply is older, so it does not replace the newer list.
    expect(ranks()[0]).toContain('US: ESPN');
    replies[1]();
    await waitFor(() => expect(ranks()[0]).toContain('US: ESPN'));
  });

  it('keeps the drop marker while the pointer moves over a child element', async () => {
    render(<TeamChannelRanking teamExternalId="134838" teamName="New York Rangers" />);
    await screen.findByText('US: ESPN');
    const items = within(screen.getByRole('list')).getAllByRole('listitem');

    fireEvent.dragStart(items[2], { dataTransfer: { effectAllowed: '' } });
    fireEvent.dragOver(items[0]);
    expect(items[0].className).toContain('border-blue-500');
    // jsdom has no DragEvent, so send a mouse event that carries relatedTarget.
    const leave = (relatedTarget: Element) =>
      fireEvent(items[0], new MouseEvent('dragleave', { bubbles: true, relatedTarget }));
    leave(within(items[0]).getByText('US: MSG'));
    expect(items[0].className).toContain('border-blue-500');

    leave(document.body);
    expect(items[0].className).not.toContain('border-blue-500');
  });

  it('says so when the lookup fails and can try again', async () => {
    transport.get.mockImplementationOnce(async () => { throw new Error('Network Error'); });
    render(<TeamChannelRanking teamExternalId="134838" teamName="New York Rangers" />);

    expect(await screen.findByText("Could not load this team's channels.")).toBeInTheDocument();
    expect(screen.queryByText(/Add one of this team's leagues first/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByText('US: MSGSN')).toBeInTheDocument();
  });

  it('looks the team up again when the refresh key changes', async () => {
    let teams: Array<{ id: number }> = [];
    transport.get.mockImplementation(async (path: string) => {
      if (path === '/teams') return { data: teams };
      if (path === '/iptv/teams/7/channel-mappings') return { data: LIST };
      throw new Error('Unconfigured request ' + path);
    });
    const { rerender } = render(<TeamChannelRanking teamExternalId="134838" teamName="New York Rangers" refreshKey={0} />);
    expect(await screen.findByText(/Add one of this team's leagues first/)).toBeInTheDocument();

    teams = [{ id: 7 }];
    rerender(<TeamChannelRanking teamExternalId="134838" teamName="New York Rangers" refreshKey={1} />);

    expect(await screen.findByText('US: MSGSN')).toBeInTheDocument();
  });

  it('drops a late save reply after the team changes', async () => {
    const otherTeam = [entry(5, 'US: NESN', 0), entry(6, 'US: NBCSB', 1)];
    let release: () => void = () => {};
    transport.get.mockImplementation(async (path: string, config?: { params?: { externalId?: string } }) => {
      if (path === '/teams') return { data: [{ id: config?.params?.externalId === 'bruins' ? 8 : 7 }] };
      if (path === '/iptv/teams/7/channel-mappings') return { data: LIST };
      if (path === '/iptv/teams/8/channel-mappings') return { data: otherTeam };
      throw new Error('Unconfigured request ' + path);
    });
    transport.put.mockImplementation((path: string, body: { channelIds: number[] }) => new Promise((resolve) => {
      const source = path.includes('/7/') ? LIST : otherTeam;
      const reply = () => resolve({
        data: body.channelIds.map((id, index) => ({
          ...source.find((e) => e.channelId === id)!, priority: index, isPreferred: index === 0,
        })),
      });
      if (path.includes('/7/')) release = reply; else reply();
    }));
    const { rerender } = render(<TeamChannelRanking teamExternalId="rangers" teamName="New York Rangers" />);

    fireEvent.click(await screen.findByRole('button', { name: 'Move US: ESPN up' }));
    await waitFor(() => expect(transport.put).toHaveBeenCalledTimes(1));

    rerender(<TeamChannelRanking teamExternalId="bruins" teamName="Boston Bruins" />);
    expect(await screen.findByText('US: NESN')).toBeInTheDocument();
    release();
    await waitFor(() => expect(screen.queryByText('US: ESPN')).not.toBeInTheDocument());
    expect(screen.getByText('US: NBCSB')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Move US: NBCSB up' }));
    await waitFor(() => expect(transport.put).toHaveBeenLastCalledWith(
      '/iptv/teams/8/channel-mappings', { channelIds: [6, 5] }));
  });

  it('resets a failed save to the new team list after a late reply for the old team', async () => {
    const otherTeam = [entry(5, 'US: NESN', 0), entry(6, 'US: NBCSB', 1)];
    let release: () => void = () => {};
    transport.get.mockImplementation(async (path: string, config?: { params?: { externalId?: string } }) => {
      if (path === '/teams') return { data: [{ id: config?.params?.externalId === 'bruins' ? 8 : 7 }] };
      if (path === '/iptv/teams/7/channel-mappings') return { data: LIST };
      if (path === '/iptv/teams/8/channel-mappings') return { data: otherTeam };
      throw new Error('Unconfigured request ' + path);
    });
    transport.put.mockImplementation((path: string, body: { channelIds: number[] }) => new Promise((resolve, reject) => {
      if (path.includes('/8/')) {
        reject(new Error('Server error'));
        return;
      }
      release = () => resolve({
        data: body.channelIds.map((id, index) => ({
          ...LIST.find((e) => e.channelId === id)!, priority: index, isPreferred: index === 0,
        })),
      });
    }));
    const { rerender } = render(<TeamChannelRanking teamExternalId="rangers" teamName="New York Rangers" />);

    fireEvent.click(await screen.findByRole('button', { name: 'Move US: ESPN up' }));
    await waitFor(() => expect(transport.put).toHaveBeenCalledTimes(1));
    rerender(<TeamChannelRanking teamExternalId="bruins" teamName="Boston Bruins" />);
    expect(await screen.findByText('US: NESN')).toBeInTheDocument();
    release();

    fireEvent.click(screen.getByRole('button', { name: 'Move US: NBCSB up' }));
    await waitFor(() => expect(transport.put).toHaveBeenCalledTimes(2));

    await waitFor(() => expect(ranks('Boston Bruins')).toEqual([
      expect.stringContaining('US: NESN'),
      expect.stringContaining('US: NBCSB'),
    ]));
    expect(screen.queryByText('US: ESPN')).not.toBeInTheDocument();
    expect(screen.queryByText('US: MSG')).not.toBeInTheDocument();
  });
});
