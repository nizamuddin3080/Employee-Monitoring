import React, { useState } from 'react';
import {
  FileSpreadsheet,
  Download,
  FolderOpen,
  CheckCircle,
  AlertCircle,
  Calendar,
  Layers,
  Shield,
  FileCheck,
  RefreshCw
} from 'lucide-react';
import { api, ChromeProfileRecord, CsvExportResponse } from '../api/client';

interface ExportViewProps {
  profiles: ChromeProfileRecord[];
}

export const ExportView: React.FC<ExportViewProps> = ({ profiles }) => {
  const todayStr = new Date().toISOString().split('T')[0];
  const sevenDaysAgoStr = new Date(Date.now() - 7 * 86400000).toISOString().split('T')[0];

  const [startDate, setStartDate] = useState(sevenDaysAgoStr);
  const [endDate, setEndDate] = useState(todayStr);
  const [selectedAllProfiles, setSelectedAllProfiles] = useState(true);
  const [selectedProfileIds, setSelectedProfileIds] = useState<string[]>([]);

  // Section inclusion flags
  const [includeMetadata, setIncludeMetadata] = useState(true);
  const [includeProfileSummary, setIncludeProfileSummary] = useState(true);
  const [includeUrlActivity, setIncludeUrlActivity] = useState(true);
  const [includeScreenshotMetadata, setIncludeScreenshotMetadata] = useState(true);
  const [includeNonChromeApps, setIncludeNonChromeApps] = useState(true);

  const [isExporting, setIsExporting] = useState(false);
  const [exportResult, setExportResult] = useState<CsvExportResponse | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [revealedStatus, setRevealedStatus] = useState<string | null>(null);

  const setQuickRange = (days: number) => {
    const end = new Date().toISOString().split('T')[0];
    const start = new Date(Date.now() - days * 86400000).toISOString().split('T')[0];
    setStartDate(start);
    setEndDate(end);
  };

  const handleToggleProfile = (pid: string) => {
    setSelectedAllProfiles(false);
    if (selectedProfileIds.includes(pid)) {
      setSelectedProfileIds(selectedProfileIds.filter((id) => id !== pid));
    } else {
      setSelectedProfileIds([...selectedProfileIds, pid]);
    }
  };

  const handleToggleAllProfiles = (checked: boolean) => {
    setSelectedAllProfiles(checked);
    if (checked) {
      setSelectedProfileIds([]);
    } else {
      setSelectedProfileIds(profiles.map((p) => p.profileId));
    }
  };

  const handleGenerateCsv = async () => {
    setIsExporting(true);
    setErrorMessage(null);
    setExportResult(null);
    setRevealedStatus(null);

    try {
      const targetProfiles = selectedAllProfiles
        ? ['ALL']
        : selectedProfileIds.length > 0
        ? selectedProfileIds
        : ['ALL'];

      const response = await api.exportCsv({
        start_date: startDate,
        end_date: endDate,
        profile_ids: targetProfiles,
        include_report_metadata: includeMetadata,
        include_profile_summary: includeProfileSummary,
        include_url_activity: includeUrlActivity,
        include_screenshot_metadata: includeScreenshotMetadata,
        include_non_chrome_apps: includeNonChromeApps
      });

      setExportResult(response);
    } catch (err) {
      setErrorMessage((err as Error).message || 'Failed to generate CSV export.');
    } finally {
      setIsExporting(false);
    }
  };

  const handleDownloadFile = () => {
    if (!exportResult) return;
    const downloadUrl = api.getExportDownloadUrl(exportResult.file_name);
    const link = document.createElement('a');
    link.href = downloadUrl;
    link.download = exportResult.file_name;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  const handleRevealInExplorer = async () => {
    if (!exportResult) return;
    try {
      await api.revealExport(exportResult.file_path);
      setRevealedStatus('Export folder opened in Windows Explorer.');
      setTimeout(() => setRevealedStatus(null), 4000);
    } catch (err) {
      setRevealedStatus('Could not reveal in Explorer: ' + (err as Error).message);
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-6 shadow-sm">
        <div className="flex items-center space-x-3 mb-2">
          <div className="p-2.5 bg-indigo-950/80 border border-indigo-700/50 rounded-lg text-indigo-400">
            <FileSpreadsheet className="w-6 h-6" />
          </div>
          <div>
            <h2 className="text-xl font-bold text-white tracking-tight">Master CSV Report Export</h2>
            <p className="text-xs sm:text-sm text-slate-400">
              Generates a unified, offline RFC-4180 CSV export with UTF-8 BOM, profile separation, formula injection defense, and duration-only non-Chrome metrics.
            </p>
          </div>
        </div>

        {/* Privacy & Excel Compatibility Invariant Banner */}
        <div className="mt-4 bg-emerald-950/40 border border-emerald-800/40 rounded-lg p-3 text-xs text-emerald-300 flex items-start space-x-2.5">
          <Shield className="w-4 h-4 text-emerald-400 mt-0.5 flex-shrink-0" />
          <div>
            <span className="font-semibold text-emerald-200">Strict Privacy Invariant Enforced: </span>
            Non-Chrome application tracking strictly collects executable name and duration only. Zero window titles or document names are stored or exported. Values starting with formula characters (<code className="bg-emerald-900/50 px-1 py-0.5 rounded text-emerald-200">=, +, -, @</code>) are automatically neutralized.
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left 2 Cols: Form Controls */}
        <div className="lg:col-span-2 space-y-6">
          {/* Step 1: Date Range Selection */}
          <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 shadow-sm space-y-4">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <div className="flex items-center space-x-2">
                <Calendar className="w-4 h-4 text-indigo-400" />
                <h3 className="text-sm font-semibold text-slate-200 uppercase tracking-wider">1. Select Date Range</h3>
              </div>
              <div className="flex space-x-1.5 text-xs">
                <button
                  type="button"
                  onClick={() => setQuickRange(0)}
                  className="px-2.5 py-1 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded border border-slate-700 transition-colors"
                >
                  Today
                </button>
                <button
                  type="button"
                  onClick={() => setQuickRange(7)}
                  className="px-2.5 py-1 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded border border-slate-700 transition-colors"
                >
                  Last 7 Days
                </button>
                <button
                  type="button"
                  onClick={() => setQuickRange(30)}
                  className="px-2.5 py-1 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded border border-slate-700 transition-colors"
                >
                  Last 30 Days
                </button>
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-medium text-slate-400 mb-1.5">Start Date (Local)</label>
                <input
                  type="date"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  className="w-full bg-slate-800 border border-slate-700 text-slate-200 text-sm rounded-lg px-3 py-2 focus:ring-1 focus:ring-indigo-500 focus:outline-none"
                />
              </div>
              <div>
                <label className="block text-xs font-medium text-slate-400 mb-1.5">End Date (Local)</label>
                <input
                  type="date"
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                  className="w-full bg-slate-800 border border-slate-700 text-slate-200 text-sm rounded-lg px-3 py-2 focus:ring-1 focus:ring-indigo-500 focus:outline-none"
                />
              </div>
            </div>
            <p className="text-xs text-slate-500 italic">
              Sessions overlapping date boundaries are clipped cleanly to prevent artificial duration inflation.
            </p>
          </div>

          {/* Step 2: Profile Selection */}
          <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 shadow-sm space-y-4">
            <div className="flex items-center justify-between border-b border-slate-800 pb-3">
              <div className="flex items-center space-x-2">
                <Layers className="w-4 h-4 text-indigo-400" />
                <h3 className="text-sm font-semibold text-slate-200 uppercase tracking-wider">2. Chrome Profiles</h3>
              </div>
              <label className="flex items-center space-x-2 text-xs text-slate-300 cursor-pointer">
                <input
                  type="checkbox"
                  checked={selectedAllProfiles}
                  onChange={(e) => handleToggleAllProfiles(e.target.checked)}
                  className="rounded border-slate-700 bg-slate-800 text-indigo-600 focus:ring-0 w-3.5 h-3.5"
                />
                <span>Include All Profiles</span>
              </label>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
              {profiles.map((p) => {
                const isChecked = selectedAllProfiles || selectedProfileIds.includes(p.profileId);
                return (
                  <label
                    key={p.profileId}
                    className={`flex items-center space-x-3 p-3 rounded-lg border cursor-pointer transition-colors ${
                      isChecked
                        ? 'bg-slate-800/80 border-indigo-600/50 text-white'
                        : 'bg-slate-950/50 border-slate-800/80 text-slate-400 hover:bg-slate-800/30'
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={isChecked}
                      disabled={selectedAllProfiles}
                      onChange={() => handleToggleProfile(p.profileId)}
                      className="rounded border-slate-700 bg-slate-800 text-indigo-600 focus:ring-0 w-4 h-4"
                    />
                    <div className="min-w-0 flex-1">
                      <div className="text-xs font-medium truncate">{p.profileName}</div>
                      <div className="text-[10px] text-slate-500 font-mono truncate">{p.profileId}</div>
                    </div>
                    <span className={`text-[10px] px-1.5 py-0.5 rounded font-semibold ${
                      p.connectionStatus === 'CONNECTED' ? 'bg-emerald-950 text-emerald-400 border border-emerald-800/50' : 'bg-slate-800 text-slate-400'
                    }`}>
                      {p.connectionStatus}
                    </span>
                  </label>
                );
              })}
            </div>
          </div>

          {/* Step 3: Section Inclusions */}
          <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 shadow-sm space-y-4">
            <div className="flex items-center space-x-2 border-b border-slate-800 pb-3">
              <FileCheck className="w-4 h-4 text-indigo-400" />
              <h3 className="text-sm font-semibold text-slate-200 uppercase tracking-wider">3. Sections to Include</h3>
            </div>

            <div className="space-y-2.5">
              <label className="flex items-center space-x-3 p-2.5 rounded-lg bg-slate-950/50 border border-slate-800/80 cursor-pointer hover:bg-slate-800/30">
                <input
                  type="checkbox"
                  checked={includeMetadata}
                  onChange={(e) => setIncludeMetadata(e.target.checked)}
                  className="rounded border-slate-700 bg-slate-800 text-indigo-600 focus:ring-0 w-4 h-4"
                />
                <div>
                  <div className="text-xs font-medium text-slate-200">Section 1: Report Metadata & System Header</div>
                  <div className="text-[11px] text-slate-500">Generation timestamp, date range, timezone, and enrolled profile count</div>
                </div>
              </label>

              <label className="flex items-center space-x-3 p-2.5 rounded-lg bg-slate-950/50 border border-slate-800/80 cursor-pointer hover:bg-slate-800/30">
                <input
                  type="checkbox"
                  checked={includeProfileSummary}
                  onChange={(e) => setIncludeProfileSummary(e.target.checked)}
                  className="rounded border-slate-700 bg-slate-800 text-indigo-600 focus:ring-0 w-4 h-4"
                />
                <div>
                  <div className="text-xs font-medium text-slate-200">Section 2: Multi-Profile Summary Table</div>
                  <div className="text-[11px] text-slate-500">Aggregate active URL duration, Chrome foreground time, visit counts, and screenshot metrics</div>
                </div>
              </label>

              <label className="flex items-center space-x-3 p-2.5 rounded-lg bg-slate-950/50 border border-slate-800/80 cursor-pointer hover:bg-slate-800/30">
                <input
                  type="checkbox"
                  checked={includeUrlActivity}
                  onChange={(e) => setIncludeUrlActivity(e.target.checked)}
                  className="rounded border-slate-700 bg-slate-800 text-indigo-600 focus:ring-0 w-4 h-4"
                />
                <div>
                  <div className="text-xs font-medium text-slate-200">Section 3: Chronological Chrome Profile URL Sessions</div>
                  <div className="text-[11px] text-slate-500">Distinct subsections per profile: date, local start/end times, clipped duration, page title, URL, domain</div>
                </div>
              </label>

              <label className="flex items-center space-x-3 p-2.5 rounded-lg bg-slate-950/50 border border-slate-800/80 cursor-pointer hover:bg-slate-800/30">
                <input
                  type="checkbox"
                  checked={includeScreenshotMetadata}
                  onChange={(e) => setIncludeScreenshotMetadata(e.target.checked)}
                  className="rounded border-slate-700 bg-slate-800 text-indigo-600 focus:ring-0 w-4 h-4"
                />
                <div>
                  <div className="text-xs font-medium text-slate-200">Section 4: Screenshot Metadata Audit</div>
                  <div className="text-[11px] text-slate-500">Timestamp, profile name, active URL, local PNG disk path, capture status, and failure reasons</div>
                </div>
              </label>

              <label className="flex items-center space-x-3 p-2.5 rounded-lg bg-slate-950/50 border border-slate-800/80 cursor-pointer hover:bg-slate-800/30">
                <input
                  type="checkbox"
                  checked={includeNonChromeApps}
                  onChange={(e) => setIncludeNonChromeApps(e.target.checked)}
                  className="rounded border-slate-700 bg-slate-800 text-indigo-600 focus:ring-0 w-4 h-4"
                />
                <div>
                  <div className="text-xs font-medium text-slate-200">Section 5: Non-Chrome Application Usage</div>
                  <div className="text-[11px] text-emerald-400/90 font-mono">Executable name, friendly display name, and foreground duration ONLY (Zero window titles)</div>
                </div>
              </label>
            </div>
          </div>
        </div>

        {/* Right 1 Col: Action & Result Card */}
        <div className="space-y-6">
          <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 shadow-sm space-y-5">
            <h3 className="text-sm font-semibold text-slate-200 uppercase tracking-wider border-b border-slate-800 pb-3">
              Export Action
            </h3>

            <button
              type="button"
              onClick={handleGenerateCsv}
              disabled={isExporting}
              className={`w-full py-3 px-4 rounded-xl font-bold text-sm shadow-lg flex items-center justify-center space-x-2 transition-all ${
                isExporting
                  ? 'bg-indigo-700/60 text-indigo-200 cursor-not-allowed'
                  : 'bg-indigo-600 hover:bg-indigo-500 text-white active:scale-[0.98]'
              }`}
            >
              {isExporting ? (
                <>
                  <RefreshCw className="w-4 h-4 animate-spin" />
                  <span>Generating Master CSV...</span>
                </>
              ) : (
                <>
                  <Download className="w-4 h-4" />
                  <span>Generate Master CSV</span>
                </>
              )}
            </button>

            {errorMessage && (
              <div className="bg-rose-950/60 border border-rose-800/60 rounded-lg p-3 text-xs text-rose-300 flex items-start space-x-2">
                <AlertCircle className="w-4 h-4 text-rose-400 mt-0.5 flex-shrink-0" />
                <span>{errorMessage}</span>
              </div>
            )}

            {exportResult && (
              <div className="bg-slate-950 border border-slate-800 rounded-xl p-4 space-y-4">
                <div className="flex items-center space-x-2 text-emerald-400 text-xs font-bold">
                  <CheckCircle className="w-4 h-4" />
                  <span>CSV Report Generated Successfully!</span>
                </div>

                <div className="space-y-1.5 text-xs">
                  <div className="text-slate-400 font-medium">File Name:</div>
                  <div className="text-slate-200 font-mono text-[11px] bg-slate-900 p-2 rounded border border-slate-800 truncate" title={exportResult.file_name}>
                    {exportResult.file_name}
                  </div>

                  <div className="text-slate-400 font-medium mt-2">Saved On Disk:</div>
                  <div className="text-slate-300 font-mono text-[10px] bg-slate-900 p-2 rounded border border-slate-800 break-all select-all" title={exportResult.file_path}>
                    {exportResult.file_path}
                  </div>
                </div>

                {/* Record Counts */}
                <div className="grid grid-cols-2 gap-2 pt-2 border-t border-slate-800/80 text-xs">
                  <div className="bg-slate-900 p-2 rounded border border-slate-800">
                    <span className="text-slate-500 text-[10px] block">Profiles</span>
                    <span className="font-bold text-slate-200">{exportResult.record_counts.profiles_included}</span>
                  </div>
                  <div className="bg-slate-900 p-2 rounded border border-slate-800">
                    <span className="text-slate-500 text-[10px] block">URL Sessions</span>
                    <span className="font-bold text-slate-200">{exportResult.record_counts.url_sessions_exported}</span>
                  </div>
                  <div className="bg-slate-900 p-2 rounded border border-slate-800">
                    <span className="text-slate-500 text-[10px] block">Screenshots</span>
                    <span className="font-bold text-slate-200">{exportResult.record_counts.screenshot_records_exported}</span>
                  </div>
                  <div className="bg-slate-900 p-2 rounded border border-slate-800">
                    <span className="text-slate-500 text-[10px] block">App Sessions</span>
                    <span className="font-bold text-slate-200">{exportResult.record_counts.application_sessions_exported}</span>
                  </div>
                </div>

                {/* Actions */}
                <div className="pt-2 flex flex-col space-y-2">
                  <button
                    type="button"
                    onClick={handleDownloadFile}
                    className="w-full py-2 px-3 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg text-xs font-semibold flex items-center justify-center space-x-2 transition-colors"
                  >
                    <Download className="w-3.5 h-3.5" />
                    <span>Download File Directly</span>
                  </button>

                  <button
                    type="button"
                    onClick={handleRevealInExplorer}
                    className="w-full py-2 px-3 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-lg text-xs font-medium border border-slate-700 flex items-center justify-center space-x-2 transition-colors"
                  >
                    <FolderOpen className="w-3.5 h-3.5" />
                    <span>Reveal in Explorer</span>
                  </button>

                  {revealedStatus && (
                    <div className="text-[11px] text-emerald-400 text-center font-mono mt-1 animate-fade-in">
                      {revealedStatus}
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
