import { Fragment, useState, useEffect, useMemo } from 'react';
import { toast } from 'sonner';
import { Dialog, Transition } from '@headlessui/react';
import {
  XMarkIcon,
  MagnifyingGlassIcon,
  ArrowDownTrayIcon,
  ExclamationTriangleIcon,
  NoSymbolIcon,
  ChevronUpIcon,
  ChevronDownIcon,
  FunnelIcon,
  CheckCircleIcon,
  XCircleIcon,
  FolderIcon,
} from '@heroicons/react/24/outline';
import { apiPost } from '../utils/api';

interface SeasonSearchModalProps {
  isOpen: boolean;
  onClose: () => void;
  leagueId: number;
  leagueName: string;
  season: string;
  qualityProfileId?: number;
}

interface MatchedFormat {
  name: string;
  score: number;
}

interface SeasonEventMatch {
  eventId: number;
  eventTitle: string;
  eventDate: string;
  episodeNumber?: number;
  confidence: number;
  matchReasons: string[];
  detectedPart?: string;
  hasFile: boolean;
  monitored: boolean;
}

interface SeasonSearchRelease {
  title: string;
  guid: string;
  downloadUrl: string;
  infoUrl?: string;
  indexer: string;
  indexerFlags?: string;
  protocol: string;
  size: number;
  quality?: string;
  source?: string;
  codec?: string;
  language?: string;
  seeders?: number;
  leechers?: number;
  publishDate: string;
  score: number;
  qualityScore: number;
  matchedFormats: MatchedFormat[];
  approved: boolean;
  rejections: string[];
  torrentInfoHash?: string;
  isSeasonPack: boolean;
  matchedEventCount: number;
  bestConfidence: number;
  detectedPart?: string;
  matchedEvents: SeasonEventMatch[];
}

interface SeasonSearchResults {
  leagueId: number;
  leagueName: string;
  season: string;
  eventCount: number;
  monitoredEventCount: number;
  downloadedEventCount: number;
  releases: SeasonSearchRelease[];
  events: Array<{
    id: number;
    title: string;
    eventDate: string;
    episodeNumber?: number;
    monitored: boolean;
    hasFile: boolean;
  }>;
}

type SortDirection = 'asc' | 'desc';
type SortField = 'score' | 'quality' | 'source' | 'age' | 'title' | 'indexer' | 'size' | 'peers' | 'events';

export default function SeasonSearchModal({
  isOpen,
  onClose,
  leagueId,
  leagueName,
  season,
  qualityProfileId,
}: SeasonSearchModalProps) {
  const [isSearching, setIsSearching] = useState(false);
  const [searchResults, setSearchResults] = useState<SeasonSearchResults | null>(null);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [downloadingIndex, setDownloadingIndex] = useState<number | null>(null);
  const [sortField, setSortField] = useState<SortField>('events');
  const [sortDirection, setSortDirection] = useState<SortDirection>('desc');
  const [showFilters, setShowFilters] = useState(false);
  const [filterSeasonPacksOnly, setFilterSeasonPacksOnly] = useState(false);
  const [expandedReleases, setExpandedReleases] = useState<Set<string>>(new Set());

  // Clear search results when modal opens for a different season
  useEffect(() => {
    if (isOpen) {
      setSearchResults(null);
      setSearchError(null);
      setDownloadingIndex(null);
      setExpandedReleases(new Set());
    }
  }, [isOpen, leagueId, season]);

  const formatFileSize = (bytes?: number) => {
    if (!bytes) return 'N/A';
    const gb = bytes / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(2)} GiB`;
    const mb = bytes / (1024 * 1024);
    return `${mb.toFixed(1)} MiB`;
  };

  const formatAge = (publishDate: string) => {
    const now = new Date();
    const published = new Date(publishDate);
    const diffMs = now.getTime() - published.getTime();
    const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24));

    if (diffDays === 0) return 'Today';
    if (diffDays === 1) return '1 day';
    return `${diffDays} days`;
  };

  const handleSearch = async () => {
    setIsSearching(true);
    setSearchError(null);
    setSearchResults(null);

    try {
      const endpoint = `/api/leagues/${leagueId}/seasons/${encodeURIComponent(season)}/search`;
      const response = await apiPost(endpoint, { qualityProfileId });
      const results = await response.json();
      setSearchResults(results);
    } catch (error) {
      console.error('Season search failed:', error);
      setSearchError('Failed to search indexers. Please try again.');
    } finally {
      setIsSearching(false);
    }
  };

  const handleDownload = async (release: SeasonSearchRelease, index: number) => {
    setDownloadingIndex(index);
    setSearchError(null);

    try {
      // For season packs, we need to grab the release and associate it with matching events
      // The backend will handle mapping the release to specific events during import
      const response = await apiPost('/api/release/grab', {
        title: release.title,
        guid: release.guid,
        downloadUrl: release.downloadUrl,
        indexer: release.indexer,
        protocol: release.protocol,
        size: release.size,
        quality: release.quality,
        source: release.source,
        codec: release.codec,
        language: release.language,
        seeders: release.seeders,
        leechers: release.leechers,
        publishDate: release.publishDate,
        score: release.score,
        qualityScore: release.qualityScore,
        torrentInfoHash: release.torrentInfoHash,
        // Use the first matched event as the primary event
        eventId: release.matchedEvents[0]?.eventId,
        // Include all matched event IDs for season pack handling
        matchedEventIds: release.matchedEvents.map(e => e.eventId),
        isSeasonPack: release.isSeasonPack,
      });

      if (response.ok) {
        toast.success('Release grabbed', {
          description: release.isSeasonPack
            ? `Season pack queued for download (${release.matchedEventCount} events)`
            : `Release queued for download`
        });
        onClose();
      } else {
        const data = await response.json();
        throw new Error(data.message || 'Failed to grab release');
      }
    } catch (error) {
      console.error('Download failed:', error);
      setSearchError(error instanceof Error ? error.message : 'Failed to grab release');
    } finally {
      setDownloadingIndex(null);
    }
  };

  const toggleReleaseExpanded = (guid: string) => {
    setExpandedReleases(prev => {
      const next = new Set(prev);
      if (next.has(guid)) {
        next.delete(guid);
      } else {
        next.add(guid);
      }
      return next;
    });
  };

  // Get resolution rank for sorting
  const getResolutionRank = (quality: string | null | undefined): number => {
    if (!quality) return 0;
    const q = quality.toLowerCase();
    if (q.includes('2160p') || q.includes('4k')) return 4;
    if (q.includes('1080p')) return 3;
    if (q.includes('720p')) return 2;
    if (q.includes('480p')) return 1;
    return 0;
  };

  // Get source rank for sorting
  const getSourceRank = (source: string | null | undefined): number => {
    if (!source) return 0;
    const s = source.toLowerCase().replace(/-/g, '').replace(/ /g, '');
    if (s.includes('remux')) return 7;
    if (s.includes('bluray') || s.includes('bray')) return 6;
    if (s.includes('webdl')) return 5;
    if (s.includes('webrip')) return 4;
    if (s.includes('web')) return 3;
    if (s.includes('hdtv')) return 2;
    if (s.includes('dvd')) return 1;
    return 0;
  };

  // Parse age from publishDate for sorting
  const getAgeInDays = (publishDate: string | null | undefined): number => {
    if (!publishDate) return Infinity;
    const date = new Date(publishDate);
    const now = new Date();
    return Math.floor((now.getTime() - date.getTime()) / (1000 * 60 * 60 * 24));
  };

  const sortedResults = useMemo(() => {
    if (!searchResults?.releases) return [];

    let releases = [...searchResults.releases];

    // Apply filter
    if (filterSeasonPacksOnly) {
      releases = releases.filter(r => r.isSeasonPack);
    }

    return releases.sort((a, b) => {
      let comparison = 0;

      switch (sortField) {
        case 'events':
          comparison = a.matchedEventCount - b.matchedEventCount;
          break;
        case 'score':
          comparison = a.score - b.score;
          break;
        case 'quality':
          const qualA = a.qualityScore || getResolutionRank(a.quality);
          const qualB = b.qualityScore || getResolutionRank(b.quality);
          comparison = qualA - qualB;
          break;
        case 'source':
          comparison = getSourceRank(a.source) - getSourceRank(b.source);
          break;
        case 'age':
          comparison = getAgeInDays(a.publishDate) - getAgeInDays(b.publishDate);
          break;
        case 'title':
          comparison = (a.title || '').localeCompare(b.title || '');
          break;
        case 'indexer':
          comparison = (a.indexer || '').localeCompare(b.indexer || '');
          break;
        case 'size':
          comparison = (a.size ?? 0) - (b.size ?? 0);
          break;
        case 'peers':
          const peersA = (a.seeders ?? 0) + (a.leechers ?? 0);
          const peersB = (b.seeders ?? 0) + (b.leechers ?? 0);
          comparison = peersA - peersB;
          break;
      }

      return sortDirection === 'desc' ? -comparison : comparison;
    });
  }, [searchResults, sortField, sortDirection, filterSeasonPacksOnly]);

  const handleSort = (field: SortField) => {
    if (sortField === field) {
      setSortDirection(prev => prev === 'desc' ? 'asc' : 'desc');
    } else {
      setSortField(field);
      setSortDirection('desc');
    }
  };

  const getProtocol = (release: SeasonSearchRelease): 'torrent' | 'usenet' => {
    if (release.protocol) {
      const proto = release.protocol.toLowerCase();
      if (proto === 'torrent' || proto.includes('torrent')) return 'torrent';
      if (proto === 'usenet' || proto.includes('usenet') || proto === 'nzb') return 'usenet';
    }
    if (release.seeders !== null || release.leechers !== null) return 'torrent';
    if (release.indexer?.toLowerCase().includes('nzb')) return 'usenet';
    return 'usenet';
  };

  return (
    <Transition
      appear
      show={isOpen}
      as={Fragment}
      unmount={true}
      afterLeave={() => {
        document.querySelectorAll('[inert]').forEach((el) => {
          el.removeAttribute('inert');
        });
      }}
    >
      <Dialog as="div" className="relative z-50" onClose={onClose}>
        <Transition.Child
          as={Fragment}
          enter="ease-out duration-300"
          enterFrom="opacity-0"
          enterTo="opacity-100"
          leave="ease-in duration-200"
          leaveFrom="opacity-100"
          leaveTo="opacity-0"
        >
          <div className="fixed inset-0 bg-black/80" />
        </Transition.Child>

        <div className="fixed inset-0 overflow-y-auto">
          <div className="flex min-h-full items-center justify-center p-2">
            <Transition.Child
              as={Fragment}
              enter="ease-out duration-300"
              enterFrom="opacity-0 scale-95"
              enterTo="opacity-100 scale-100"
              leave="ease-in duration-200"
              leaveFrom="opacity-100 scale-100"
              leaveTo="opacity-0 scale-95"
            >
              <Dialog.Panel className="w-[98vw] max-w-none mx-2 md:mx-4 transform overflow-hidden rounded-lg bg-gradient-to-br from-gray-900 to-black border border-red-900/30 shadow-2xl transition-all">
                {/* Header */}
                <div className="relative bg-gradient-to-r from-gray-900 via-red-950/20 to-gray-900 border-b border-red-900/30">
                  <div className="px-3 md:px-6 py-3 md:py-4 flex items-center justify-between">
                    <div className="min-w-0 flex-1 mr-2">
                      <h2 className="text-base md:text-xl font-bold text-white truncate">
                        Season Search: {leagueName} - {season}
                      </h2>
                      {searchResults && (
                        <p className="text-xs md:text-sm text-gray-400 mt-1">
                          {searchResults.eventCount} events in season
                          ({searchResults.monitoredEventCount} monitored, {searchResults.downloadedEventCount} downloaded)
                        </p>
                      )}
                    </div>
                    <button
                      onClick={onClose}
                      className="p-1.5 md:p-2 rounded-lg bg-black/50 hover:bg-black/70 transition-colors flex-shrink-0"
                    >
                      <XMarkIcon className="w-5 h-5 md:w-6 md:h-6 text-white" />
                    </button>
                  </div>
                </div>

                {/* Search Controls */}
                <div className="px-3 md:px-6 py-2 md:py-3 border-b border-gray-800 flex flex-col sm:flex-row sm:items-center justify-between gap-2">
                  <p className="text-gray-400 text-xs md:text-sm hidden sm:block">
                    Search for season packs and releases matching this season
                  </p>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => setShowFilters(!showFilters)}
                      className="px-2 md:px-3 py-1 md:py-1.5 bg-gray-800 hover:bg-gray-700 text-gray-300 rounded transition-colors flex items-center gap-1 md:gap-1.5 text-xs md:text-sm"
                    >
                      <FunnelIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                      Filter
                    </button>
                    <button
                      onClick={handleSearch}
                      disabled={isSearching}
                      className="px-3 md:px-4 py-1 md:py-1.5 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 text-white rounded transition-colors flex items-center gap-1.5 md:gap-2 text-xs md:text-sm"
                    >
                      {isSearching ? (
                        <>
                          <div className="animate-spin rounded-full h-3.5 w-3.5 md:h-4 md:w-4 border-b-2 border-white"></div>
                          <span className="hidden sm:inline">Searching...</span>
                        </>
                      ) : (
                        <>
                          <MagnifyingGlassIcon className="w-3.5 h-3.5 md:w-4 md:h-4" />
                          <span className="hidden sm:inline">Search Season</span>
                          <span className="sm:hidden">Search</span>
                        </>
                      )}
                    </button>
                  </div>
                </div>

                {/* Filter Panel */}
                {showFilters && (
                  <div className="px-3 md:px-6 py-2 border-b border-gray-800 bg-gray-900/50">
                    <label className="flex items-center gap-2 text-xs md:text-sm text-gray-300">
                      <input
                        type="checkbox"
                        checked={filterSeasonPacksOnly}
                        onChange={(e) => setFilterSeasonPacksOnly(e.target.checked)}
                        className="rounded bg-gray-700 border-gray-600 text-red-600 focus:ring-red-600"
                      />
                      Show season packs only (releases matching multiple events)
                    </label>
                  </div>
                )}

                {/* Error Message */}
                {searchError && (
                  <div className="mx-6 mt-3 bg-red-900/20 border border-red-600/50 rounded-lg p-3">
                    <p className="text-red-400 text-sm">{searchError}</p>
                  </div>
                )}

                {/* Results Count */}
                {searchResults && searchResults.releases.length > 0 && (
                  <div className="px-6 py-2 text-gray-400 text-sm flex items-center gap-4">
                    <span>Found {searchResults.releases.length} releases</span>
                    {filterSeasonPacksOnly && sortedResults.length !== searchResults.releases.length && (
                      <span className="text-yellow-500">
                        (showing {sortedResults.length} season packs)
                      </span>
                    )}
                  </div>
                )}

                {/* Content */}
                <div className="max-h-[65vh] overflow-y-auto">
                  {isSearching ? (
                    <div className="p-8 text-center">
                      <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-red-600 mx-auto mb-4"></div>
                      <p className="text-gray-400">Searching indexers for season releases...</p>
                    </div>
                  ) : sortedResults.length > 0 ? (
                    <table className="w-full text-xs">
                      <thead className="bg-gray-900/80 sticky top-0 z-10">
                        <tr className="border-b border-gray-800">
                          <th
                            className="text-left py-1.5 px-2 text-gray-400 font-medium w-[60px] cursor-pointer hover:text-white transition-colors select-none"
                            onClick={() => handleSort('events')}
                            title="Sort by matched events"
                          >
                            <div className="flex items-center gap-0.5">
                              <span>Events</span>
                              {sortField === 'events' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                            </div>
                          </th>
                          <th
                            className="text-left py-1.5 px-2 text-gray-400 font-medium w-[52px] cursor-pointer hover:text-white transition-colors select-none"
                            onClick={() => handleSort('source')}
                            title="Sort by source"
                          >
                            <div className="flex items-center gap-0.5">
                              <span>Source</span>
                              {sortField === 'source' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                            </div>
                          </th>
                          <th
                            className="text-left py-1.5 px-2 text-gray-400 font-medium w-[60px] cursor-pointer hover:text-white transition-colors select-none"
                            onClick={() => handleSort('age')}
                            title="Sort by age"
                          >
                            <div className="flex items-center gap-0.5">
                              <span>Age</span>
                              {sortField === 'age' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                            </div>
                          </th>
                          <th
                            className="text-left py-1.5 px-2 text-gray-400 font-medium min-w-[150px] cursor-pointer hover:text-white transition-colors select-none"
                            onClick={() => handleSort('title')}
                            title="Sort by title"
                          >
                            <div className="flex items-center gap-0.5">
                              <span>Title</span>
                              {sortField === 'title' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                            </div>
                          </th>
                          <th
                            className="text-left py-1.5 px-2 text-gray-400 font-medium w-[100px] cursor-pointer hover:text-white transition-colors select-none"
                            onClick={() => handleSort('indexer')}
                            title="Sort by indexer"
                          >
                            <div className="flex items-center gap-0.5">
                              <span>Indexer</span>
                              {sortField === 'indexer' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                            </div>
                          </th>
                          <th
                            className="text-right py-1.5 px-2 text-gray-400 font-medium w-[70px] cursor-pointer hover:text-white transition-colors select-none"
                            onClick={() => handleSort('size')}
                            title="Sort by size"
                          >
                            <div className="flex items-center justify-end gap-0.5">
                              <span>Size</span>
                              {sortField === 'size' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                            </div>
                          </th>
                          <th
                            className="text-center py-1.5 px-2 text-gray-400 font-medium w-[60px] cursor-pointer hover:text-white transition-colors select-none"
                            onClick={() => handleSort('peers')}
                            title="Sort by peers"
                          >
                            <div className="flex items-center justify-center gap-0.5">
                              <span>Peers</span>
                              {sortField === 'peers' && (sortDirection === 'desc' ? <ChevronDownIcon className="w-3 h-3" /> : <ChevronUpIcon className="w-3 h-3" />)}
                            </div>
                          </th>
                          <th className="text-right py-1.5 px-2 text-gray-400 font-medium w-[80px]">Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {sortedResults.map((result, index) => {
                          const protocol = getProtocol(result);
                          const isExpanded = expandedReleases.has(result.guid);
                          const hasRejections = result.rejections && result.rejections.length > 0;

                          return (
                            <Fragment key={result.guid}>
                              <tr
                                className={`border-b border-gray-800/50 hover:bg-gray-800/30 transition-colors ${hasRejections ? 'opacity-60' : ''}`}
                              >
                                {/* Events Column */}
                                <td className="py-1 px-2">
                                  <button
                                    onClick={() => toggleReleaseExpanded(result.guid)}
                                    className="flex items-center gap-1 text-white hover:text-red-400 transition-colors"
                                    title={`Click to see ${result.matchedEventCount} matched events`}
                                  >
                                    {result.isSeasonPack ? (
                                      <FolderIcon className="w-3.5 h-3.5 text-yellow-500" />
                                    ) : (
                                      <span className="w-3.5 h-3.5" />
                                    )}
                                    <span className={result.isSeasonPack ? 'text-yellow-400 font-medium' : ''}>
                                      {result.matchedEventCount}
                                    </span>
                                    {isExpanded ? (
                                      <ChevronUpIcon className="w-3 h-3" />
                                    ) : (
                                      <ChevronDownIcon className="w-3 h-3" />
                                    )}
                                  </button>
                                </td>

                                {/* Source Column */}
                                <td className="py-1 px-2">
                                  <div className="flex flex-col gap-0.5">
                                    {result.quality && (
                                      <span className="px-1 py-0.5 bg-blue-900/50 text-blue-400 text-[10px] rounded whitespace-nowrap">
                                        {result.quality}
                                      </span>
                                    )}
                                    {result.source && (
                                      <span className="px-1 py-0.5 bg-gray-700 text-gray-300 text-[10px] rounded whitespace-nowrap">
                                        {result.source}
                                      </span>
                                    )}
                                  </div>
                                </td>

                                {/* Age Column */}
                                <td className="py-1 px-2 text-gray-400 whitespace-nowrap">
                                  {formatAge(result.publishDate)}
                                </td>

                                {/* Title Column */}
                                <td className="py-1 px-2" style={{ maxWidth: '400px' }}>
                                  <div className="flex flex-col">
                                    <span className="text-white truncate" title={result.title}>
                                      {result.title}
                                    </span>
                                    {result.isSeasonPack && (
                                      <span className="text-yellow-500 text-[10px]">Season Pack</span>
                                    )}
                                  </div>
                                </td>

                                {/* Indexer Column */}
                                <td className="py-1 px-2">
                                  <div className="flex items-center gap-1">
                                    <span className={`w-1.5 h-1.5 rounded-full ${protocol === 'torrent' ? 'bg-green-500' : 'bg-blue-500'}`}
                                          title={protocol === 'torrent' ? 'Torrent' : 'Usenet'} />
                                    <span className="text-gray-300 truncate" style={{ maxWidth: '80px' }}>
                                      {result.indexer}
                                    </span>
                                  </div>
                                </td>

                                {/* Size Column */}
                                <td className="py-1 px-2 text-gray-400 text-right whitespace-nowrap">
                                  {formatFileSize(result.size)}
                                </td>

                                {/* Peers Column */}
                                <td className="py-1 px-2 text-center">
                                  {protocol === 'torrent' && (
                                    <span className="text-gray-400">
                                      {result.seeders ?? 0} / {result.leechers ?? 0}
                                    </span>
                                  )}
                                </td>

                                {/* Actions Column */}
                                <td className="py-1 px-2">
                                  <div className="flex items-center justify-end gap-1">
                                    {hasRejections && (
                                      <div className="relative group">
                                        <ExclamationTriangleIcon className="w-4 h-4 text-yellow-500 cursor-help" />
                                        <div className="absolute right-0 top-5 z-50 hidden group-hover:block w-56 p-2 bg-gray-900 border border-gray-700 rounded-lg shadow-xl">
                                          {result.rejections.map((r, i) => (
                                            <p key={i} className="text-yellow-400 text-[10px]">• {r}</p>
                                          ))}
                                        </div>
                                      </div>
                                    )}
                                    <button
                                      onClick={() => handleDownload(result, index)}
                                      disabled={downloadingIndex !== null}
                                      className="p-1.5 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 text-white rounded transition-colors"
                                      title={result.isSeasonPack ? `Download season pack (${result.matchedEventCount} events)` : 'Download'}
                                    >
                                      {downloadingIndex === index ? (
                                        <div className="animate-spin rounded-full h-3.5 w-3.5 border-b-2 border-white"></div>
                                      ) : (
                                        <ArrowDownTrayIcon className="w-3.5 h-3.5" />
                                      )}
                                    </button>
                                  </div>
                                </td>
                              </tr>

                              {/* Expanded row showing matched events */}
                              {isExpanded && (
                                <tr className="bg-gray-900/50">
                                  <td colSpan={8} className="py-2 px-4">
                                    <div className="text-xs">
                                      <p className="text-gray-400 mb-2 font-medium">Matched Events:</p>
                                      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-2">
                                        {result.matchedEvents.map((event) => (
                                          <div
                                            key={event.eventId}
                                            className="flex items-center gap-2 px-2 py-1 bg-gray-800/50 rounded"
                                          >
                                            {event.hasFile ? (
                                              <CheckCircleIcon className="w-4 h-4 text-green-500 flex-shrink-0" />
                                            ) : event.monitored ? (
                                              <XCircleIcon className="w-4 h-4 text-red-500 flex-shrink-0" />
                                            ) : (
                                              <div className="w-4 h-4 border border-gray-600 rounded flex-shrink-0" />
                                            )}
                                            <div className="min-w-0 flex-1">
                                              <p className="text-white truncate" title={event.eventTitle}>
                                                {event.eventTitle}
                                              </p>
                                              <p className="text-gray-500 text-[10px]">
                                                {new Date(event.eventDate).toLocaleDateString()}
                                                {event.detectedPart && (
                                                  <span className="ml-1 px-1 bg-purple-900/50 text-purple-400 rounded">
                                                    {event.detectedPart}
                                                  </span>
                                                )}
                                              </p>
                                            </div>
                                            <span className={`text-[10px] px-1 rounded ${
                                              event.confidence >= 80 ? 'bg-green-900/50 text-green-400' :
                                              event.confidence >= 50 ? 'bg-yellow-900/50 text-yellow-400' :
                                              'bg-gray-700 text-gray-400'
                                            }`}>
                                              {event.confidence}%
                                            </span>
                                          </div>
                                        ))}
                                      </div>
                                    </div>
                                  </td>
                                </tr>
                              )}
                            </Fragment>
                          );
                        })}
                      </tbody>
                    </table>
                  ) : searchResults ? (
                    <div className="p-8 text-center text-gray-400">
                      <p>No releases found matching this season.</p>
                      <p className="text-sm mt-2">Try adjusting your indexers or search later.</p>
                    </div>
                  ) : (
                    <div className="p-8 text-center text-gray-400">
                      <MagnifyingGlassIcon className="w-12 h-12 mx-auto mb-4 opacity-50" />
                      <p>Click "Search Season" to find releases for this season.</p>
                      <p className="text-sm mt-2">Season packs will match multiple events at once.</p>
                    </div>
                  )}
                </div>

                {/* Footer */}
                <div className="border-t border-gray-800 px-6 py-3 flex justify-end">
                  <button
                    onClick={onClose}
                    className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded transition-colors text-sm"
                  >
                    Close
                  </button>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </div>
      </Dialog>
    </Transition>
  );
}
