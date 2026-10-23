import React, { useState, useEffect } from 'react';
import { Camera, Trash2, X, AlertCircle, CheckCircle2, ChevronLeft, ChevronRight, Eye } from 'lucide-react';
import { api, ScreenshotRecord, ChromeProfileRecord } from '../api/client';

interface ScreenshotsViewProps {
  profiles: ChromeProfileRecord[];
}

export const ScreenshotsView: React.FC<ScreenshotsViewProps> = ({ profiles }) => {
  const [screenshots, setScreenshots] = useState<ScreenshotRecord[]>([]);
  const [totalRecords, setTotalRecords] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(18);
  const [selectedProfile, setSelectedProfile] = useState('ALL');
  const [activeModalScreenshot, setActiveModalScreenshot] = useState<ScreenshotRecord | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const fetchScreenshots = async () => {
    setIsLoading(true);
    try {
      const data = await api.getScreenshots({
        profileId: selectedProfile === 'ALL' ? undefined : selectedProfile,
        page,
        pageSize
      });
      setScreenshots(data.records);
      setTotalRecords(data.total_records);
    } catch (err) {
      console.error('Failed to load screenshots:', err);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchScreenshots();
  }, [page, selectedProfile]);

  const handleDelete = async (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    if (!confirm('Are you sure you want to delete this screenshot permanently from local disk?')) {
      return;
    }

    try {
      await api.deleteScreenshot(id);
      if (activeModalScreenshot?.screenshot_id === id) {
        setActiveModalScreenshot(null);
      }
      fetchScreenshots();
    } catch (err) {
      alert('Failed to delete screenshot: ' + (err as Error).message);
    }
  };

  const totalPages = Math.ceil(totalRecords / pageSize) || 1;

  return (
    <div className="space-y-4">
      {/* Header & Filter */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
        <div className="flex items-center space-x-2">
          <Camera className="w-5 h-5 text-amber-400" />
          <h2 className="text-sm font-bold text-white">Local Visible-Tab Screenshot Gallery</h2>
        </div>

        <div className="flex items-center space-x-3 w-full sm:w-auto">
          <select
            value={selectedProfile}
            onChange={(e) => {
              setSelectedProfile(e.target.value);
              setPage(1);
            }}
            className="bg-slate-800 border border-slate-700 text-slate-100 text-xs px-3 py-1.5 rounded-lg focus:outline-none focus:border-indigo-500 w-full sm:w-auto"
          >
            <option value="ALL">All Enrolled Profiles</option>
            {profiles.map((p) => (
              <option key={p.profileId} value={p.profileId}>
                {p.profileName}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Grid of Screenshots */}
      {isLoading ? (
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-12 text-center text-slate-500 text-sm">
          Loading screenshots...
        </div>
      ) : screenshots.length === 0 ? (
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-12 text-center text-slate-500 text-sm">
          No screenshots captured yet. Screenshots are captured automatically at the configured interval (default 10m)
          when Chrome holds active OS foreground focus.
        </div>
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
          {screenshots.map((s) => (
            <div
              key={s.screenshot_id}
              onClick={() => s.has_image && setActiveModalScreenshot(s)}
              className={`bg-slate-900 border border-slate-800 rounded-xl overflow-hidden group shadow-sm flex flex-col justify-between transition-all ${
                s.has_image ? 'cursor-pointer hover:border-slate-700 hover:shadow-md' : 'opacity-80'
              }`}
            >
              {/* Thumbnail Container */}
              <div className="relative aspect-video bg-slate-950 flex items-center justify-center overflow-hidden border-b border-slate-800/80">
                {s.has_image ? (
                  <img
                    src={`/api/screenshots/${encodeURIComponent(s.screenshot_id)}/image`}
                    alt={s.url}
                    className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                    loading="lazy"
                  />
                ) : (
                  <div className="flex flex-col items-center justify-center p-4 text-center">
                    <AlertCircle className="w-8 h-8 text-amber-500/80 mb-1" />
                    <span className="text-[11px] font-mono font-semibold text-amber-400">
                      {s.failure_reason || 'CAPTURE_FAILED'}
                    </span>
                    <span className="text-[10px] text-slate-500 mt-0.5">Honest Failure Recorded</span>
                  </div>
                )}

                {/* Status Badge */}
                <div className="absolute top-2 left-2">
                  <span
                    className={`px-2 py-0.5 rounded text-[10px] font-bold uppercase tracking-wider backdrop-blur-md ${
                      s.capture_status === 'SUCCESS'
                        ? 'bg-emerald-500/80 text-white'
                        : 'bg-amber-500/80 text-white'
                    }`}
                  >
                    {s.capture_status}
                  </span>
                </div>

                {/* Delete Button */}
                <button
                  onClick={(e) => handleDelete(s.screenshot_id, e)}
                  title="Delete screenshot from local disk"
                  className="absolute top-2 right-2 p-1.5 rounded bg-slate-900/80 text-slate-400 hover:text-red-400 opacity-0 group-hover:opacity-100 transition-opacity backdrop-blur-md"
                >
                  <Trash2 className="w-3.5 h-3.5" />
                </button>
              </div>

              {/* Metadata Footer */}
              <div className="p-3 text-xs space-y-1">
                <div className="flex items-center justify-between text-slate-400 font-mono text-[11px]">
                  <span>{new Date(s.capture_time_utc).toLocaleTimeString()}</span>
                  <span>{(s.file_size_bytes / 1024).toFixed(0)} KB</span>
                </div>
                <div className="text-white font-medium truncate" title={s.url}>
                  {s.url}
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Pagination Controls */}
      {screenshots.length > 0 && (
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-3 flex items-center justify-between text-xs text-slate-400">
          <div>
            Showing {screenshots.length > 0 ? (page - 1) * pageSize + 1 : 0} to{' '}
            {Math.min(page * pageSize, totalRecords)} of {totalRecords} screenshots
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
      )}

      {/* Lightbox Modal */}
      {activeModalScreenshot && (
        <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl max-w-5xl w-full max-h-[90vh] overflow-hidden flex flex-col shadow-2xl">
            {/* Modal Header */}
            <div className="p-4 border-b border-slate-800 flex items-center justify-between">
              <div>
                <h3 className="text-sm font-semibold text-white truncate max-w-xl">
                  {activeModalScreenshot.url}
                </h3>
                <p className="text-xs text-slate-400 font-mono">
                  Captured: {new Date(activeModalScreenshot.capture_time_utc).toLocaleString()} &bull; Size:{' '}
                  {(activeModalScreenshot.file_size_bytes / 1024).toFixed(1)} KB
                </p>
              </div>
              <div className="flex items-center space-x-2">
                <button
                  onClick={(e) => handleDelete(activeModalScreenshot.screenshot_id, e)}
                  className="p-2 rounded-lg bg-red-500/10 hover:bg-red-500/20 text-red-400 transition-colors"
                  title="Delete screenshot"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
                <button
                  onClick={() => setActiveModalScreenshot(null)}
                  className="p-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 transition-colors"
                >
                  <X className="w-4 h-4" />
                </button>
              </div>
            </div>

            {/* Modal Image */}
            <div className="p-4 overflow-auto bg-slate-950 flex items-center justify-center flex-1">
              <img
                src={`/api/screenshots/${encodeURIComponent(activeModalScreenshot.screenshot_id)}/image`}
                alt={activeModalScreenshot.url}
                className="max-h-[70vh] object-contain rounded-lg border border-slate-800"
              />
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
