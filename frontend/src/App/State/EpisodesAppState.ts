import AppSectionState from 'App/State/AppSectionState';
import Column from 'Components/Table/Column';
import FightCard from 'FightCard/FightCard';

interface EpisodesAppState extends AppSectionState<FightCard> {
  columns: Column[];
}

export default EpisodesAppState;
