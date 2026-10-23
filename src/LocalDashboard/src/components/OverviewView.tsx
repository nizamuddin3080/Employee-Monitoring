import React from 'react';
import { Chrome, Globe, Layers, Users, Eye, Camera, AlertTriangle, CheckCircle2, PauseCircle } from 'lucide-react';
import { OverviewMetrics, ProfilesResponse } from '../api/client';

interface OverviewViewProps {
  metrics: OverviewMetrics | null;
  profilesData: ProfilesResponse | null;
  onNavigateToProfiles: () => void;
}

export const OverviewView: React.FC<OverviewViewProps> = ({
  metrics,
  profilesData,
  onNavigateToProfiles
}) => {
  const formatDuration = (seconds: number) => {
    if (!seconds || seconds <= 0) return '0m';
    const hrs = Math.floor(seconds / 3600);
    const mins = Math.floor((seconds % 3600) / 60);
    const secs = seconds % 60;

    if (hrs > 0) {
      return `${hrs}h ${mins}m`;
    }
    if (mins > 0) {
      return `${mins}m ${secs}s`;
    }
    return `${secs}s`;
  };

  const cards = [
    {
      title: 'Chrome Foreground Time',
      value: formatDuration(metrics?.chrome_foreground_seconds ?? 0),
      subtitle: "Total time Chrome was in OS foreground today",
      icon: Chrome,
      color: 'text-blue-400',
      bgColor: 'bg-blue-500/10',
      borderColor: 'border-blue-500/20'
    },
    {
      title: 'Active Tab URL Time',
      value: formatDuration(metrics?.active_url_seconds ?? 0),
      subtitle: "Foreground tab active browsing duration",
      icon: Globe,
      color: 'text-indigo-400',
      bgColor: 'bg-indigo-500/10',
      borderColor: 'border-indigo-500/20'
    },
    {
      title: 'External Apps Time',
      value: formatDuration(metrics?.non_chrome_app_seconds ?? 0),
      subtitle: "VS Code, Office, Slack, Terminal (zero window titles)",
      icon: Layers,
      color: 'text-purple-400',
      bgColor: 'bg-purple-500/10',
      borderColor: 'border-purple-500/20'
    },
    {
      title: 'Enrolled Chrome Profiles',
      value: metrics?.enrolled_profiles_count ?? 0,
      subtitle: "Profiles reporting to local SQLite store",
      icon: Users,
      color: 'text-emerald-400',
      bgColor: 'bg-emerald-500/10',
      borderColor: 'border-emerald-500/20'
    },
    {
      title: 'Total URL Visits',
      value: metrics?.total_url_visits ?? 0,
      subtitle: "Distinct active URL browsing sessions",
      icon: Eye,
      color: 'text-cyan-400',
      bgColor: 'bg-cyan-500/10',
      borderColor: 'border-cyan-500/20'
    },
    {
      title: 'Screenshots Captured',
      value: `${metrics?.screenshots_successful ?? 0} ok`,
      subtitle: `${metrics?.screenshots_failed ?? 0} honest failures recorded`,
      icon: Camera,
      color: 'text-amber-400',
      bgColor: 'bg-amber-500/10',
      borderColor: 'border-amber-500/20'
    }
  ];

  return (
    <div className="space-y-6">
      {/* Tracking State Banner */}
      {metrics?.is_tracking_paused ? (
        <div className="bg-amber-500/10 border border-amber-500/30 rounded-lg p-4 flex items-center justify-between text-amber-200">
          <div className="flex items-center space-x-3">
            <PauseCircle className="w-5 h-5 text-amber-400 shrink-0" />
            <div>
              <p className="text-sm font-semibold">Activity Tracking is Currently Paused</p>
              <p className="text-xs text-amber-300/80">Windows session is locked or user has manually paused tracking. Zero duration is currently being accrued.</p>
            </div>
          </div>
        </div>
      ) : (
        <div className="bg-emerald-500/10 border border-emerald-500/30 rounded-lg p-3.5 flex items-center justify-between text-emerald-200">
          <div className="flex items-center space-x-2.5">
            <CheckCircle2 className="w-5 h-5 text-emerald-400 shrink-0" />
            <p className="text-xs sm:text-sm font-medium">Tracking is active in Windows session &bull; Database in WAL mode &bull; Localhost only</p>
          </div>
          {metrics?.last_event_time_utc && (
            <span className="text-xs font-mono text-emerald-300/70 hidden sm:inline">
              Last event: {new Date(metrics.last_event_time_utc).toLocaleTimeString()}
            </span>
          )}
        </div>
      )}

      {/* Honest Coverage Warning: Unmonitored Chrome Profiles Detected */}
      {profilesData?.has_unmonitored_profiles && (
        <div className="bg-red-500/10 border border-red-500/30 rounded-lg p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3 text-red-200">
          <div className="flex items-start sm:items-center space-x-3">
            <AlertTriangle className="w-5 h-5 text-red-400 shrink-0 mt-0.5 sm:mt-0" />
            <div>
              <p className="text-sm font-semibold">Honest Coverage Warning: Unmonitored Chrome Profiles Detected</p>
              <p className="text-xs text-red-300/80">
                Found {profilesData.unmonitored_profiles.length} Chrome profile(s) on disk without the extension enrolled. Activity in those profiles is NOT tracked.
              </p>
            </div>
          </div>
          <button
            onClick={onNavigateToProfiles}
            className="text-xs font-semibold px-3 py-1.5 bg-red-600 hover:bg-red-500 text-white rounded-md transition-colors shrink-0 self-start sm:self-auto"
          >
            Review Profiles
          </button>
        </div>
      )}

      {/* KPI Cards Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
        {cards.map((card, idx) => {
          const Icon = card.icon;
          return (
            <div
              key={idx}
              className={`p-5 rounded-xl border ${card.borderColor} ${card.bgColor} backdrop-blur-sm shadow-sm flex flex-col justify-between`}
            >
              <div className="flex items-center justify-between">
                <span className="text-xs font-semibold uppercase tracking-wider text-slate-400">{card.title}</span>
                <div className={`p-2 rounded-lg bg-slate-800/80 ${card.color}`}>
                  <Icon className="w-5 h-5" />
                </div>
              </div>
              <div className="mt-4">
                <div className="text-2xl sm:text-3xl font-extrabold tracking-tight text-white">{card.value}</div>
                <p className="text-xs text-slate-400 mt-1">{card.subtitle}</p>
              </div>
            </div>
          );
        })}
      </div>

      {/* Architecture & Privacy Invariant Overview */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-5 text-slate-300 text-xs sm:text-sm space-y-2">
        <h3 className="font-semibold text-white text-sm">Strict Privacy & Offline Invariants</h3>
        <ul className="list-disc list-inside space-y-1 text-slate-400">
          <li><strong>Local-first:</strong> Zero telemetry, zero cloud uploads, zero remote databases. Operates 100% offline.</li>
          <li><strong>Zero window titles:</strong> External application tracking records strictly process name and duration. Never reads window titles, document filenames, or keystrokes.</li>
          <li><strong>No desktop capture:</strong> Screenshots are captured strictly via Chrome's visible-tab extension API when Chrome is in the OS foreground. Never captures desktop or other apps.</li>
          <li><strong>Query parameter sanitization:</strong> Passwords, authentication tokens, API keys, and session cookies are redacted before persistence.</li>
        </ul>
      </div>
    </div>
  );
};
