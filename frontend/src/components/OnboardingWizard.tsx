import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import {
  CheckCircleIcon,
  SignalIcon,
  ArrowDownTrayIcon,
  FolderIcon,
  ArrowPathIcon,
  ServerIcon,
  MagnifyingGlassIcon,
  SparklesIcon,
  LockClosedIcon,
  ExclamationTriangleIcon,
  InformationCircleIcon,
} from '@heroicons/react/24/outline';
import { useAuth } from '../contexts/AuthContext';
import apiClient from '../api/client';
import FileBrowserModal from './FileBrowserModal';
import { toApiIndexer } from '../utils/indexerPayload';
import { NAMING_CONTEXT_PANEL, NAMING_GUIDANCE, OPTION_CARD_SELECTED, OPTION_CARD_UNSELECTED } from '../utils/designTokens';
import { getNamingWarning, NAMING_CONTEXT_TEXT, renderNamingExample } from '../utils/namingGuidance';

/**
 * First-run setup guide. Walks a new install from nothing to a working setup in
 * one flow, driving the same endpoints the settings pages use so the user never
 * hops between them. Usenet/torrent is the primary, fully-guided path (the
 * majority of users); IPTV is offered as a secondary guided path. The final step
 * hands off to the league picker, which is inherently interactive (you choose
 * teams), so it stays forward motion rather than back-and-forth.
 */

interface OnboardingWizardProps {
  onClose: () => void;
  onComplete: () => void;
  isFirstRunGuide?: boolean;
}

// Steps are assembled from the source selection: both acquisition paths can
// be active at once (a user with an IPTV sub AND a torrent client is
// common), so the flow is a union rather than an either/or fork.
function buildSteps(wantsDownload: boolean, wantsIptv: boolean): { key: string; title: string }[] {
  const steps = [
    { key: 'intro', title: 'Welcome' },
    { key: 'security', title: 'Security' },
    { key: 'welcome', title: 'Sources' },
    { key: 'root', title: 'Library' },
    { key: 'quality', title: 'Preferences' },
  ];
  if (wantsDownload) {
    steps.push({ key: 'client', title: 'Downloader' });
    steps.push({ key: 'indexer', title: 'Indexer' });
  }
  if (wantsIptv) {
    steps.push({ key: 'provider', title: 'Provider' });
  }
  steps.push({ key: 'playback', title: 'Playback' });
  steps.push({ key: 'finish', title: 'Done' });
  return steps;
}

// Playback apps the metadata agent step covers, with each brand's accent colour
// used for the selected glow. Instructions mirror Settings > General > Media
// Server Agents (which mirrors each agent's README) - the wizard must never
// drift from those. Downloads open the GitHub releases page in a NEW tab so
// the user can't lose the wizard mid-setup.
const RELEASES_URL = 'https://github.com/Sportarr/Sportarr/releases/latest';
const PLEX_PROVIDER_URL = 'https://sportarr.net/plex';
const JELLYFIN_REPO_URL = 'https://raw.githubusercontent.com/sportarr/Sportarr/main/agents/jellyfin/manifest.json';

const PLAYBACK_APPS = [
  { key: 'plex', label: 'Plex', accent: '#e5a00d' },
  { key: 'jellyfin', label: 'Jellyfin', accent: '#a05cc5' },
  { key: 'emby', label: 'Emby', accent: '#52b54b' },
] as const;

const APP_STEPS: Record<string, string[]> = {
  plex: [
    'In Plex Web: Settings -> Metadata Agents -> + Add Provider.',
    'Paste the provider URL below, click + Add Agent, name it (e.g. "Sportarr"), and Save.',
    'Restart Plex Media Server.',
    'Add a TV Shows library pointed at your sports folder and select the Sportarr agent you created.',
  ],
  jellyfin: [
    'In Jellyfin: Dashboard -> Plugins -> Repositories -> Add, name it "Sportarr", and paste the repository URL below.',
    'Open Catalog -> Metadata, install the Sportarr plugin, then restart Jellyfin.',
    'On your sports library, move Sportarr to the top of the metadata downloaders and image fetchers.',
    'Refresh metadata - your games fill in from Sportarr.',
  ],
  emby: [
    'Download the Sportarr Emby plugin ZIP from the releases page below and extract it.',
    'Copy the DLL into Emby\'s "plugins" folder, then restart Emby Server.',
    'On your sports library, enable the Sportarr metadata provider.',
    'Refresh metadata - your games fill in from Sportarr.',
  ],
};

// Order the naming presets with the recommended full-details preset first so
// it reads as the default it actually is.
function orderNamingPresets(keys: string[], presets: Record<string, { description: string }>): string[] {
  const isDetailed = (k: string) => /detail|full/i.test(k + ' ' + (presets[k]?.description ?? ''));
  return [...keys].sort((a, b) => Number(isDetailed(b)) - Number(isDetailed(a)));
}

// Download client types the guide offers. Value maps to the backend enum
// (QBittorrent=0, Transmission=1, Deluge=2, Sabnzbd=5, NzbGet=6). SABnzbd
// authenticates with an API key; the rest use username/password.
const CLIENT_TYPES = [
  { value: 0, label: 'qBittorrent', port: 8080, auth: 'userpass', protocol: 'torrent' },
  { value: 1, label: 'Transmission', port: 9091, auth: 'userpass', protocol: 'torrent' },
  { value: 2, label: 'Deluge', port: 8112, auth: 'userpass', protocol: 'torrent' },
  { value: 5, label: 'SABnzbd', port: 8080, auth: 'apikey', protocol: 'usenet' },
  { value: 6, label: 'NZBGet', port: 6789, auth: 'userpass', protocol: 'usenet' },
] as const;

export default function OnboardingWizard({ onClose, onComplete, isFirstRunGuide = false }: OnboardingWizardProps) {
  const navigate = useNavigate();
  const { login } = useAuth();

  // Source selection: both can be on at once (see buildSteps).
  const [wantsDownload, setWantsDownload] = useState(true);
  const [wantsIptv, setWantsIptv] = useState(false);
  const [stepKey, setStepKey] = useState('intro');

  // Security step: authentication method, mirroring Settings > General.
  const [authMethod, setAuthMethod] = useState<'none' | 'forms' | 'basic' | 'external'>('none');
  const [authUser, setAuthUser] = useState('');
  const [authPass, setAuthPass] = useState('');
  const needsCredentials = authMethod === 'forms' || authMethod === 'basic';

  // Playback step: which media server the user is setting up.
  const [playbackApp, setPlaybackApp] = useState<'plex' | 'jellyfin' | 'emby'>('plex');
  const [busy, setBusy] = useState(false);
  const [progress, setProgress] = useState<string | null>(null);

  // Library folder, with a browse dialog and a live check that the typed
  // path really exists (the same signals the settings pages give).
  const [rootPath, setRootPath] = useState('/media/sports');
  const [showBrowser, setShowBrowser] = useState(false);
  const [pathCheck, setPathCheck] = useState<{ exists: boolean; folders: string[] } | null>(null);
  const [checkingPath, setCheckingPath] = useState(false);
  // Optional remote path mapping, for when the download client sees a different
  // path than Sportarr does (client on another host/container).
  const [showRemotePath, setShowRemotePath] = useState(false);
  const [rpHost, setRpHost] = useState('');
  const [rpRemote, setRpRemote] = useState('');
  const [rpLocal, setRpLocal] = useState('');
  const [namingPresets, setNamingPresets] = useState<Record<string, { format: string; description: string }>>({});
  const [namingChoice, setNamingChoice] = useState<string | null>(null);
  const [namingContextLoaded, setNamingContextLoaded] = useState(false);
  const [namingPresetsLoaded, setNamingPresetsLoaded] = useState(false);

  // Download client form plus the list already saved (this session or
  // before), each entry editable so a typo doesn't require Settings.
  const [dcType, setDcType] = useState(0);
  // The host defaults to localhost, which is a helpful placeholder and a
  // terrible thing to save on its own. Reopening the guide on a configured
  // install and clicking past this step used to create a qBittorrent client
  // at localhost:8080 that nobody asked for. The step is only saved once the
  // user has actually touched it.
  const [dcHost, setDcHost] = useState('localhost');
  const [dcFormTouched, setDcFormTouched] = useState(false);
  const [dcPort, setDcPort] = useState(8080);
  const [dcUser, setDcUser] = useState('');
  const [dcPass, setDcPass] = useState('');
  const [dcApiKey, setDcApiKey] = useState('');
  const [dcTest, setDcTest] = useState<{ ok: boolean; msg: string } | null>(null);
  const [addedClients, setAddedClients] = useState<{ id: number; label: string; raw: any }[]>([]);
  const [editingClientId, setEditingClientId] = useState<number | null>(null);

  // Indexer form plus the editable list of saved indexers. The API key is
  // shown for the Prowlarr instructions so the user can copy it straight in.
  const [ixProtocol, setIxProtocol] = useState<'usenet' | 'torrent'>('usenet');
  const [ixName, setIxName] = useState('');
  const [ixUrl, setIxUrl] = useState('');
  const [ixApiKey, setIxApiKey] = useState('');
  const [ixTest, setIxTest] = useState<{ ok: boolean; msg: string } | null>(null);
  const [addedIndexers, setAddedIndexers] = useState<{ id: number; label: string; raw: any }[]>([]);
  const [editingIndexerId, setEditingIndexerId] = useState<number | null>(null);
  const [ixOriginalImplementation, setIxOriginalImplementation] = useState<string | null>(null);
  const [ixTypeChanged, setIxTypeChanged] = useState(false);
  const [sportarrApiKey, setSportarrApiKey] = useState('');

  // Existing-install awareness: when the guide is reopened on a configured
  // install (Run Setup Guide on System Health), it hydrates from current
  // settings and shows what's already in place instead of starting blank.
  const [hasExistingCreds, setHasExistingCreds] = useState(false);
  const [currentNamingFormat, setCurrentNamingFormat] = useState('');
  const [currentRenameEvents, setCurrentRenameEvents] = useState(false);
  // IPTV provider form plus the editable list of connected providers
  // (multiple providers are fully supported, same as the sources page).
  const [pName, setPName] = useState('');
  const [pType, setPType] = useState<'M3U' | 'Xtream'>('M3U');
  const [pUrl, setPUrl] = useState('');
  const [pUser, setPUser] = useState('');
  const [pPass, setPPass] = useState('');
  const [pEpg, setPEpg] = useState('');
  const [channelCount, setChannelCount] = useState<number | null>(null);
  const [addedProviders, setAddedProviders] = useState<{ id: number; label: string; raw: any }[]>([]);
  const [editingProviderId, setEditingProviderId] = useState<number | null>(null);

  const [releaseSetup, setReleaseSetup] = useState<'standard' | 'recommended' | null>('standard');
  const [releaseSetupTouched, setReleaseSetupTouched] = useState(false);
  const releaseSetupTouchedRef = useRef(false);
  const [hasLegacyReleasePreferences, setHasLegacyReleasePreferences] = useState(false);

  // Hydrate from the current install so a reopened guide reflects reality:
  // sources selected from what exists, library path prefilled, existing
  // clients/indexers/providers listed, auth method and username shown.
  useEffect(() => {
    (async () => {
      try {
        const { data: st } = await apiClient.get<any>('/onboarding/status');
        if (st && (st.hasDownloadClient || st.hasEnabledIndexer || st.hasIptvSource)) {
          setWantsDownload(Boolean(st.hasDownloadClient || st.hasEnabledIndexer));
          setWantsIptv(Boolean(st.hasIptvSource));
        }
      } catch { /* fresh-install defaults stand */ }
      try {
        const { data: settings } = await apiClient.get<any>('/settings');
        const security = JSON.parse(settings.securitySettings || '{}');
        const method = security.authenticationMethod;
        if (method === 'forms' || method === 'basic' || method === 'external') {
          setAuthMethod(method);
          if (security.username) {
            setAuthUser(security.username);
            setHasExistingCreds(true);
          }
        }
        const media = JSON.parse(settings.mediaManagementSettings || '{}');
        if (media.standardFileFormat) setCurrentNamingFormat(media.standardFileFormat);
        setCurrentRenameEvents(media.renameEvents === true);
      } catch { /* defaults stand */ }
      finally { setNamingContextLoaded(true); }
      try {
        const { data: roots } = await apiClient.get<any[]>('/rootfolder');
        if (Array.isArray(roots) && roots.length > 0 && roots[0]?.path) setRootPath(roots[0].path);
      } catch { /* default path stands */ }
      try {
        const { data: clients } = await apiClient.get<any[]>('/downloadclient');
        if (Array.isArray(clients) && clients.length > 0) {
          setAddedClients(clients.map((c) => ({ id: c.id, label: `${c.name} (${c.host}:${c.port})`, raw: c })));
        }
      } catch { /* none listed */ }
      try {
        const { data: ixs } = await apiClient.get<any[]>('/indexer');
        if (Array.isArray(ixs) && ixs.length > 0) {
          setAddedIndexers(ixs.map((x) => ({ id: x.id, label: x.name, raw: x })));
        }
      } catch { /* none listed */ }
      try {
        const { data: sources } = await apiClient.get<any[]>('/iptv/sources');
        if (Array.isArray(sources) && sources.length > 0) {
          setAddedProviders(sources.map((s) => ({ id: s.id, label: s.name, raw: s })));
        }
      } catch { /* none listed */ }
    })();
  }, []);

  // Show the recommendation choice saved on an existing install.
  useEffect(() => {
    (async () => {
      try {
        const { data } = await apiClient.get<{ useRecommendedReleaseSettings?: boolean }>('/trash/settings');
        if (data.useRecommendedReleaseSettings === true) {
          if (!releaseSetupTouchedRef.current) setReleaseSetup('recommended');
        }
        if (data.useRecommendedReleaseSettings == null) {
          if (!releaseSetupTouchedRef.current) setReleaseSetup(null);
          setHasLegacyReleasePreferences(true);
        }
      } catch { /* The new-install choice remains Standard. */ }
    })();
  }, []);

  // Load the naming presets and default to the most detailed one.
  useEffect(() => {
    (async () => {
      try {
        const { data } = await apiClient.get<{ file: Record<string, { format: string; description: string }> }>(
          '/trash/naming-presets?enableMultiPartEpisodes=true'
        );
        const file = data.file ?? {};
        setNamingPresets(file);
      } catch {
        // Non-fatal: naming just won't be offered.
      } finally {
        setNamingPresetsLoaded(true);
      }
    })();
  }, []);

  const stockNamingFormat = namingPresets['plex-standard']?.format === currentNamingFormat
    || currentNamingFormat === '{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}';
  const recommendedNamingKey = orderNamingPresets(Object.keys(namingPresets), namingPresets)[0] ?? '';
  const namingKey = namingChoice ?? (isFirstRunGuide && stockNamingFormat ? recommendedNamingKey : '');
  const shouldApplyNamingPreset = Boolean(namingKey);
  const namingWarning = getNamingWarning(
    shouldApplyNamingPreset ? namingPresets[namingKey]?.format ?? '' : currentNamingFormat,
    shouldApplyNamingPreset || currentRenameEvents,
  );
  const steps = buildSteps(wantsDownload, wantsIptv);
  const stepIndex = Math.max(0, steps.findIndex((s) => s.key === stepKey));
  const clientAuth = CLIENT_TYPES.find((c) => c.value === dcType)?.auth ?? 'userpass';
  const isXtream = pType === 'Xtream';

  // Live existence check for the typed library path (debounced). Uses the
  // same /filesystem endpoint the settings folder browser uses, so what the
  // user sees here matches what Browse shows.
  useEffect(() => {
    if (stepKey !== 'root') return;
    const trimmed = rootPath.trim();
    if (!trimmed) { setPathCheck(null); return; }
    let cancelled = false;
    setCheckingPath(true);
    const timer = setTimeout(async () => {
      try {
        const { data } = await apiClient.get<{ directories?: { name: string; type: string }[] }>(
          `/filesystem?path=${encodeURIComponent(trimmed)}&includeFiles=false`
        );
        if (cancelled) return;
        const folders = (data.directories ?? []).filter((d) => d.type !== 'file').map((d) => d.name);
        setPathCheck({ exists: true, folders });
      } catch {
        if (!cancelled) setPathCheck({ exists: false, folders: [] });
      } finally {
        if (!cancelled) setCheckingPath(false);
      }
    }, 450);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [rootPath, stepKey]);

  // Sportarr's own API key (bootstrapped on window), handed to the Prowlarr
  // instructions with a copy button so the user never leaves the wizard.
  useEffect(() => {
    if (stepKey !== 'indexer' || sportarrApiKey) return;
    const key = window.Sportarr?.apiKey;
    if (key) setSportarrApiKey(key);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stepKey]);

  // The server remembers the dismissal so the guide stays closed on every
  // machine. localStorage stays as a same-browser fast path.
  const persistDismissal = () => {
    localStorage.setItem('sportarr.onboardingDismissed', '1');
    apiClient.post('/onboarding/dismiss').catch(() => { /* localStorage still covers this browser */ });
  };
  const dismiss = () => {
    persistDismissal();
    onClose();
  };
  const finish = () => {
    persistDismissal();
    onComplete();
  };

  // Save the login preference. Critically, if login is being turned on we also
  // establish a session right away (via the auth context) so enabling auth does
  // NOT bounce the user to /login mid-setup - they stay signed in here, and use
  // the credentials next time they open Sportarr.
  const saveSecurity = async (): Promise<boolean> => {
    // On a reopened guide, an untouched password field means "keep the
    // existing credentials" - only a typed password writes new ones.
    const writingCredentials = needsCredentials && (authPass.trim().length > 0 || !hasExistingCreds);
    if (writingCredentials && (!authUser.trim() || authPass.trim().length < 6)) {
      toast.error('Enter a username and a password (at least 6 characters)');
      return false;
    }
    setBusy(true);
    try {
      const { data: settings } = await apiClient.get<any>('/settings');
      const security = JSON.parse(settings.securitySettings || '{}');
      security.authenticationMethod = authMethod;
      if (writingCredentials) {
        security.username = authUser.trim();
        security.password = authPass;
      }
      await apiClient.put('/settings', { ...settings, securitySettings: JSON.stringify(security) });
      if (writingCredentials) {
        // Establish a session right away so enabling auth (forms OR basic -
        // the session cookie is honored ahead of the basic challenge) does
        // not bounce the user out of the wizard mid-setup.
        try {
          await login(authUser.trim(), authPass, true);
        } catch {
          // Session couldn't be established now; the credentials still work on next load.
        }
      }
      return true;
    } catch (err: any) {
      toast.error('Could not save security settings', { description: err?.response?.data?.error || err?.message });
      return false;
    } finally {
      setBusy(false);
    }
  };

  const buildClientPayload = () => ({
    name: CLIENT_TYPES.find((c) => c.value === dcType)?.label ?? 'Download Client',
    type: dcType,
    host: dcHost.trim(),
    port: dcPort,
    username: clientAuth === 'userpass' ? dcUser.trim() : '',
    password: clientAuth === 'userpass' ? dcPass : '',
    apiKey: clientAuth === 'apikey' ? dcApiKey.trim() : '',
    category: 'sportarr',
    useSsl: false,
    enabled: true,
    priority: 1,
    tags: [] as number[],
  });

  const buildIndexerPayload = () => {
    const selectedImplementation = ixProtocol === 'usenet' ? 'Newznab' : 'Torznab';
    const implementation = editingIndexerId != null && !ixTypeChanged && ixOriginalImplementation
      ? ixOriginalImplementation
      : selectedImplementation;
    const edited = toApiIndexer({
      name: ixName.trim() || (ixProtocol === 'usenet' ? 'Newznab' : 'Torznab'),
      implementation,
      enabled: true,
      enableRss: true,
      enableAutomaticSearch: true,
      enableInteractiveSearch: true,
      priority: 25,
      baseUrl: ixUrl.trim(),
      apiKey: ixApiKey.trim(),
      categories: [],
    });
    if (editingIndexerId == null) return edited;

    const existing = addedIndexers.find((indexer) => indexer.id === editingIndexerId)?.raw;
    if (!existing || !Array.isArray(existing.fields)) return { ...edited, id: editingIndexerId };

    const editedFields = new Map(
      edited.fields
        .filter((field) => field.name === 'baseUrl' || field.name === 'apiKey')
        .map((field) => [field.name, field.value]),
    );
    const fields = existing.fields.map((field: { name: string; value: string | string[] }) => (
      editedFields.has(field.name)
        ? { ...field, value: editedFields.get(field.name)! }
        : field
    ));
    for (const [name, value] of editedFields) {
      if (!fields.some((field: { name: string }) => field.name === name)) fields.push({ name, value });
    }

    return {
      ...existing,
      id: editingIndexerId,
      name: edited.name,
      implementation: edited.implementation,
      fields,
    };
  };

  const testClient = async () => {
    setBusy(true);
    setDcTest(null);
    try {
      await apiClient.post('/downloadclient/test', buildClientPayload());
      setDcTest({ ok: true, msg: 'Connected' });
      // Testing the form is asking for this client to be kept. The prefilled
      // localhost setup is right for most people, so they can test it without
      // editing a field, and Save then walked past it and finished onboarding
      // with no client at all.
      setDcFormTouched(true);
    } catch (err: any) {
      setDcTest({ ok: false, msg: err?.response?.data?.error || err?.message || 'Could not connect' });
    } finally {
      setBusy(false);
    }
  };

  const testIndexer = async () => {
    setBusy(true);
    setIxTest(null);
    try {
      await apiClient.post('/indexer/test', buildIndexerPayload());
      setIxTest({ ok: true, msg: 'Connected' });
    } catch (err: any) {
      setIxTest({
        ok: false,
        msg: err?.response?.data?.message || err?.response?.data?.error || err?.message || 'Could not connect',
      });
    } finally {
      setBusy(false);
    }
  };

  const saveQualityStep = async (): Promise<boolean> => {
    setBusy(true);
    const failures: string[] = [];
    try {
      if (releaseSetup && releaseSetupTouched) {
        try {
          await apiClient.post('/onboarding/release-preferences', { mode: releaseSetup });
        }
        catch (err: any) {
          failures.push(`release preferences (${err?.response?.data?.error || err?.message || 'unknown error'})`);
        }
      }
      const preset = namingPresets[namingKey];
      if (preset?.format && shouldApplyNamingPreset) {
        try {
          const { data: settings } = await apiClient.get<any>('/settings');
          const media = JSON.parse(settings.mediaManagementSettings || '{}');
          media.standardFileFormat = preset.format;
          media.renameEvents = true;
          await apiClient.put('/settings', { ...settings, mediaManagementSettings: JSON.stringify(media) });
        } catch (err: any) {
          failures.push(`file naming (${err?.response?.data?.error || err?.message || 'unknown error'})`);
        }
      }

      if (failures.length > 0) {
        toast.error('Some of these settings were not saved', {
          description: `${failures.join('; ')}. Fix this under Settings, or try again.`,
        });
        return false;
      }

      return true;
    } finally {
      setBusy(false);
    }
  };

  const saveRootFolder = async (): Promise<boolean> => {
    const trimmed = rootPath.trim();
    if (!trimmed) {
      toast.error('Enter a folder path for your library');
      return false;
    }
    setBusy(true);
    try {
      try {
        await apiClient.post('/rootfolder', { path: trimmed });
      } catch (err: any) {
        const msg = err?.response?.data?.error || err?.message || 'Could not create the folder';
        if (!String(msg).toLowerCase().includes('already')) {
          toast.error('Could not set up the library folder', { description: msg });
          return false;
        }
      }
      // Optional remote path mapping (client sees a different path than Sportarr).
      if (showRemotePath && rpHost.trim() && rpRemote.trim() && rpLocal.trim()) {
        try {
          await apiClient.post('/remotepathmapping', {
            host: rpHost.trim(),
            remotePath: rpRemote.trim(),
            localPath: rpLocal.trim(),
          });
        } catch (err: any) {
          toast.error('Library folder set, but the remote path mapping failed', {
            description: err?.response?.data?.error || err?.message,
          });
        }
      }
      return true;
    } finally {
      setBusy(false);
    }
  };

  const resetClientForm = () => {
    // Host intentionally resets to EMPTY: Save & Next treats an empty host
    // with clients already added as "done", so finishing the step never
    // saves an accidental duplicate.
    setDcHost('');
    setDcUser('');
    setDcPass('');
    setDcApiKey('');
    setDcTest(null);
    setEditingClientId(null);
  };

  // Create when the form is fresh, update when an existing entry was
  // loaded via its Edit button.
  const saveClient = async (): Promise<boolean> => {
    if (!dcHost.trim()) {
      toast.error('Enter the download client host');
      return false;
    }
    setBusy(true);
    try {
      const payload = buildClientPayload();
      if (editingClientId != null) {
        const existing = addedClients.find((c) => c.id === editingClientId);
        const { data } = await apiClient.put(`/downloadclient/${editingClientId}`, { ...existing?.raw, ...payload, id: editingClientId });
        setAddedClients((prev) => prev.map((c) => (c.id === editingClientId
          ? { id: editingClientId, label: `${payload.name} (${payload.host}:${payload.port})`, raw: data ?? { ...existing?.raw, ...payload } }
          : c)));
        toast.success('Download client updated');
      } else {
        const { data } = await apiClient.post<any>('/downloadclient', payload);
        setAddedClients((prev) => [...prev, { id: data?.id, label: `${payload.name} (${payload.host}:${payload.port})`, raw: data ?? payload }]);
      }
      return true;
    } catch (err: any) {
      toast.error('Could not save the download client', {
        description: err?.response?.data?.error || err?.message,
      });
      return false;
    } finally {
      setBusy(false);
    }
  };

  const saveClientAndAddAnother = async () => {
    const wasEditing = editingClientId != null;
    if (await saveClient()) {
      resetClientForm();
      if (!wasEditing) toast.success('Download client added - enter the next one');
    }
  };

  const editClient = (entry: { id: number; raw: any }) => {
    const c = entry.raw ?? {};
    setDcType(c.type ?? 0);
    setDcHost(c.host ?? '');
    setDcPort(c.port ?? 8080);
    setDcUser(c.username ?? '');
    setDcPass(c.password ?? '');
    setDcApiKey(c.apiKey ?? '');
    setDcTest(null);
    setEditingClientId(entry.id);
  };

  const resetIndexerForm = () => {
    setIxName('');
    setIxUrl('');
    setIxApiKey('');
    setIxTest(null);
    setEditingIndexerId(null);
    setIxOriginalImplementation(null);
    setIxTypeChanged(false);
  };

  const saveIndexer = async (): Promise<boolean> => {
    if (!ixUrl.trim()) {
      toast.error('Enter the indexer URL');
      return false;
    }
    setBusy(true);
    try {
      const payload = buildIndexerPayload();
      if (editingIndexerId != null) {
        const { data } = await apiClient.put(`/indexer/${editingIndexerId}`, payload);
        setAddedIndexers((prev) => prev.map((x) => (x.id === editingIndexerId
          ? { id: editingIndexerId, label: payload.name, raw: data?.fields ? data : payload }
          : x)));
        toast.success('Indexer updated');
      } else {
        const { data } = await apiClient.post<any>('/indexer', payload);
        setAddedIndexers((prev) => [...prev, { id: data?.id, label: payload.name, raw: { ...payload, id: data?.id } }]);
      }
      return true;
    } catch (err: any) {
      toast.error('Could not save the indexer', {
        description: err?.response?.data?.message || err?.response?.data?.error || err?.message,
      });
      return false;
    } finally {
      setBusy(false);
    }
  };

  const saveIndexerAndAddAnother = async () => {
    const wasEditing = editingIndexerId != null;
    if (await saveIndexer()) {
      resetIndexerForm();
      if (!wasEditing) toast.success('Indexer added - enter the next one');
    }
  };

  const editIndexer = (entry: { id: number; raw: any }) => {
    const x = entry.raw ?? {};
    const fields = new Map<string, string | string[]>(
      Array.isArray(x.fields)
        ? x.fields.map((field: { name: string; value: string | string[] }) => [field.name, field.value])
        : [],
    );
    const implementation = x.implementation ?? (x.type === 1 ? 'Newznab' : 'Torznab');
    setIxProtocol(implementation === 'Newznab' ? 'usenet' : 'torrent');
    setIxOriginalImplementation(implementation);
    setIxTypeChanged(false);
    setIxName(x.name ?? '');
    setIxUrl(String(fields.get('baseUrl') ?? x.baseUrl ?? x.url ?? ''));
    setIxApiKey(String(fields.get('apiKey') ?? x.apiKey ?? ''));
    setIxTest(null);
    setEditingIndexerId(entry.id);
  };

  const resetProviderForm = () => {
    setPName('');
    setPUrl('');
    setPUser('');
    setPPass('');
    setPEpg('');
    setEditingProviderId(null);
  };

  const editProvider = (entry: { id: number; raw: any }) => {
    const s = entry.raw ?? {};
    setPName(s.name ?? '');
    setPType(s.type === 'Xtream' ? 'Xtream' : 'M3U');
    setPUrl(s.url ?? '');
    setPUser(s.username ?? '');
    setPPass(s.password ?? '');
    setEditingProviderId(entry.id);
  };

  const connectProvider = async (): Promise<boolean> => {
    if (!pName.trim() || !pUrl.trim()) {
      toast.error('Give the provider a name and a URL');
      return false;
    }
    if (isXtream && (!pUser.trim() || !pPass.trim())) {
      toast.error('Xtream providers need a username and password');
      return false;
    }
    const body = {
      name: pName.trim(),
      type: pType,
      url: pUrl.trim(),
      username: pUser.trim(),
      password: pPass,
      maxStreams: 1,
      userAgent: '',
      ffmpegInputArgs: '',
    };
    // Update path: an entry loaded via Edit just PUTs the changed fields.
    if (editingProviderId != null) {
      setBusy(true);
      try {
        await apiClient.put(`/iptv/sources/${editingProviderId}`, body);

        // A guide URL typed while editing used to be dropped on the floor, so
        // the provider kept whatever guide it had, or none, and recordings
        // were scheduled against missing programme data.
        const editedGuideUrl = pEpg.trim();
        if (editedGuideUrl) {
          try {
            const { data: existingSources } = await apiClient.get<{ id: number; url: string; iptvSourceId: number | null }[]>('/epg/sources');
            const linked = (Array.isArray(existingSources) ? existingSources : [])
              .find((s) => s.iptvSourceId === editingProviderId);
            if (linked) {
              if (linked.url !== editedGuideUrl) {
                await apiClient.put(`/epg/sources/${linked.id}`, {
                  name: `${pName.trim()} Guide`,
                  url: editedGuideUrl,
                  isActive: true,
                  priority: 25,
                  iptvSourceId: editingProviderId,
                });
              }
            } else {
              await apiClient.post('/epg/sources', {
                name: `${pName.trim()} Guide`,
                url: editedGuideUrl,
                priority: 25,
                iptvSourceId: editingProviderId,
              });
            }
            await apiClient.post('/epg/auto-map');
          } catch (guideErr: any) {
            toast.warning('Provider updated, but the guide could not be saved', {
              description: guideErr?.response?.data?.error || guideErr?.message,
            });
          }
        }

        setAddedProviders((prev) => prev.map((p) => (p.id === editingProviderId
          ? { id: editingProviderId, label: body.name, raw: { ...p.raw, ...body } }
          : p)));
        toast.success('Provider updated');
        return true;
      } catch (err: any) {
        toast.error('Could not update the provider', { description: err?.response?.data?.error || err?.message });
        return false;
      } finally {
        setBusy(false);
      }
    }
    setBusy(true);
    try {
      setProgress('Adding your provider...');
      const { data: source } = await apiClient.post<{ id: number }>('/iptv/sources', body);
      setProgress('Loading channels...');
      let count = 0;
      try {
        const { data: sync } = await apiClient.post<{ channelCount: number }>(`/iptv/sources/${source.id}/sync`);
        count = sync?.channelCount ?? 0;
      } catch {
        // Background sync also runs on add; a slow first sync isn't a failure.
      }
      setChannelCount(count);
      const guideUrl = pEpg.trim();
      if (guideUrl) {
        setProgress('Attaching the program guide...');
        try {
          await apiClient.post('/epg/sources', {
            name: `${pName.trim()} Guide`,
            url: guideUrl,
            priority: 25,
            iptvSourceId: source.id,
          });
          setProgress('Matching channels to the guide...');
          await apiClient.post('/epg/auto-map');
        } catch {
          // Guide is optional; don't block setup if it fails.
        }
      }
      setAddedProviders((prev) => [...prev, { id: source.id, label: body.name, raw: { ...body, id: source.id } }]);
      return true;
    } catch (err: any) {
      toast.error('Could not connect the provider', {
        description: err?.response?.data?.error || err?.message,
      });
      return false;
    } finally {
      setBusy(false);
      setProgress(null);
    }
  };

  const connectProviderAndAddAnother = async () => {
    const wasEditing = editingProviderId != null;
    if (await connectProvider()) {
      resetProviderForm();
      if (!wasEditing) toast.success('Provider connected - enter the next one');
    }
  };

  const goTo = (key: string) => setStepKey(key);
  const nextKey = () => steps[Math.min(steps.length - 1, stepIndex + 1)].key;
  const prevKey = () => steps[Math.max(0, stepIndex - 1)].key;

  const goNext = async () => {
    if (stepKey === 'intro' || stepKey === 'playback') {
      goTo(nextKey());
      return;
    }
    if (stepKey === 'security') {
      if (await saveSecurity()) goTo(nextKey());
      return;
    }
    if (stepKey === 'welcome') {
      if (!wantsDownload && !wantsIptv) {
        toast.error('Pick at least one way you get your games');
        return;
      }
      goTo(nextKey());
      return;
    }
    if (stepKey === 'root') {
      // Next step is 'quality'.
      if (await saveRootFolder()) goTo(nextKey());
      return;
    }
    if (stepKey === 'quality') {
      if (await saveQualityStep()) goTo(nextKey());
      return;
    }
    if (stepKey === 'client') {
      // An untouched form is not a client the user asked for, whether or not
      // one was already added.
      if (!dcFormTouched || !dcHost.trim()) { goTo(nextKey()); return; }
      if (await saveClient()) goTo(nextKey());
      return;
    }
    if (stepKey === 'indexer') {
      // Skippable: Prowlarr users add indexers from there, so an empty URL just
      // moves on rather than blocking the step.
      if (!ixUrl.trim()) { goTo(nextKey()); return; }
      if (await saveIndexer()) goTo(nextKey());
      return;
    }
    if (stepKey === 'provider') {
      // An untouched form is fine when providers already exist.
      if (!pName.trim() && !pUrl.trim() && addedProviders.length > 0) { goTo(nextKey()); return; }
      if (await connectProvider()) goTo(nextKey());
      return;
    }
  };

  const goBack = () => {
    if (stepIndex <= 0) return;
    goTo(prevKey());
  };

  // Advance to the next step without applying this step's settings. The user still
  // passes through every step, they just opt out of this one.
  const skipStep = () => goTo(nextKey());

  const inputCls =
    'w-full rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-white focus:border-red-600 focus:outline-none';
  const labelCls = 'mb-2 block text-sm font-medium text-gray-300';

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4">
      <div className="animate-wizard-modal flex max-h-[85dvh] w-full max-w-2xl flex-col overflow-hidden rounded-2xl border border-gray-800 bg-gray-950 shadow-2xl">
        {/* Keep the step count readable on narrow screens. */}
        <div className="flex items-center justify-between gap-4 border-b border-gray-800 px-6 py-4">
          <div className="hidden items-center gap-1.5 sm:flex">
            {steps.map((s, i) => (
              <div key={s.key} className="flex items-center gap-1.5">
                <div
                  className={`flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-full text-xs font-bold transition-all duration-300 ${
                    i < stepIndex
                      ? 'bg-green-600 text-white'
                      : i === stepIndex
                        ? 'scale-110 bg-red-600 text-white ring-2 ring-red-500/40'
                        : 'bg-gray-800 text-gray-500'
                  }`}
                >
                  {i < stepIndex ? '✓' : i + 1}
                </div>
                {i < steps.length - 1 && <div className="h-px w-2 bg-gray-800" />}
              </div>
            ))}
          </div>
          <span className="hidden flex-shrink-0 text-sm font-medium text-white sm:block">
            {steps[stepIndex]?.title}
          </span>
          <span className="text-sm font-medium text-white sm:hidden">
            Step {stepIndex + 1} of {steps.length} · {steps[stepIndex]?.title}
          </span>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-6 py-6">
          {/* Keyed on the step so each step's content gently animates in. */}
          <div key={stepKey} className="animate-wizard-step">
          {stepKey === 'intro' && (
            <div className="py-4 text-center">
              <div className="mx-auto mb-5 flex h-16 w-16 items-center justify-center rounded-2xl bg-gradient-to-br from-red-600 to-red-800 shadow-lg">
                <SparklesIcon className="h-9 w-9 text-white" />
              </div>
              <h2 className="mb-2 text-3xl font-bold text-white">Welcome to Sportarr</h2>
              <p className="mx-auto mb-6 max-w-md text-sm text-gray-400">
                Thanks for installing Sportarr. This quick setup walks you through everything it takes to
                have your sports finding, grabbing, and organizing themselves - a few steps and it just works.
              </p>
              <div className="mx-auto mb-2 grid max-w-md grid-cols-2 gap-2 text-left sm:grid-cols-3">
                {[
                  { icon: LockClosedIcon, label: 'Secure it' },
                  { icon: FolderIcon, label: 'Library folder' },
                  { icon: SparklesIcon, label: 'Release setup' },
                  { icon: ServerIcon, label: 'Downloader / IPTV' },
                  { icon: MagnifyingGlassIcon, label: 'Indexer' },
                  { icon: CheckCircleIcon, label: 'Pick your sports' },
                ].map((item, i) => (
                  <div
                    key={item.label}
                    className="animate-wizard-fade flex items-center gap-2 rounded-lg border border-gray-800 bg-gray-900/60 px-3 py-2"
                    style={{ animationDelay: `${100 + i * 60}ms` }}
                  >
                    <item.icon className="h-4 w-4 flex-shrink-0 text-red-400" />
                    <span className="truncate text-xs text-gray-300">{item.label}</span>
                  </div>
                ))}
              </div>
              <p className="mt-4 text-xs text-gray-500">Takes about two minutes. You can skip any step you like.</p>
            </div>
          )}

          {stepKey === 'security' && (
            <div>
              <LockClosedIcon className="mb-2 h-7 w-7 text-gray-300" />
              <h2 className="mb-1 text-2xl font-bold text-white">Secure your instance</h2>
              <p className="mb-4 text-sm text-gray-400">
                Optionally require a login to open Sportarr. Leave it off on a trusted local network,
                or set a username and password. You can change this any time in Settings.
              </p>
              <div className="space-y-2">
                {([
                  { value: 'none', label: 'No login', desc: 'Anyone who can reach this address can open Sportarr. Fine on a trusted local network.' },
                  { value: 'forms', label: 'Forms (login page)', desc: 'A Sportarr login page asks for a username and password. The usual choice.' },
                  { value: 'basic', label: 'Basic (browser popup)', desc: "The browser's built-in credentials popup. Handy behind some proxies and tools." },
                  { value: 'external', label: 'External (reverse proxy)', desc: 'Authelia, Authentik, oauth-proxy or similar handles the login in front of Sportarr.' },
                ] as const).map((m) => (
                  <button
                    key={m.value}
                    onClick={() => setAuthMethod(m.value)}
                    className={`w-full rounded-lg border p-3 text-left transition-colors ${
                      authMethod === m.value ? 'border-red-500 bg-red-950/20' : 'border-gray-800 bg-gray-900/60 hover:border-gray-700'
                    }`}
                  >
                    <div className="text-sm font-medium text-white">{m.label}</div>
                    <p className="mt-0.5 text-xs text-gray-400">{m.desc}</p>
                  </button>
                ))}
              </div>
              {needsCredentials && (
                <>
                  <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <div>
                      <label className={labelCls}>Username</label>
                      <input type="text" value={authUser} onChange={(e) => setAuthUser(e.target.value)} className={inputCls} />
                    </div>
                    <div>
                      <label className={labelCls}>Password</label>
                      <input
                        type="password"
                        value={authPass}
                        onChange={(e) => setAuthPass(e.target.value)}
                        placeholder={hasExistingCreds ? 'unchanged unless you type a new one' : ''}
                        className={inputCls}
                      />
                    </div>
                  </div>
                  <p className="mt-2 text-xs text-gray-500">
                    {hasExistingCreds
                      ? 'A login is already set up - leave the password empty to keep it as is.'
                      : "Saving keeps you signed in here - you won't be locked out mid-setup. You'll use these credentials next time you open Sportarr."}
                  </p>
                </>
              )}
              {authMethod === 'external' && (
                <p className="mt-3 rounded-lg border border-gray-800 bg-gray-900/40 p-3 text-xs text-gray-400">
                  Sportarr won't ask for credentials itself - make sure your reverse proxy is already
                  enforcing a login in front of it, or the UI is open to anyone who can reach it.
                </p>
              )}
            </div>
          )}

          {stepKey === 'welcome' && (
            <div>
              <h2 className="mb-1 text-2xl font-bold text-white">Let's get you recording</h2>
              <p className="mb-6 text-sm text-gray-400">
                A few quick steps and Sportarr will find and grab your sports automatically. How do you
                get the games? Pick everything that applies - both work side by side.
              </p>
              <div className="grid gap-3 sm:grid-cols-2">
                <button
                  onClick={() => setWantsDownload(!wantsDownload)}
                  className={`relative rounded-xl border p-4 text-left transition-colors ${
                    wantsDownload ? 'border-red-500 bg-red-950/20' : 'border-gray-800 bg-gray-900 hover:border-gray-700'
                  }`}
                >
                  {wantsDownload && <CheckCircleIcon className="absolute right-3 top-3 h-5 w-5 text-red-400" />}
                  <ArrowDownTrayIcon className="mb-2 h-6 w-6 text-blue-400" />
                  <div className="font-semibold text-white">Usenet or Torrents</div>
                  <p className="mt-1 text-xs text-gray-400">
                    The usual setup. We'll connect your download client and an indexer and start
                    grabbing releases. Fully guided.
                  </p>
                </button>
                <button
                  onClick={() => setWantsIptv(!wantsIptv)}
                  className={`relative rounded-xl border p-4 text-left transition-colors ${
                    wantsIptv ? 'border-red-500 bg-red-950/20' : 'border-gray-800 bg-gray-900 hover:border-gray-700'
                  }`}
                >
                  {wantsIptv && <CheckCircleIcon className="absolute right-3 top-3 h-5 w-5 text-red-400" />}
                  <SignalIcon className="mb-2 h-6 w-6 text-red-400" />
                  <div className="font-semibold text-white">An IPTV subscription</div>
                  <p className="mt-1 text-xs text-gray-400">
                    Record games live off a playlist or Xtream login, with guide data. Also guided.
                  </p>
                </button>
              </div>
              {wantsDownload && wantsIptv && (
                <p className="mt-3 text-xs text-gray-500">
                  Both selected - the guide covers your download client and indexer first, then your
                  IPTV provider.
                </p>
              )}
            </div>
          )}

          {stepKey === 'root' && (
            <div>
              <FolderIcon className="mb-2 h-7 w-7 text-yellow-400" />
              <h2 className="mb-1 text-2xl font-bold text-white">Where should your library live?</h2>
              <p className="mb-6 text-sm text-gray-400">
                Imported and recorded games are organized into this folder. Point it at the same place
                your media server watches.
              </p>
              <label className={labelCls}>Library folder</label>
              <div className="flex gap-2">
                <input type="text" value={rootPath} onChange={(e) => setRootPath(e.target.value)} placeholder="/media/sports" className={inputCls} />
                <button
                  type="button"
                  onClick={() => setShowBrowser(true)}
                  className="flex-shrink-0 rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm text-gray-200 transition-colors hover:bg-gray-700"
                >
                  Browse
                </button>
              </div>
              <p className="mt-1 text-xs text-gray-500">The folder must exist and be writable by Sportarr.</p>

              {/* Live check of the typed path, so the user sees the folder is
                  really there (and what's inside) before moving on. */}
              {rootPath.trim() && (
                <div className={`mt-3 rounded-lg border p-3 text-xs ${
                  checkingPath || pathCheck === null
                    ? 'border-gray-800 bg-gray-900/40 text-gray-500'
                    : pathCheck.exists
                      ? 'border-green-900/50 bg-green-950/20 text-green-300'
                      : 'border-amber-900/50 bg-amber-950/20 text-amber-300'
                }`}>
                  {checkingPath || pathCheck === null ? (
                    <span className="flex items-center gap-2"><ArrowPathIcon className="h-3.5 w-3.5 animate-spin" /> Checking the path...</span>
                  ) : pathCheck.exists ? (
                    <>
                      <div className="font-medium">✓ Folder found</div>
                      {pathCheck.folders.length > 0 ? (
                        <div className="mt-2 flex flex-wrap gap-1.5">
                          {pathCheck.folders.slice(0, 12).map((f) => (
                            <span key={f} className="inline-flex items-center gap-1 rounded bg-gray-800/80 px-2 py-0.5 text-gray-300">
                              <FolderIcon className="h-3 w-3 text-yellow-400" />{f}
                            </span>
                          ))}
                          {pathCheck.folders.length > 12 && (
                            <span className="text-gray-500">+{pathCheck.folders.length - 12} more</span>
                          )}
                        </div>
                      ) : (
                        <p className="mt-1 text-green-400/80">It's empty - Sportarr will build the league folders here.</p>
                      )}
                    </>
                  ) : (
                    <div>
                      <span className="font-medium">This folder doesn't exist.</span> Sportarr only accepts
                      folders that already exist and are writable - create it on your server first, or use
                      Browse to pick one that's already there.
                    </div>
                  )}
                </div>
              )}

              <button
                type="button"
                onClick={() => setShowRemotePath(!showRemotePath)}
                className="mt-5 text-sm text-gray-400 transition-colors hover:text-white"
              >
                {showRemotePath ? '- Hide' : '+ '}Remote path mapping (advanced)
              </button>
              {showRemotePath && (
                <div className="mt-3 space-y-3 rounded-lg border border-gray-800 bg-gray-900/60 p-4">
                  <p className="text-xs text-gray-400">
                    Only needed if your download client runs on a different host or container and reports
                    a path Sportarr can't see. Map the client's path to the local one.
                  </p>
                  <div>
                    <label className={labelCls}>Download client host</label>
                    <input type="text" value={rpHost} onChange={(e) => setRpHost(e.target.value)} placeholder="192.168.1.50" className={inputCls} />
                  </div>
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <div>
                      <label className={labelCls}>Remote path (client sees)</label>
                      <input type="text" value={rpRemote} onChange={(e) => setRpRemote(e.target.value)} placeholder="/downloads" className={inputCls} />
                    </div>
                    <div>
                      <label className={labelCls}>Local path (Sportarr sees)</label>
                      <input type="text" value={rpLocal} onChange={(e) => setRpLocal(e.target.value)} placeholder="/media/downloads" className={inputCls} />
                    </div>
                  </div>
                </div>
              )}
            </div>
          )}

          {stepKey === 'quality' && (
            <div>
              <SparklesIcon className="mb-2 h-7 w-7 text-red-500" />
              <h2 className="mb-1 text-2xl font-bold text-white">Choose your release setup</h2>
              <p className="mb-4 text-sm text-gray-400">
                Both 1080p and 4K profiles are ready. Sportarr starts with 1080p.
                You can change it later in Settings.
              </p>
              <div className="mb-4 grid gap-3 sm:grid-cols-2">
                <button
                  type="button"
                  aria-pressed={releaseSetup === 'standard'}
                  onClick={() => { releaseSetupTouchedRef.current = true; setReleaseSetup('standard'); setReleaseSetupTouched(true); }}
                  className={releaseSetup === 'standard' ? OPTION_CARD_SELECTED : OPTION_CARD_UNSELECTED}
                >
                  <div className="font-semibold text-white">Standard setup</div>
                  <p className="mt-1 text-xs text-gray-400">Use built-in profiles. Fine-tune preferences later in Settings.</p>
                </button>
                <button
                  type="button"
                  aria-pressed={releaseSetup === 'recommended'}
                  onClick={() => { releaseSetupTouchedRef.current = true; setReleaseSetup('recommended'); setReleaseSetupTouched(true); }}
                  className={releaseSetup === 'recommended' ? OPTION_CARD_SELECTED : OPTION_CARD_UNSELECTED}
                >
                  <div className="font-semibold text-white">Recommended setup</div>
                  <p className="mt-1 text-xs text-gray-400">Import TRaSH Guides scores and quality size limits.</p>
                </button>
              </div>
              {hasLegacyReleasePreferences && !releaseSetupTouched && (
                <p className="mb-4 text-sm text-gray-400">
                  Your current release preferences will stay in place unless you choose a setup above.
                </p>
              )}
              {Object.keys(namingPresets).length > 0 && (
                <div className="mb-4">
                  <label htmlFor="onboarding-naming-preset" className={labelCls}>File naming (optional)</label>
                  <select id="onboarding-naming-preset" value={namingKey} onChange={(e) => setNamingChoice(e.target.value)} className={inputCls}>
                    <option value="">Keep current naming</option>
                    {orderNamingPresets(Object.keys(namingPresets), namingPresets).map((key, i) => (
                      <option key={key} value={key}>
                        {key === 'original' ? 'Original Filename (check source)' : key.replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase())}{i === 0 ? ' (recommended)' : ''}
                      </option>
                    ))}
                  </select>
                  {namingWarning && (
                    <div role="alert" className={`mt-3 ${NAMING_GUIDANCE}`}>
                      <ExclamationTriangleIcon className="mt-0.5 h-4 w-4 shrink-0 text-amber-400" aria-hidden="true" />
                      <p>{namingWarning}</p>
                    </div>
                  )}
                  <div role="note" className={`mt-3 ${NAMING_CONTEXT_PANEL}`}>
                    <InformationCircleIcon className="mt-0.5 h-4 w-4 shrink-0 text-gray-400" aria-hidden="true" />
                    <p>{NAMING_CONTEXT_TEXT}</p>
                  </div>
                  <p className="mt-1 text-xs text-gray-500">
                    {namingKey ? namingPresets[namingKey]?.description : 'Your current naming settings will stay in place.'}
                  </p>
                  {(namingKey ? namingPresets[namingKey]?.format : currentNamingFormat) && (
                    <div className="mt-2 rounded-lg border border-gray-800 bg-gray-900/60 p-3">
                      <p className="mb-1 text-[10px] font-medium uppercase tracking-wide text-gray-500">
                        {namingKey ? 'Files will be named like' : 'Current format'}
                      </p>
                      <p className="break-all font-mono text-xs text-gray-200">
                        {namingKey ? renderNamingExample(namingPresets[namingKey].format) : currentNamingFormat}
                      </p>
                    </div>
                  )}
                </div>
              )}

            </div>
          )}

          {stepKey === 'client' && (
            <div>
              <ServerIcon className="mb-2 h-7 w-7 text-blue-400" />
              <h2 className="mb-1 text-2xl font-bold text-white">Connect your download client</h2>
              <p className="mb-6 text-sm text-gray-400">
                This is where Sportarr sends grabs to be downloaded. Run more than one? Add them all
                with the button at the bottom.
              </p>
              {addedClients.length > 0 && (
                <div className="mb-4 space-y-1.5">
                  {addedClients.map((c) => (
                    <div
                      key={c.id}
                      className={`flex items-center gap-2 rounded-lg border px-3 py-2 text-sm ${
                        editingClientId === c.id
                          ? 'border-red-500 bg-red-950/20 text-red-200'
                          : 'border-green-900/50 bg-green-950/20 text-green-300'
                      }`}
                    >
                      <CheckCircleIcon className="h-4 w-4 flex-shrink-0" />
                      <span className="min-w-0 flex-1 truncate">{c.label}{editingClientId === c.id ? ' - editing below' : ''}</span>
                      <button
                        onClick={() => editClient(c)}
                        className="flex-shrink-0 rounded border border-gray-700 bg-gray-800 px-2 py-0.5 text-xs text-gray-300 transition-colors hover:bg-gray-700"
                      >
                        Edit
                      </button>
                    </div>
                  ))}
                </div>
              )}
              <div className="space-y-4">
                <div>
                  <label className={labelCls}>Client</label>
                  <select
                    value={dcType}
                    onChange={(e) => {
                      const t = parseInt(e.target.value, 10);
                      setDcFormTouched(true);
                      setDcType(t);
                      setDcPort(CLIENT_TYPES.find((c) => c.value === t)?.port ?? 8080);
                      setDcTest(null);
                    }}
                    className={inputCls}
                  >
                    {CLIENT_TYPES.map((c) => (
                      <option key={c.value} value={c.value}>{c.label} ({c.protocol})</option>
                    ))}
                  </select>
                </div>
                <div className="grid grid-cols-3 gap-4">
                  <div className="col-span-2">
                    <label className={labelCls}>Host</label>
                    <input type="text" value={dcHost} onChange={(e) => { setDcFormTouched(true); setDcHost(e.target.value); }} placeholder="localhost" className={inputCls} />
                  </div>
                  <div>
                    <label className={labelCls}>Port</label>
                    <input type="number" value={dcPort} onChange={(e) => { setDcFormTouched(true); setDcPort(parseInt(e.target.value, 10) || 0); }} className={inputCls} />
                  </div>
                </div>
                {clientAuth === 'apikey' ? (
                  <div>
                    <label className={labelCls}>API Key</label>
                    <input type="text" value={dcApiKey} onChange={(e) => { setDcFormTouched(true); setDcApiKey(e.target.value); }} className={inputCls} />
                  </div>
                ) : (
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div>
                      <label className={labelCls}>Username</label>
                      <input type="text" value={dcUser} onChange={(e) => { setDcFormTouched(true); setDcUser(e.target.value); }} className={inputCls} />
                    </div>
                    <div>
                      <label className={labelCls}>Password</label>
                      <input type="password" value={dcPass} onChange={(e) => { setDcFormTouched(true); setDcPass(e.target.value); }} className={inputCls} />
                    </div>
                  </div>
                )}
                <div className="flex flex-wrap items-center gap-3">
                  <button onClick={testClient} disabled={busy} className="rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm text-gray-200 transition-colors hover:bg-gray-700 disabled:opacity-50">
                    Test connection
                  </button>
                  <button onClick={saveClientAndAddAnother} disabled={busy} className="rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm text-gray-200 transition-colors hover:bg-gray-700 disabled:opacity-50">
                    {editingClientId != null ? 'Update client' : '+ Save & add another'}
                  </button>
                  {editingClientId != null && (
                    <button onClick={resetClientForm} disabled={busy} className="rounded-lg px-3 py-2 text-sm text-gray-400 transition-colors hover:text-white disabled:opacity-50">
                      Cancel edit
                    </button>
                  )}
                  {dcTest && (
                    <span className={`text-sm ${dcTest.ok ? 'text-green-400' : 'text-red-400'}`}>
                      {dcTest.ok ? '✓ ' : '✕ '}{dcTest.msg}
                    </span>
                  )}
                </div>
                {addedClients.length > 0 && editingClientId == null && (
                  <p className="text-xs text-gray-500">
                    Done adding clients? Leave the host empty and hit Save &amp; Next.
                  </p>
                )}
              </div>
            </div>
          )}

          {stepKey === 'indexer' && (
            <div>
              <MagnifyingGlassIcon className="mb-2 h-7 w-7 text-purple-400" />
              <h2 className="mb-1 text-2xl font-bold text-white">Add your indexers</h2>
              <p className="mb-4 text-sm text-gray-400">
                Where Sportarr searches for releases. There are two ways to do this.
              </p>

              {/* Prowlarr-first recommendation with the exact steps, since
                  Sportarr isn't in Prowlarr's app list yet and the Sonarr
                  option is the non-obvious trick. */}
              <div className="mb-5 rounded-lg border border-gray-800 bg-gray-900/60 p-4">
                <div className="mb-1 text-sm font-semibold text-white">
                  Using Prowlarr? <span className="rounded bg-green-600/20 px-1.5 py-0.5 text-[10px] font-medium text-green-400">recommended</span>
                </div>
                <p className="mb-2 text-xs text-gray-400">
                  Prowlarr manages all your indexers in one place and syncs them into Sportarr
                  automatically. Set it up there, then just Skip this step.
                </p>
                <ol className="space-y-1 text-xs text-gray-300">
                  <li>1. In Prowlarr: Settings → Apps → + → add <span className="font-semibold text-white">Sonarr</span> (Sportarr isn't listed in Prowlarr yet - the Sonarr option speaks Sportarr's compatible API).</li>
                  <li>
                    2. <span className="font-semibold text-white">Prowlarr Server</span>: an address Sportarr can
                    reach, like <code className="rounded bg-gray-800 px-1">http://192.168.x.x:9696</code>. Don't
                    leave it as localhost when Prowlarr runs in its own container or on another machine -
                    searches go through this address, and localhost points Sportarr back at itself, so every
                    search fails.
                  </li>
                  <li>3. <span className="font-semibold text-white">Sonarr Server</span>: this Sportarr's address, <code className="rounded bg-gray-800 px-1">{`${window.location.protocol}//${window.location.host}`}</code></li>
                  <li>4. API key:</li>
                </ol>
                {sportarrApiKey ? (
                  <div className="mt-1.5 flex items-center gap-2">
                    <code className="flex-1 overflow-hidden text-ellipsis whitespace-nowrap rounded bg-gray-800 px-2 py-1 text-xs text-gray-300">{sportarrApiKey}</code>
                    <button
                      type="button"
                      onClick={() => { navigator.clipboard.writeText(sportarrApiKey); toast.success('API key copied'); }}
                      className="flex-shrink-0 rounded border border-gray-700 bg-gray-800 px-2 py-1 text-xs text-gray-300 transition-colors hover:bg-gray-700"
                    >
                      Copy
                    </button>
                  </div>
                ) : (
                  <p className="mt-1 text-xs text-gray-400">Find it in Settings → General after setup.</p>
                )}
                <p className="mt-2 text-xs text-gray-300">
                  5. Under sync categories, tick <span className="font-semibold text-white">TV (5000)</span>, then
                  expand it and tick <span className="font-semibold text-white">TV/Sport (5060)</span> yourself -
                  Prowlarr leaves Sport (along with Anime and Documentary) unticked even when you select the whole
                  TV group. TV/HD (5040) and TV/UHD (5045) should be ticked too. Indexers then sync in and stay
                  updated.
                </p>
              </div>

              <div className="mb-2 text-sm font-semibold text-white">Or add an indexer directly</div>
              {addedIndexers.length > 0 && (
                <div className="mb-3 space-y-1.5">
                  {addedIndexers.map((x) => (
                    <div
                      key={x.id}
                      className={`flex items-center gap-2 rounded-lg border px-3 py-2 text-sm ${
                        editingIndexerId === x.id
                          ? 'border-red-500 bg-red-950/20 text-red-200'
                          : 'border-green-900/50 bg-green-950/20 text-green-300'
                      }`}
                    >
                      <CheckCircleIcon className="h-4 w-4 flex-shrink-0" />
                      <span className="min-w-0 flex-1 truncate">{x.label}{editingIndexerId === x.id ? ' - editing below' : ''}</span>
                      <button
                        onClick={() => editIndexer(x)}
                        className="flex-shrink-0 rounded border border-gray-700 bg-gray-800 px-2 py-0.5 text-xs text-gray-300 transition-colors hover:bg-gray-700"
                      >
                        Edit
                      </button>
                    </div>
                  ))}
                </div>
              )}
              <div className="space-y-4">
                <div>
                  <label className={labelCls}>Type</label>
                  <div className="flex gap-2">
                    {(['usenet', 'torrent'] as const).map((p) => (
                      <button
                        key={p}
                        onClick={() => { setIxProtocol(p); setIxTypeChanged(true); setIxTest(null); }}
                        className={`flex-1 rounded-lg border px-4 py-2 text-sm capitalize transition-colors ${
                          ixProtocol === p ? 'border-red-500 bg-red-950/20 text-white' : 'border-gray-700 bg-gray-800 text-gray-300 hover:bg-gray-700'
                        }`}
                      >
                        {p} ({p === 'usenet' ? 'Newznab' : 'Torznab'})
                      </button>
                    ))}
                  </div>
                </div>
                <div>
                  <label className={labelCls}>Name</label>
                  <input type="text" value={ixName} onChange={(e) => setIxName(e.target.value)} placeholder={ixProtocol === 'usenet' ? 'My Usenet Indexer' : 'My Torrent Indexer'} className={inputCls} />
                </div>
                <div>
                  <label className={labelCls}>URL</label>
                  <input type="text" value={ixUrl} onChange={(e) => setIxUrl(e.target.value)} placeholder="https://indexer.example.com" className={inputCls} />
                </div>
                <div>
                  <label htmlFor="onboarding-indexer-api-key" className={labelCls}>API Key</label>
                  <input id="onboarding-indexer-api-key" type="text" value={ixApiKey} onChange={(e) => setIxApiKey(e.target.value)} className={inputCls} />
                </div>
                <div className="flex flex-wrap items-center gap-3">
                  <button onClick={testIndexer} disabled={busy} className="rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm text-gray-200 transition-colors hover:bg-gray-700 disabled:opacity-50">
                    Test connection
                  </button>
                  <button onClick={saveIndexerAndAddAnother} disabled={busy} className="rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm text-gray-200 transition-colors hover:bg-gray-700 disabled:opacity-50">
                    {editingIndexerId != null ? 'Update indexer' : '+ Save & add another'}
                  </button>
                  {editingIndexerId != null && (
                    <button onClick={resetIndexerForm} disabled={busy} className="rounded-lg px-3 py-2 text-sm text-gray-400 transition-colors hover:text-white disabled:opacity-50">
                      Cancel edit
                    </button>
                  )}
                  {ixTest && (
                    <span className={`text-sm ${ixTest.ok ? 'text-green-400' : 'text-red-400'}`}>
                      {ixTest.ok ? '✓ ' : '✕ '}{ixTest.msg}
                    </span>
                  )}
                </div>
                <p className="text-xs text-gray-500">
                  Using Prowlarr, or done adding? Leave the URL empty and hit Save &amp; Next.
                </p>
              </div>
            </div>
          )}

          {stepKey === 'provider' && (
            <div>
              <SignalIcon className="mb-2 h-7 w-7 text-red-400" />
              <h2 className="mb-1 text-2xl font-bold text-white">Connect your provider</h2>
              <p className="mb-4 text-sm text-gray-400">
                Enter your subscription once. We'll load its channels and, with a guide URL, match the
                guide automatically.
              </p>
              {addedProviders.length > 0 && (
                <div className="mb-4 space-y-1.5">
                  {addedProviders.map((p) => (
                    <div
                      key={p.id}
                      className={`flex items-center gap-2 rounded-lg border px-3 py-2 text-sm ${
                        editingProviderId === p.id
                          ? 'border-red-500 bg-red-950/20 text-red-200'
                          : 'border-green-900/50 bg-green-950/20 text-green-300'
                      }`}
                    >
                      <CheckCircleIcon className="h-4 w-4 flex-shrink-0" />
                      <span className="min-w-0 flex-1 truncate">{p.label}{editingProviderId === p.id ? ' - editing below' : ' - connected'}</span>
                      <button
                        onClick={() => editProvider(p)}
                        className="flex-shrink-0 rounded border border-gray-700 bg-gray-800 px-2 py-0.5 text-xs text-gray-300 transition-colors hover:bg-gray-700"
                      >
                        Edit
                      </button>
                    </div>
                  ))}
                  {editingProviderId == null && (
                    <p className="text-xs text-gray-500">
                      Adding another is optional - leave the form empty and hit Save &amp; Next to keep what you have.
                    </p>
                  )}
                </div>
              )}
              <div className="space-y-4">
                <div>
                  <label className={labelCls}>Name</label>
                  <input type="text" value={pName} onChange={(e) => setPName(e.target.value)} placeholder="My IPTV Provider" className={inputCls} />
                </div>
                <div>
                  <label className={labelCls}>Type</label>
                  <select value={pType} onChange={(e) => setPType(e.target.value as 'M3U' | 'Xtream')} className={inputCls}>
                    <option value="M3U">M3U Playlist</option>
                    <option value="Xtream">Xtream Codes API</option>
                  </select>
                </div>
                <div>
                  <label className={labelCls}>{isXtream ? 'Server URL' : 'Playlist URL'}</label>
                  <input type="text" value={pUrl} onChange={(e) => setPUrl(e.target.value)} placeholder={isXtream ? 'http://server.example.com:8080' : 'http://example.com/playlist.m3u'} className={inputCls} />
                </div>
                {isXtream && (
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    <div>
                      <label className={labelCls}>Username</label>
                      <input type="text" value={pUser} onChange={(e) => setPUser(e.target.value)} className={inputCls} />
                    </div>
                    <div>
                      <label className={labelCls}>Password</label>
                      <input type="password" value={pPass} onChange={(e) => setPPass(e.target.value)} className={inputCls} />
                    </div>
                  </div>
                )}
                <div>
                  <label className={labelCls}>Program Guide (EPG) URL <span className="text-gray-500">(optional)</span></label>
                  <input type="text" value={pEpg} onChange={(e) => setPEpg(e.target.value)} placeholder="http://example.com/guide.xml" className={inputCls} />
                  <p className="mt-1 text-xs text-gray-500">Adds guide data so recordings start and end on time.</p>
                </div>
                <div className="flex flex-wrap items-center gap-3">
                  <button onClick={connectProviderAndAddAnother} disabled={busy} className="rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm text-gray-200 transition-colors hover:bg-gray-700 disabled:opacity-50">
                    {editingProviderId != null ? 'Update provider' : '+ Save & add another'}
                  </button>
                  {editingProviderId != null && (
                    <button onClick={resetProviderForm} disabled={busy} className="rounded-lg px-3 py-2 text-sm text-gray-400 transition-colors hover:text-white disabled:opacity-50">
                      Cancel edit
                    </button>
                  )}
                </div>
              </div>
            </div>
          )}

          {stepKey === 'playback' && (
            <div>
              <h2 className="mb-1 text-2xl font-bold text-white">Set up your media server</h2>
              <p className="mb-3 text-sm text-gray-400">
                Sportarr's metadata comes entirely from{' '}
                <a href="https://www.thesportsdb.com" target="_blank" rel="noopener noreferrer" className="text-red-400 underline hover:text-red-300">TheSportsDB</a>,
                whose team has been wonderfully generous with their data and support - please consider
                supporting them. To make your library match, point your player at Sportarr's own agent.
              </p>
              <p className="mb-4 text-sm text-gray-300">
                It's important to use <span className="font-semibold text-white">Sportarr's metadata agent</span> (served from sportarr.net).
                It's what keeps every event file matched to the right game.
              </p>

              {/* App picker - selected app glows in its brand colour */}
              <div className="mb-5 grid grid-cols-3 gap-3">
                {PLAYBACK_APPS.map((app) => {
                  const selected = playbackApp === app.key;
                  return (
                    <button
                      key={app.key}
                      onClick={() => setPlaybackApp(app.key)}
                      className="rounded-xl border bg-gray-900 px-3 py-4 text-center font-semibold transition-all"
                      style={
                        selected
                          ? { borderColor: app.accent, color: app.accent, boxShadow: `0 0 22px ${app.accent}66` }
                          : { borderColor: '#1f2937', color: '#e5e7eb' }
                      }
                    >
                      {app.label}
                    </button>
                  );
                })}
              </div>

              {/* Steps for the selected app - kept in lockstep with
                  Settings > General > Media Server Agents and each agent's
                  README. */}
              <ol className="mb-4 space-y-2">
                {APP_STEPS[playbackApp].map((step, i) => (
                  <li key={i} className="flex gap-3 text-sm text-gray-300">
                    <span className="flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-full bg-gray-800 text-xs font-bold text-gray-300">{i + 1}</span>
                    <span>{step}</span>
                  </li>
                ))}
              </ol>

              {/* The URL each app needs, with a copy button so nothing is retyped. */}
              {playbackApp === 'plex' && (
                <div className="mb-4">
                  <p className="mb-1 text-xs font-medium text-gray-400">Provider URL (Plex 1.43.0+, no files to install)</p>
                  <div className="flex items-center gap-2">
                    <code className="flex-1 overflow-hidden text-ellipsis whitespace-nowrap rounded bg-gray-800 px-2 py-1.5 text-xs text-gray-300">{PLEX_PROVIDER_URL}</code>
                    <button
                      type="button"
                      onClick={() => { navigator.clipboard.writeText(PLEX_PROVIDER_URL); toast.success('Provider URL copied'); }}
                      className="flex-shrink-0 rounded border border-gray-700 bg-gray-800 px-3 py-1.5 text-xs text-gray-300 transition-colors hover:bg-gray-700"
                    >
                      Copy
                    </button>
                  </div>
                  <details className="mt-3 text-xs">
                    <summary className="cursor-pointer text-gray-500 hover:text-gray-400">Older Plex (before 1.43.0)? Use the legacy agent</summary>
                    <p className="mt-1 text-gray-400">
                      Download the legacy bundle from the{' '}
                      <a href={RELEASES_URL} target="_blank" rel="noopener noreferrer" className="text-red-400 underline hover:text-red-300">releases page</a>{' '}
                      (opens in a new tab), unzip it into Plex's "Plug-ins" folder, and restart Plex.
                    </p>
                  </details>
                </div>
              )}
              {playbackApp === 'jellyfin' && (
                <div className="mb-4">
                  <p className="mb-1 text-xs font-medium text-gray-400">Plugin repository URL</p>
                  <div className="flex items-center gap-2">
                    <code className="flex-1 overflow-hidden text-ellipsis whitespace-nowrap rounded bg-gray-800 px-2 py-1.5 text-xs text-gray-300" title={JELLYFIN_REPO_URL}>{JELLYFIN_REPO_URL}</code>
                    <button
                      type="button"
                      onClick={() => { navigator.clipboard.writeText(JELLYFIN_REPO_URL); toast.success('Repository URL copied'); }}
                      className="flex-shrink-0 rounded border border-gray-700 bg-gray-800 px-3 py-1.5 text-xs text-gray-300 transition-colors hover:bg-gray-700"
                    >
                      Copy
                    </button>
                  </div>
                </div>
              )}
              {playbackApp === 'emby' && (
                <a
                  href={RELEASES_URL}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="mb-4 inline-flex items-center gap-2 rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm font-medium text-gray-200 transition-colors hover:bg-gray-700"
                >
                  <ArrowDownTrayIcon className="h-4 w-4" />
                  Open the releases page for the Emby plugin (new tab)
                </a>
              )}

              {/* Hard warnings */}
              <div className="space-y-3">
                <div className="flex items-start gap-3 rounded-lg border border-red-900/60 bg-red-950/30 p-4">
                  <ExclamationTriangleIcon className="mt-0.5 h-5 w-5 flex-shrink-0 text-red-300" />
                  <p className="text-sm text-red-200/90">
                    <span className="font-semibold text-red-200">Always create a brand-new library for your sports</span>,
                    with Sportarr as its only agent from the moment it's created. Never point an existing library at
                    your sports folder - one that has ever used another agent (TheTVDB, TMDb, etc.) keeps its old
                    matching behavior{playbackApp === 'plex' ? ' (a Plex limitation)' : ''} and your events will never
                    fill in correctly. If you already made one, delete that library and create a fresh one with only
                    Sportarr selected.
                  </p>
                </div>
                <div className="flex items-start gap-3 rounded-lg border border-amber-900/50 bg-amber-950/30 p-4">
                  <ExclamationTriangleIcon className="mt-0.5 h-5 w-5 flex-shrink-0 text-amber-300" />
                  <p className="text-sm text-amber-200/90">
                    <span className="font-semibold text-amber-200">Do not enable any other metadata source</span> on your
                    sports library (TheTVDB, TMDb, local NFO, etc.). They fight Sportarr and tag your event files with the
                    wrong data. Sportarr's agent should be the only one.
                  </p>
                </div>
              </div>
            </div>
          )}

          {stepKey === 'finish' && (
            <div className="text-center">
              <CheckCircleIcon className="animate-wizard-pop mx-auto mb-3 h-14 w-14 text-green-500" />
              <h2 className="mb-1 text-2xl font-bold text-white">You're set up</h2>
              <p className="mb-6 text-sm text-gray-400">
                {wantsDownload && wantsIptv
                  ? 'Your downloader and IPTV provider are connected. '
                  : wantsIptv
                    ? `Your provider is connected${channelCount !== null ? ` with ${channelCount} channels` : ''}${pEpg.trim() ? ' and guide data attached' : ''}. `
                    : 'Your downloader is connected. '}
                Last step: choose the sports you follow, and Sportarr takes it from there automatically.
              </p>
              <button
                onClick={() => { finish(); navigate('/add-league/search'); }}
                className="rounded-lg bg-red-600 px-6 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-red-700"
              >
                Pick my sports
              </button>
            </div>
          )}
          </div>
        </div>

        {/* Footer nav. The intro gets its own choice - the only deliberate way to
            opt out of the whole thing - and there is no close button anywhere, so a
            user either skips everything up front or passes through every step. */}
        {stepKey === 'intro' && (
          <div className="flex items-center justify-between border-t border-gray-800 px-6 py-4">
            <button
              onClick={dismiss}
              className="text-sm text-gray-500 transition-colors hover:text-gray-300"
            >
              Skip all steps
            </button>
            <button
              onClick={goNext}
              className="rounded-lg bg-red-600 px-6 py-2 text-sm font-semibold text-white transition-colors hover:bg-red-700"
            >
              Get started
            </button>
          </div>
        )}
        {stepKey !== 'finish' && stepKey !== 'intro' && (
          <div className="flex items-center justify-between border-t border-gray-800 px-6 py-4">
            <button
              onClick={goBack}
              disabled={busy || stepIndex === 0}
              className="text-sm text-gray-400 transition-colors hover:text-white disabled:opacity-30"
            >
              Back
            </button>
            <div className="flex items-center gap-2">
              <button
                onClick={skipStep}
                disabled={busy}
                className="rounded-lg px-4 py-2 text-sm text-gray-400 transition-colors hover:bg-gray-800 hover:text-white disabled:opacity-50"
              >
                Skip Step
              </button>
              <button
                onClick={goNext}
                disabled={busy || (stepKey === 'quality' && (!namingContextLoaded || !namingPresetsLoaded))}
                className="flex items-center gap-2 rounded-lg bg-red-600 px-5 py-2 text-sm font-semibold text-white transition-colors hover:bg-red-700 disabled:opacity-50"
              >
                {busy && <ArrowPathIcon className="h-4 w-4 animate-spin" />}
                {busy ? (progress ?? 'Working...') : stepKey === 'playback' ? 'Next' : 'Save & Next'}
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Folder browser for the library step - the same component the
          settings pages use, so browsing behaves identically everywhere. */}
      <FileBrowserModal
        isOpen={showBrowser}
        onClose={() => setShowBrowser(false)}
        onSelect={(p) => { setRootPath(p); setShowBrowser(false); }}
        title="Choose your library folder"
        initialPath={pathCheck?.exists ? rootPath.trim() : undefined}
      />
    </div>
  );
}
