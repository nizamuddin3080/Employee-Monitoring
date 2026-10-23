import React, { useState, useEffect } from 'react';
import { Search, Filter, ExternalLink, Calendar, ChevronLeft, ChevronRight, Clock, RotateCw } from 'lucide-react';
import { api, ActivityRecord, ChromeProfileRecord } from '../api/client';

interface ActivityViewProps {
  profiles: ChromeProfileRecord[];
}

export const ActivityView: React.FC<ActivityViewProps> = ({ profiles }) => {
  const [records, setRecords] = useState<ActivityRecord[]>([]);
  const [totalRecords, setTotalRecords] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(25);
  const [selectedProfile, setSelectedProfile] = useState('ALL');
  const [domainSearch, setDomainSearch] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  const fetchRecords = async () => {
    setIsLoading(true);
    try {
      const res = await api.getActivity({
        profileId: selectedProfile === 'ALL' ? undefined : selectedProfile,
        domain: domainSearch.trim() || undefined,
        page,
        pageSize
      });
      setRecords(res.records);
      setTotalRecords(res.total_records);
    } catch (err) {
      console.error('Failed to load activity records:', err);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchRecords();
    const interval = setInterval(fetchRecords, 5000);
    return () => clearInterval(interval);
  }, [page, selectedProfile]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    fetchRecords();
  };

  const formatDuration = (seconds: number) => {
    if (!seconds || seconds <= 0) return '0s';
    const mins = Math.floor(seconds / 60);
    const secs = seconds % 60;
    if (mins > 0) {
      return `${mins}m ${secs}s`;
    }
    return `${secs}s`;
  };

  const totalPages = Math.ceil(totalRecords / pageSize) || 1;

  return (
    <div className="space-y-4">
      {/* Filters Bar */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 flex flex-col md:flex-row gap-3 items-stretch md:items-center justify-between">
        <form onSubmit={handleSearchSubmit} className="flex-1 flex gap-2">
          <div className="relative flex-1">
            <Search className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
            <input
              type="text"
              value={domainSearch}
              onChange={(e) => setDomainSearch(e.target.value)}
              placeholder="Search domain or keyword (e.g. github.com)..."
              className="w-full bg-slate-800 border border-slate-700 text-slate-100 text-xs sm:text-sm pl-9 pr-3 py-2 rounded-lg focus:outline-none focus:border-indigo-500"
            />
          </div>
          <button
            type="submit"
            className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg text-xs sm:text-sm font-medium transition-colors"
          >
            Filter
          </button>
        </form>

        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2">
            <Filter className="w-4 h-4 text-slate-400 shrink-0" />
            <select
              value={selectedProfile}
              onChange={(e) => {
                setSelectedProfile(e.target.value);
                setPage(1);
              }}
              className="bg-slate-800 border border-slate-700 text-slate-100 text-xs sm:text-sm px-3 py-2 rounded-lg focus:outline-none focus:border-indigo-500"
            >
              <option value="ALL">All Enrolled Profiles</option>
              {profiles.map((p) => (
                <option key={p.profileId} value={p.profileId}>
                  {p.profileName} ({p.profileId.slice(0, 8)}...)
                </option>
              ))}
            </select>
          </div>

          <button
            type="button"
            onClick={() => fetchRecords()}
            title="Refresh Activity Log"
            className="px-3 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-lg text-xs sm:text-sm font-medium transition-colors flex items-center gap-1.5 shrink-0 border border-slate-700"
          >
            <RotateCw className={`w-3.5 h-3.5 ${isLoading ? 'animate-spin text-indigo-400' : ''}`} />
            <span>Refresh</span>
          </button>
        </div>
      </div>

      {/* Activity Table */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs sm:text-sm text-slate-300">
            <thead className="bg-slate-800/60 text-slate-400 uppercase text-[11px] font-semibold border-b border-slate-800">
              <tr>
                <th className="px-4 py-3">Time</th>
                <th className="px-4 py-3">Profile</th>
                <th className="px-4 py-3">Page Title & URL</th>
                <th className="px-4 py-3">Domain</th>
                <th className="px-4 py-3 text-right">Active Time</th>
                <th className="px-4 py-3 text-center">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800">
              {isLoading ? (
                <tr>
                  <td colSpan={6} className="px-4 py-8 text-center text-slate-500">
                    Loading activity timeline...
                  </td>
                </tr>
              ) : records.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-4 py-8 text-center text-slate-500">
                    No active URL browsing records found matching current filters.
                  </td>
                </tr>
              ) : (
                records.map((r) => (
                  <tr key={r.session_id} className="hover:bg-slate-800/40 transition-colors">
                    <td className="px-4 py-3 font-mono text-xs text-slate-400 whitespace-nowrap">
                      {new Date(r.start_time_utc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap">
                      <span className="px-2 py-0.5 rounded text-[11px] font-medium bg-slate-800 text-slate-300 border border-slate-700">
                        {r.profile_name}
                      </span>
                    </td>
                    <td className="px-4 py-3 max-w-md">
                      <div className="font-medium text-white truncate" title={r.page_title}>
                        {r.page_title || r.domain}
                      </div>
                      <div className="text-xs text-slate-400 font-mono truncate" title={r.url}>
                        {r.url}
                      </div>
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap text-slate-300 font-mono text-xs">
                      {r.domain}
                    </td>
                    <td className="px-4 py-3 text-right whitespace-nowrap font-mono font-semibold text-emerald-400">
                      {formatDuration(r.duration_seconds)}
                    </td>
                    <td className="px-4 py-3 text-center whitespace-nowrap">
                      <span
                        className={`px-2 py-0.5 rounded-full text-[10px] font-semibold uppercase tracking-wider ${
                          r.session_status === 'ACTIVE'
                            ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                            : 'bg-slate-800 text-slate-400'
                        }`}
                      >
                        {r.session_status}
                      </span>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination Footer */}
        <div className="p-3.5 border-t border-slate-800 flex items-center justify-between text-xs text-slate-400">
          <div>
            Showing {records.length > 0 ? (page - 1) * pageSize + 1 : 0} to{' '}
            {Math.min(page * pageSize, totalRecords)} of {totalRecords} records
          </div>
          <div className="flex items-center space-x-1">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
              className="p-1 rounded bg-slate-800 hover:bg-slate-700 disabled:opacity-30 disabled:cursor-not-allowed"
            >
              <ChevronLeft className="w-4 h-4" />
            </button>
            <span className="px-2">
              Page {page} of {totalPages}
            </span>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              className="p-1 rounded bg-slate-800 hover:bg-slate-700 disabled:opacity-30 disabled:cursor-not-allowed"
            >
              <ChevronRight className="w-4 h-4" />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
