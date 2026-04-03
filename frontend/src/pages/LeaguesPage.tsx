import { useState, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { MagnifyingGlassIcon, CheckIcon, TrashIcon, ArrowPathIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../api/client';
import type { League } from '../types';
import { LeagueProgressLine } from '../components/LeagueProgressBar';
import SortableFilterableHeader from '../components/SortableFilterableHeader';
import ColumnPicker from '../components/ColumnPicker';
import CompactTableFrame from '../components/CompactTableFrame';
import PageHeader from '../components/PageHeader';
import PageShell from '../components/PageShell';
import { useCompactView } from '../hooks/useCompactView';
import { useTableSortFilter, applyTableSortFilter } from '../hooks/useTableSortFilter';
import { useColumnVisibility } from '../hooks/useColumnVisibility';

const SPORT_ICONS: Record<string, string> = {
  'American Football': '🏈',
  Athletics: '🏃',
  'Australian Football': '🏉',
  Badminton: '🏸',
  Baseball: '⚾',
  Basketball: '🏀',
  Climbing: '🧗',
  Cricket: '🏏',
  Cycling: '🚴',
  Darts: '🎯',
  Esports: '🎮',
  Equestrian: '🏇',
  'Extreme Sports': '🪂',
  'Field Hockey': '🏑',
  Fighting: '🥊',
  Gaelic: '🏐',
  Gambling: '🎰',
  Golf: '⛳',
  Gymnastics: '🤸',
  Handball: '🤾',
  'Ice Hockey': '🏒',
  Lacrosse: '🥍',
  Motorsport: '🏎️',
  'Multi Sports': '🏅',
  Netball: '🏀',
  Rugby: '🏉',
  Shooting: '🎯',
  Skating: '⛸️',
  Skiing: '⛷️',
  Snooker: '🎱',
  Soccer: '⚽',
  'Table Tennis': '🏓',
  Tennis: '🎾',
  Volleyball: '🏐',
  Watersports: '🏄',
  Weightlifting: '🏋️',
  Wintersports: '🎿',
};

const PAGE_PADDING = 'p-4 md:p-8';
const TABLE_ROW_HOVER = 'text-sm transition-colors hover:bg-gray-800/50';
const SCROLLABLE_LIST = 'max-h-60 overflow-y-auto';
const BADGE_GRAY = 'whitespace-nowrap rounded bg-gray-800 px-2 py-0.5 text-xs text-gray-300';

type LeaguesColumnKey =
  | 'sport'
  | 'logo'
  | 'name'
  | 'monitored'
  | 'eventCount'
  | 'downloaded'
  | 'missing'
  | 'progress'
  | 'quality';

const getSportIcon = (sport: string): string => SPORT_ICONS[sport] || '🌐';

export default function LeaguesPage() {
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedSport, setSelectedSport] = useState('all');
  const [selectedLeagueIds, setSelectedLeagueIds] = useState<Set<number>>(new Set());
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  const [deleteLeagueFolder, setDeleteLeagueFolder] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [showRenameDialog, setShowRenameDialog] = useState(false);
  const [isRenaming, setIsRenaming] = useState(false);
  const [renamePreview, setRenamePreview] = useState<Array<{leagueId: number; leagueName: string; existingPath: string; newPath: string; existingFileName?: string; newFileName?: string; folderChanged?: boolean; changes: Array<{field: string; oldValue: string; newValue: string}>}>>([]);
  const [isLoadingPreview, setIsLoadingPreview] = useState(false);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const compactView = useCompactView();
  const {
    sortCol,
    sortDir,
    colFilters,
    activeFilterCol,
    handleColSort,
    onFilterChange,
    onFilterToggle,
  } = useTableSortFilter('name');
  const { isVisible, toggleCol } = useColumnVisibility<LeaguesColumnKey>(
    'leagues-col-visibility',
    {
      sport: true,
      logo: true,
      name: true,
      monitored: true,
      eventCount: true,
      downloaded: true,
      missing: true,
      progress: true,
      quality: true,
    },
    ['name']
  );

  const { data: leagues, isLoading, error, refetch } = useQuery({
    queryKey: ['leagues'],
    queryFn: async () => {
      const response = await apiClient.get<League[]>('/leagues');
      return response.data;
    },
    staleTime: 2 * 60 * 1000, // 2 minutes - library data changes less frequently
    refetchOnWindowFocus: false, // Don't refetch on tab focus
  });

  const filteredLeagues = (leagues?.filter(league => {
    const name = league.name ?? '';
    if (name.startsWith('_') || name.endsWith('_')) return false;
    const matchesSport = selectedSport === 'all' || league.sport === selectedSport;
    const matchesSearch = name.toLowerCase().includes(searchQuery.toLowerCase());
    return matchesSport && matchesSearch;
  }) || []).sort((a, b) => (a.name ?? '').localeCompare(b.name ?? ''));

  const leaguesBySport = leagues?.reduce((acc, league) => {
    const name = league.name ?? '';
    if (league.sport && !name.startsWith('_') && !name.endsWith('_')) {
      acc[league.sport] = (acc[league.sport] || 0) + 1;
    }
    return acc;
  }, {} as Record<string, number>) || {};

  const sportFilters = useMemo(() => {
    const filters = [{ id: 'all', name: 'All Sports', icon: '🌍' }];
    const uniqueSports = Array.from(
      new Set(
        (leagues || [])
          .filter((league) => {
            const name = league.name ?? '';
            return !name.startsWith('_') && !name.endsWith('_');
          })
          .map((league) => league.sport)
          .filter(Boolean)
      )
    );

    uniqueSports.forEach(sport => {
      filters.push({
        id: sport,
        name: sport,
        icon: getSportIcon(sport),
      });
    });

    return filters;
  }, [leagues]);

  const toggleLeagueSelection = (leagueId: number, e: React.MouseEvent) => {
    e.stopPropagation();
    setSelectedLeagueIds(prev => {
      const next = new Set(prev);
      if (next.has(leagueId)) {
        next.delete(leagueId);
      } else {
        next.add(leagueId);
      }
      return next;
    });
  };

  const selectAllFiltered = () => {
    setSelectedLeagueIds(new Set(filteredLeagues.map(l => l.id)));
  };

  const clearSelection = () => {
    setSelectedLeagueIds(new Set());
  };

  const handleDeleteSelected = async () => {
    if (selectedLeagueIds.size === 0) return;

    setIsDeleting(true);
    try {
      await Promise.all(
        Array.from(selectedLeagueIds).map(leagueId =>
          apiClient.delete(`/leagues/${leagueId}`, {
            params: { deleteFiles: deleteLeagueFolder }
          })
        )
      );
      setShowDeleteDialog(false);
      setDeleteLeagueFolder(false);
      setSelectedLeagueIds(new Set());
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
    } catch (error) {
      console.error('Failed to delete leagues:', error);
    } finally {
      setIsDeleting(false);
    }
  };

  const handleOpenRenameDialog = async () => {
    if (selectedLeagueIds.size === 0) return;

    setShowRenameDialog(true);
    setIsLoadingPreview(true);
    setRenamePreview([]);

    try {
      const response = await apiClient.post('/leagues/rename-preview', {
        leagueIds: Array.from(selectedLeagueIds)
      });
      setRenamePreview(response.data || []);
    } catch (error) {
      console.error('Failed to load rename preview:', error);
      toast.error('Failed to load rename preview');
    } finally {
      setIsLoadingPreview(false);
    }
  };

  const handleRenameSelected = async () => {
    if (selectedLeagueIds.size === 0) return;

    setIsRenaming(true);
    try {
      const response = await apiClient.post('/leagues/rename', {
        leagueIds: Array.from(selectedLeagueIds)
      });
      const { totalRenamed } = response.data;

      toast.success('Files Renamed Successfully', {
        description: `${totalRenamed} file(s) have been renamed according to your naming scheme.`,
      });
      setShowRenameDialog(false);
      setSelectedLeagueIds(new Set());
      queryClient.invalidateQueries({ queryKey: ['leagues'] });
    } catch (error) {
      console.error('Failed to rename files:', error);
      toast.error('Failed to rename files');
    } finally {
      setIsRenaming(false);
    }
  };

  const selectedLeagues = useMemo(() => {
    return leagues?.filter(l => selectedLeagueIds.has(l.id)) || [];
  }, [leagues, selectedLeagueIds]);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-center">
          <p className="text-red-500 text-xl mb-4">Failed to load leagues</p>
          <button
            onClick={() => refetch()}
            className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700"
          >
            Retry
          </button>
        </div>
      </div>
    );
  }

  const renderCompactTable = () => {
    if (filteredLeagues.length === 0) {
      return (
        <div className="text-center py-12">
          <p className="text-gray-400 text-lg">
            {searchQuery ? 'No leagues found' : selectedSport === 'all' ? 'No leagues yet' : `No ${selectedSport} leagues`}
          </p>
          <p className="text-gray-500 text-sm mt-2">
            Click "Add League" to start tracking sports competitions
          </p>
        </div>
      );
    }

    const tableLeagues = applyTableSortFilter(
      filteredLeagues,
      colFilters,
      sortCol,
      sortDir,
      (col, item) => {
        switch (col) {
          case 'sport':
            return String(item.sport || '');
          case 'name':
            return String(item.name || '');
          case 'eventCount':
            return String(item.eventCount || 0);
          case 'downloadedMonitoredCount':
            return String(item.downloadedMonitoredCount || 0);
          case 'missingCount':
            return String((item.eventCount || 0) - (item.downloadedMonitoredCount || 0));
          default:
            return '';
        }
      }
    );

    const colDefs: Array<{
      key: LeaguesColumnKey;
      label: string;
      alwaysVisible?: boolean;
    }> = [
      { key: 'sport', label: 'Sport' },
      { key: 'logo', label: 'Logo' },
      { key: 'name', label: 'League', alwaysVisible: true },
      { key: 'monitored', label: 'Monitored' },
      { key: 'eventCount', label: 'Total Events' },
      { key: 'downloaded', label: 'Downloaded' },
      { key: 'missing', label: 'Missing' },
      { key: 'progress', label: 'Progress' },
      { key: 'quality', label: 'Quality' },
    ];

    return (
      <CompactTableFrame
        controls={
          <ColumnPicker
            columns={colDefs}
            isVisible={isVisible as (col: string) => boolean}
            onToggle={toggleCol as (col: string) => void}
          />
        }
      >
        <thead>
          <tr className="sticky top-0 border-b border-gray-700 bg-gray-950 text-left text-xs uppercase text-gray-400">
            <th className="w-10 px-3 py-1.5 text-center">
              <input
                type="checkbox"
                checked={filteredLeagues.length > 0 && selectedLeagueIds.size === filteredLeagues.length}
                onChange={() => {
                  if (selectedLeagueIds.size === filteredLeagues.length) {
                    clearSelection();
                  } else {
                    selectAllFiltered();
                  }
                }}
                className="h-4 w-4 cursor-pointer rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-500"
                title="Select all leagues"
              />
            </th>
            {isVisible('sport') && (
              <SortableFilterableHeader
                col="sport"
                label="Sport"
                sortCol={sortCol}
                sortDir={sortDir}
                onSort={handleColSort}
                colFilters={colFilters}
                activeFilterCol={activeFilterCol}
                onFilterChange={onFilterChange}
                onFilterToggle={onFilterToggle}
                className="px-3 py-1.5"
              />
            )}
            {isVisible('logo') && <th className="px-3 py-1.5">Logo</th>}
            <SortableFilterableHeader
              col="name"
              label="League"
              sortCol={sortCol}
              sortDir={sortDir}
              onSort={handleColSort}
              colFilters={colFilters}
              activeFilterCol={activeFilterCol}
              onFilterChange={onFilterChange}
              onFilterToggle={onFilterToggle}
              className="px-3 py-1.5"
            />
            {isVisible('monitored') && <th className="px-3 py-1.5 text-center">Monitored</th>}
            {isVisible('eventCount') && (
              <SortableFilterableHeader
                col="eventCount"
                label="Total Events"
                sortCol={sortCol}
                sortDir={sortDir}
                onSort={handleColSort}
                colFilters={colFilters}
                activeFilterCol={activeFilterCol}
                onFilterChange={onFilterChange}
                onFilterToggle={onFilterToggle}
                className="px-3 py-1.5 text-center"
                centered
              />
            )}
            {isVisible('downloaded') && (
              <SortableFilterableHeader
                col="downloadedMonitoredCount"
                label="Downloaded"
                sortCol={sortCol}
                sortDir={sortDir}
                onSort={handleColSort}
                colFilters={colFilters}
                activeFilterCol={activeFilterCol}
                onFilterChange={onFilterChange}
                onFilterToggle={onFilterToggle}
                className="px-3 py-1.5 text-center"
                centered
              />
            )}
            {isVisible('missing') && (
              <SortableFilterableHeader
                col="missingCount"
                label="Missing"
                sortCol={sortCol}
                sortDir={sortDir}
                onSort={handleColSort}
                colFilters={colFilters}
                activeFilterCol={activeFilterCol}
                onFilterChange={onFilterChange}
                onFilterToggle={onFilterToggle}
                className="px-3 py-1.5 text-center"
                centered
              />
            )}
            {isVisible('progress') && <th className="px-3 py-1.5">Progress</th>}
            {isVisible('quality') && <th className="px-3 py-1.5">Quality</th>}
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-700">
          {tableLeagues.map((league) => {
            const isSelected = selectedLeagueIds.has(league.id);
            const missingCount = (league.eventCount || 0) - (league.downloadedMonitoredCount || 0);

            return (
              <tr
                key={league.id}
                onClick={() => {
                  if (!isSelected) {
                    navigate(`/leagues/${league.id}`);
                  }
                }}
                className={`${TABLE_ROW_HOVER} cursor-pointer ${isSelected ? 'bg-red-900/20' : ''}`}
              >
                <td className="px-3 py-1.5 text-center" onClick={(e) => e.stopPropagation()}>
                  <input
                    type="checkbox"
                    checked={isSelected}
                    onChange={(e) => {
                      e.stopPropagation();
                      toggleLeagueSelection(league.id, e as unknown as React.MouseEvent);
                    }}
                    className="h-4 w-4 cursor-pointer rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-500"
                  />
                </td>
                {isVisible('sport') && (
                  <td className="px-3 py-1.5 text-gray-400">
                    <span className="text-lg">{getSportIcon(league.sport || '')}</span>
                  </td>
                )}
                {isVisible('logo') && (
                  <td className="px-3 py-3">
                    <div className="flex h-12 w-12 flex-shrink-0 items-center justify-center rounded bg-black/50">
                      {league.logoUrl ? (
                        <img src={league.logoUrl} alt={league.name} className="max-h-full max-w-full object-contain" />
                      ) : (
                        <span className="text-xl">{getSportIcon(league.sport || '')}</span>
                      )}
                    </div>
                  </td>
                )}
                <td className="px-3 py-1.5 font-medium text-white">{league.name}</td>
                {isVisible('monitored') && (
                  <td className="px-3 py-1.5 text-center">
                    {league.monitored ? <span className="text-lg text-green-400">●</span> : <span className="text-lg text-gray-600">○</span>}
                  </td>
                )}
                {isVisible('eventCount') && (
                  <td className="px-2 py-1.5 text-center text-gray-300">{league.eventCount || 0}</td>
                )}
                {isVisible('downloaded') && (
                  <td className="px-2 py-1.5 text-center">
                    <span className="text-green-400">{league.downloadedMonitoredCount || 0}</span>
                  </td>
                )}
                {isVisible('missing') && (
                  <td className="px-2 py-1.5 text-center">
                    {missingCount > 0 ? <span className="text-red-400">{missingCount}</span> : <span className="text-gray-600">-</span>}
                  </td>
                )}
                {isVisible('progress') && (
                  <td className="px-3 py-1.5">
                    {league.progressPercent !== undefined && (
                      <LeagueProgressLine
                        progressPercent={league.progressPercent}
                        progressStatus={league.progressStatus || 'unmonitored'}
                      />
                    )}
                  </td>
                )}
                {isVisible('quality') && (
                  <td className="px-2 py-1.5">
                    {league.qualityProfileId ? <span className={BADGE_GRAY}>#{league.qualityProfileId}</span> : <span className="text-gray-600">-</span>}
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </CompactTableFrame>
    );
  };

  return (
    <PageShell>
      <PageHeader
        title="Leagues"
        subtitle="Manage your monitored leagues and competitions"
        actions={
          <button
            onClick={() => navigate('/add-league/search')}
            className="rounded-lg bg-red-600 px-3 py-2 text-sm font-semibold text-white transition-colors hover:bg-red-700 md:px-6 md:py-3 md:text-base"
          >
            <span className="sm:hidden">+ Add</span>
            <span className="hidden sm:inline">+ Add League</span>
          </button>
        }
      />

      {/* Sport Filter Tabs - Only show if user has leagues */}
      {sportFilters.length > 1 && (
        <div className="mb-4 md:mb-8">
          <div className="flex gap-2 overflow-x-auto pb-2 scrollbar-hide">
            {sportFilters.map(sport => (
              <button
                key={sport.id}
                onClick={() => setSelectedSport(sport.id)}
                className={`
                  flex items-center gap-1.5 md:gap-2 px-3 md:px-4 py-1.5 md:py-2 rounded-lg whitespace-nowrap font-medium transition-all text-sm md:text-base
                  ${selectedSport === sport.id
                    ? 'bg-red-600 text-white'
                    : 'bg-gray-900 text-gray-400 hover:bg-gray-800 hover:text-white border border-red-900/30'
                  }
                `}
              >
                <span className="text-lg md:text-xl">{sport.icon}</span>
                <span className="hidden sm:inline">{sport.name}</span>
                {sport.id !== 'all' && leaguesBySport[sport.id] && (
                  <span className="ml-1 rounded bg-black/30 px-1.5 py-0.5 text-xs md:px-2">
                    {leaguesBySport[sport.id]}
                  </span>
                )}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Search Bar */}
      <div className="mb-4 max-w-2xl md:mb-8">
        <div className="relative">
          <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 md:pl-4">
            <MagnifyingGlassIcon className="h-4 w-4 text-gray-400 md:h-5 md:w-5" />
          </div>
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search leagues..."
            className="w-full rounded-lg border border-red-900/30 bg-gray-900 py-2 pl-10 pr-4 text-sm text-white placeholder-gray-500 transition-all focus:border-red-600 focus:outline-none focus:ring-2 focus:ring-red-600/20 md:py-3 md:pl-12 md:text-base"
          />
        </div>
      </div>

      {/* Stats */}
      <div className="mb-4 grid grid-cols-2 gap-2 sm:grid-cols-3 md:mb-8 md:grid-cols-5 md:gap-4">
        <div className="rounded-lg border border-red-900/30 bg-gray-900 p-3 md:p-4">
          <p className="mb-1 text-xs text-gray-400 md:text-sm">Total Leagues</p>
          <p className="text-xl font-bold text-white md:text-3xl">{leagues?.length || 0}</p>
        </div>
        <div className="rounded-lg border border-red-900/30 bg-gray-900 p-3 md:p-4">
          <p className="mb-1 text-xs text-gray-400 md:text-sm">Monitored</p>
          <p className="text-xl font-bold text-white md:text-3xl">
            {leagues?.filter(l => l.monitored).length || 0}
          </p>
        </div>
        <div className="rounded-lg border border-red-900/30 bg-gray-900 p-3 md:p-4">
          <p className="mb-1 text-xs text-gray-400 md:text-sm">Total Events</p>
          <p className="text-xl font-bold text-white md:text-3xl">
            {leagues?.reduce((sum, league) => sum + (league.eventCount || 0), 0) || 0}
          </p>
        </div>
        <div className="rounded-lg border border-red-900/30 bg-gray-900 p-3 md:p-4">
          <p className="mb-1 text-xs text-gray-400 md:text-sm">Monitored Events</p>
          <p className="text-xl font-bold text-white md:text-3xl">
            {leagues?.reduce((sum, league) => sum + (league.monitoredEventCount || 0), 0) || 0}
          </p>
        </div>
        <div className="col-span-2 rounded-lg border border-red-900/30 bg-gray-900 p-3 sm:col-span-1 md:p-4">
          <p className="mb-1 text-xs text-gray-400 md:text-sm">Downloaded</p>
          <p className="text-xl font-bold text-white md:text-3xl">
            {leagues?.reduce((sum, league) => sum + (league.fileCount || 0), 0) || 0}
          </p>
        </div>
      </div>

      {compactView ? (
        renderCompactTable()
      ) : filteredLeagues.length === 0 ? (
        <div className="py-12 text-center">
          <p className="text-lg text-gray-400">
            {searchQuery ? 'No leagues found' : selectedSport === 'all' ? 'No leagues yet' : `No ${selectedSport} leagues`}
          </p>
          <p className="mt-2 text-sm text-gray-500">
            Click "Add League" to start tracking sports competitions
          </p>
        </div>
      ) : (
        <div className={`grid grid-cols-1 gap-3 sm:grid-cols-2 md:gap-6 lg:grid-cols-3 xl:grid-cols-4 ${selectedLeagueIds.size > 0 ? 'pb-24' : ''}`}>
          {filteredLeagues.map((league) => {
            const isSelected = selectedLeagueIds.has(league.id);
            return (
              <div
                key={league.id}
                onClick={() => navigate(`/leagues/${league.id}`)}
                className={`group cursor-pointer overflow-hidden rounded-lg border bg-gray-900 transition-all hover:shadow-lg ${
                  isSelected
                    ? 'border-red-500 ring-2 ring-red-500/50 shadow-red-900/30'
                    : 'border-red-900/30 hover:border-red-600/50 hover:shadow-red-900/20'
                }`}
              >
                <div className="relative aspect-[16/9] overflow-hidden bg-gray-800">
                  {league.logoUrl || league.bannerUrl || league.posterUrl ? (
                    <img
                      src={league.logoUrl || league.bannerUrl || league.posterUrl}
                      alt={league.name}
                      className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-105"
                    />
                  ) : (
                    <div className="flex h-full w-full items-center justify-center">
                      <span className="text-6xl font-bold text-gray-700">
                        {league.name.charAt(0)}
                      </span>
                    </div>
                  )}

                  <div
                    className="absolute left-2 top-2 z-10"
                    onClick={(e) => toggleLeagueSelection(league.id, e)}
                  >
                    <div className={`flex h-6 w-6 items-center justify-center rounded border-2 transition-colors ${
                      isSelected
                        ? 'border-red-600 bg-red-600'
                        : 'border-white/50 bg-black/50 hover:border-white'
                    }`}>
                      {isSelected && (
                        <CheckIcon className="h-4 w-4 text-white" />
                      )}
                    </div>
                  </div>

                  <div className="absolute left-10 top-2">
                    <span className="rounded bg-black/70 px-2 py-1 text-xs font-semibold text-white backdrop-blur-sm">
                      {getSportIcon(league.sport || '')} {league.sport}
                    </span>
                  </div>

                  <div className="absolute right-2 top-2 flex flex-col items-end gap-2">
                    {league.monitored ? (
                      <span className="rounded bg-green-600/90 px-2 py-1 text-xs font-semibold text-white backdrop-blur-sm">
                        Monitored
                      </span>
                    ) : (
                      <span className="rounded bg-gray-600/90 px-2 py-1 text-xs font-semibold text-white backdrop-blur-sm">
                        Not Monitored
                      </span>
                    )}
                  </div>

                  <div className="absolute bottom-3 left-2">
                    <span className="rounded bg-black/70 px-3 py-1 text-sm font-semibold text-white backdrop-blur-sm">
                      {league.eventCount || 0} {(league.eventCount || 0) === 1 ? 'Event' : 'Events'}
                    </span>
                  </div>

                  <LeagueProgressLine
                    progressPercent={league.progressPercent || 0}
                    progressStatus={league.progressStatus || 'unmonitored'}
                  />
                </div>

                <div className="p-4">
                  <h3 className="mb-2 truncate text-lg font-bold text-white">{league.name}</h3>

                  {league.country && (
                    <p className="mb-3 text-sm text-gray-400">{league.country}</p>
                  )}

                  <div className="mb-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
                    <div className="flex items-center gap-1 whitespace-nowrap">
                      <span className="h-2 w-2 flex-shrink-0 rounded-full bg-blue-500"></span>
                      <span className="text-gray-400">Monitored:</span>
                      <span className="font-semibold text-white">{league.monitoredEventCount || 0}</span>
                    </div>
                    <div className="flex items-center gap-1 whitespace-nowrap">
                      <span className="h-2 w-2 flex-shrink-0 rounded-full bg-green-500"></span>
                      <span className="text-gray-400">Have:</span>
                      <span className="font-semibold text-white">{league.downloadedMonitoredCount || 0}</span>
                    </div>
                    {(league.missingCount || 0) > 0 && (
                      <div className="flex items-center gap-1 whitespace-nowrap">
                        <span className="h-2 w-2 flex-shrink-0 rounded-full bg-red-500"></span>
                        <span className="text-gray-400">Missing:</span>
                        <span className="font-semibold text-red-400">{league.missingCount}</span>
                      </div>
                    )}
                  </div>

                  {league.qualityProfileId && (
                    <span className={BADGE_GRAY}>
                      Quality Profile #{league.qualityProfileId}
                    </span>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Floating Action Bar (when items are selected) */}
      {selectedLeagueIds.size > 0 && (
        <div className="fixed bottom-0 left-0 right-0 z-50 border-t border-red-900/50 bg-gray-900 shadow-lg shadow-black/50">
          <div className="mx-auto flex max-w-7xl flex-col items-center justify-between gap-3 px-4 py-3 md:px-8 md:py-4 sm:flex-row">
            <div className="flex flex-wrap items-center justify-center gap-2 md:gap-4 sm:justify-start">
              <span className="text-sm font-semibold text-white md:text-base">
                {selectedLeagueIds.size} {selectedLeagueIds.size === 1 ? 'League' : 'Leagues'} Selected
              </span>
              <button
                onClick={selectAllFiltered}
                className="text-xs text-gray-400 transition-colors hover:text-white md:text-sm"
              >
                Select All ({filteredLeagues.length})
              </button>
              <button
                onClick={clearSelection}
                className="text-xs text-gray-400 transition-colors hover:text-white md:text-sm"
              >
                Clear
              </button>
            </div>
            <div className="flex items-center gap-3">
              <button
                onClick={handleOpenRenameDialog}
                className="flex items-center gap-2 rounded-lg bg-blue-600 px-3 py-2 text-sm font-semibold text-white transition-colors hover:bg-blue-700 md:px-4 md:text-base"
              >
                <ArrowPathIcon className="h-4 w-4 md:h-5 md:w-5" />
                <span className="hidden sm:inline">Rename Files</span>
                <span className="sm:hidden">Rename</span>
              </button>
              <button
                onClick={() => setShowDeleteDialog(true)}
                className="flex items-center gap-2 rounded-lg bg-red-600 px-3 py-2 text-sm font-semibold text-white transition-colors hover:bg-red-700 md:px-4 md:text-base"
              >
                <TrashIcon className="h-4 w-4 md:h-5 md:w-5" />
                <span className="hidden sm:inline">Delete Selected</span>
                <span className="sm:hidden">Delete</span>
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation Dialog */}
      {showDeleteDialog && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70">
          <div className="mx-4 w-full max-w-lg rounded-lg border border-red-900/50 bg-gray-900 p-6 shadow-2xl">
            <h2 className="mb-4 text-xl font-bold text-white">Delete {selectedLeagueIds.size} {selectedLeagueIds.size === 1 ? 'League' : 'Leagues'}?</h2>

            <p className="mb-4 text-gray-400">
              The following {selectedLeagueIds.size === 1 ? 'league' : 'leagues'} and all associated events will be removed from Sportarr:
            </p>

            <div className="mb-4 max-h-40 overflow-y-auto rounded-lg bg-gray-800/50 p-3">
              {selectedLeagues.map(league => (
                <div key={league.id} className="flex items-center gap-2 py-1 text-sm text-white">
                  <span>{getSportIcon(league.sport || '')}</span>
                  <span>{league.name}</span>
                  {(league.eventCount || 0) > 0 && (
                    <span className="text-gray-500">({league.eventCount} events)</span>
                  )}
                </div>
              ))}
            </div>

            <label className="group mb-6 flex cursor-pointer items-start gap-3">
              <div className="relative flex items-center">
                <input
                  type="checkbox"
                  checked={deleteLeagueFolder}
                  onChange={(e) => setDeleteLeagueFolder(e.target.checked)}
                  className="sr-only"
                />
                <div className={`flex h-5 w-5 items-center justify-center rounded border-2 transition-colors ${
                  deleteLeagueFolder
                    ? 'border-red-600 bg-red-600'
                    : 'border-gray-500 group-hover:border-gray-400'
                }`}>
                  {deleteLeagueFolder && (
                    <CheckIcon className="h-3 w-3 text-white" />
                  )}
                </div>
              </div>
              <div>
                <span className="font-medium text-white">Delete league folder(s)</span>
                <p className="text-sm text-gray-500">This will permanently delete the league folders and all files from disk.</p>
              </div>
            </label>

            {deleteLeagueFolder && (
              <div className="mb-4 rounded-lg border border-red-600/50 bg-red-900/30 p-3">
                <p className="text-sm text-red-400">
                  <strong>Warning:</strong> This action cannot be undone. All media files in the selected league folders will be permanently deleted.
                </p>
              </div>
            )}

            <div className="flex justify-end gap-3">
              <button
                onClick={() => {
                  setShowDeleteDialog(false);
                  setDeleteLeagueFolder(false);
                }}
                disabled={isDeleting}
                className="rounded-lg bg-gray-700 px-4 py-2 font-semibold text-white transition-colors hover:bg-gray-600 disabled:opacity-50"
              >
                Cancel
              </button>
              <button
                onClick={handleDeleteSelected}
                disabled={isDeleting}
                className="flex items-center gap-2 rounded-lg bg-red-600 px-4 py-2 font-semibold text-white transition-colors hover:bg-red-700 disabled:opacity-50"
              >
                {isDeleting ? (
                  <>
                    <div className="h-4 w-4 animate-spin rounded-full border-b-2 border-white"></div>
                    Deleting...
                  </>
                ) : (
                  <>
                    <TrashIcon className="h-5 w-5" />
                    Delete
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Rename Confirmation Dialog */}
      {showRenameDialog && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70">
          <div className="mx-4 flex max-h-[90vh] w-full max-w-4xl flex-col rounded-lg border border-red-900/50 bg-gray-900 p-6 shadow-2xl">
            <h2 className="mb-4 text-xl font-bold text-white">
              Organize {selectedLeagueIds.size} Selected {selectedLeagueIds.size === 1 ? 'League' : 'Leagues'}
            </h2>

            <p className="mb-4 text-gray-400">
              The following files will be renamed according to your naming settings:
            </p>

            <div className="mb-4 rounded-lg bg-gray-800/50 p-3">
              <p className="mb-2 text-sm text-gray-400">Selected Leagues:</p>
              <div className="flex flex-wrap gap-2">
                {selectedLeagues.map(league => (
                  <span key={league.id} className="flex items-center gap-1 rounded bg-gray-700 px-2 py-1 text-sm text-white">
                    <span>{getSportIcon(league.sport || '')}</span>
                    <span>{league.name}</span>
                  </span>
                ))}
              </div>
            </div>

            <div className="mb-4 flex-1 overflow-y-auto">
              {isLoadingPreview ? (
                <div className="flex h-32 items-center justify-center">
                  <div className="h-8 w-8 animate-spin rounded-full border-b-2 border-red-600"></div>
                </div>
              ) : renamePreview.length > 0 ? (
                <div className="space-y-2">
                  <div className="rounded-lg border border-blue-600/50 bg-blue-900/20 p-3">
                    <p className="text-sm text-blue-400">
                      <strong>{renamePreview.length}</strong> file{renamePreview.length !== 1 ? 's' : ''} will be renamed
                    </p>
                  </div>
                  <div className={`space-y-2 ${SCROLLABLE_LIST}`}>
                    {renamePreview.map((preview, index) => (
                      <div key={index} className="rounded-lg border border-red-900/20 bg-gray-800/50 p-3">
                        <div className="mb-2 flex items-center gap-2">
                          <span className="text-xs text-gray-500">{preview.leagueName}</span>
                          {preview.folderChanged && (
                            <span className="rounded bg-yellow-600/20 px-1.5 py-0.5 text-xs text-yellow-400">
                              Folder Change
                            </span>
                          )}
                        </div>
                        <div className="space-y-1">
                          <div>
                            <p className="text-xs text-gray-400">Current Path:</p>
                            <p className="break-all font-mono text-xs text-gray-300">{preview.existingPath}</p>
                          </div>
                          <div>
                            <p className="text-xs text-gray-400">New Path:</p>
                            <p className="break-all font-mono text-xs text-green-400">{preview.newPath}</p>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <div className="rounded-lg bg-gray-800/50 p-6 text-center">
                  <ArrowPathIcon className="mx-auto mb-3 h-12 w-12 text-gray-600" />
                  <p className="text-gray-400">No files need renaming</p>
                  <p className="mt-1 text-sm text-gray-500">
                    All files are already using the correct naming format
                  </p>
                </div>
              )}
            </div>

            <div className="flex justify-end gap-3 border-t border-gray-800 pt-4">
              <button
                onClick={() => setShowRenameDialog(false)}
                disabled={isRenaming}
                className="rounded-lg bg-gray-700 px-4 py-2 font-semibold text-white transition-colors hover:bg-gray-600 disabled:opacity-50"
              >
                Cancel
              </button>
              {renamePreview.length > 0 && (
                <button
                  onClick={handleRenameSelected}
                  disabled={isRenaming}
                  className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 font-semibold text-white transition-colors hover:bg-blue-700 disabled:opacity-50"
                >
                  {isRenaming ? (
                    <>
                      <div className="h-4 w-4 animate-spin rounded-full border-b-2 border-white"></div>
                      Renaming...
                    </>
                  ) : (
                    <>
                      <ArrowPathIcon className="h-5 w-5" />
                      Organize
                    </>
                  )}
                </button>
              )}
            </div>
          </div>
        </div>
      )}
    </PageShell>
  );
}
