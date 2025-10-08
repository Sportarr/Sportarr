import React from 'react';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItemDescription from 'Components/DescriptionList/DescriptionListItemDescription';
import DescriptionListItemTitle from 'Components/DescriptionList/DescriptionListItemTitle';
import FieldSet from 'Components/FieldSet';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';

function MoreInfo() {
  return (
    <FieldSet legend={translate('MoreInfo')}>
      <DescriptionList>
        <DescriptionListItemTitle>
          {translate('HomePage')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://fightarr.tv/">fightarr.tv</Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>{translate('Wiki')}</DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://wiki.servarr.com/fightarr">
            wiki.servarr.com/fightarr
          </Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>
          {translate('Forums')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://forums.fightarr.tv/">forums.fightarr.tv</Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>
          {translate('Twitter')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://twitter.com/fightarrtv">@fightarrtv</Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>
          {translate('Discord')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://discord.fightarr.tv/">discord.fightarr.tv</Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>{translate('IRC')}</DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="irc://irc.libera.chat/#fightarr">
            {translate('IRCLinkText')}
          </Link>
        </DescriptionListItemDescription>
        <DescriptionListItemDescription>
          <Link to="https://web.libera.chat/?channels=#fightarr">
            {translate('LiberaWebchat')}
          </Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>
          {translate('Donations')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://fightarr.tv/donate">fightarr.tv/donate</Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>
          {translate('Source')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://github.com/Fightarr/Fightarr/">
            github.com/Fightarr/Fightarr
          </Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>
          {translate('FeatureRequests')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://forums.fightarr.tv/">forums.fightarr.tv</Link>
        </DescriptionListItemDescription>
        <DescriptionListItemDescription>
          <Link to="https://github.com/Fightarr/Fightarr/issues">
            github.com/Fightarr/Fightarr/issues
          </Link>
        </DescriptionListItemDescription>
      </DescriptionList>
    </FieldSet>
  );
}

export default MoreInfo;
