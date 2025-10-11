import FightCard from 'FightCard/FightCard';
import { update } from 'Store/Actions/baseActions';

function updateEpisodes(
  section: string,
  episodes: FightCard[],
  episodeIds: number[],
  options: Partial<FightCard>
) {
  const data = episodes.reduce<FightCard[]>((result, item) => {
    if (episodeIds.indexOf(item.id) > -1) {
      result.push({
        ...item,
        ...options,
      });
    } else {
      result.push(item);
    }

    return result;
  }, []);

  return update({ section, data });
}

export default updateEpisodes;
