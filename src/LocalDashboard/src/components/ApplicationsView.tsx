import React, { useState, useEffect } from 'react';
import { Layers, Clock, ShieldCheck, ChevronDown, ChevronRight, Laptop } from 'lucide-react';
import { api, ApplicationUsageGroup } from '../api/client';

export const ApplicationsView: React.FC = () => {
  const [apps, setApps] = useState<ApplicationUsageGroup[]>([]);
  const [expandedApp, setExpandedApp] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    const fetchApps = async () => {
      setIsLoading(true);
      try {
        const data = await api.getApplications();
        setApps(data);
      } catch (err) {
        console.error('Failed to load application usage data:', err);
      } finally {
        setIsLoading(false);
      }
    };

    fetchApps();
  }, []);

  const formatDuration = (seconds: number) => {
    if (!seconds || seconds <= 0) return '0s';
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

  const totalAppSeconds = apps.reduce((sum, a) => sum + a.total_duration_seconds, 0);

  return (
    <div className="space-y-4">
      {/* Privacy Notice Banner */}
      <div className="bg-purple-500/10 border border-purple-500/20 rounded-xl p-4 flex items-center justify-between text-purple-200">
        <div className="flex items-center space-x-3">
          <ShieldCheck className="w-5 h-5 text-purple-400 shrink-0" />
          <div>
            <h3 className="text-sm font-semibold text-white">Strict Privacy Guarantee: Zero Window Titles Recorded</h3>
            <p className="text-xs text-purple-300/80">
              For applications other than Google Chrome, Local Activity Tracker strictly records process executable names and foreground duration.
              Window titles, document names, spreadsheets, emails, and keystrokes are completely omitted.
            </p>
          </div>
        </div>
      </div>

      {/* Applications List */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden shadow-sm">
        <div className="p-4 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <Laptop className="w-4 h-4 text-purple-400" />
            <h2 className="text-sm font-bold text-white">Desktop Applications Tracked Today</h2>
          </div>
          <span className="text-xs font-mono text-purple-300 font-semibold">
            Total: {formatDuration(totalAppSeconds)} ({apps.length} apps)
          </span>
        </div>

        {isLoading ? (
          <div className="p-8 text-center text-slate-500 text-sm">Loading application usage...</div>
        ) : apps.length === 0 ? (
          <div className="p-8 text-center text-slate-500 text-sm">
            No non-Chrome application usage sessions recorded for today.
          </div>
        ) : (
          <div className="divide-y divide-slate-800">
            {apps.map((app) => {
              const isExpanded = expandedApp === app.process_name;
              const percentage = totalAppSeconds > 0 ? (app.total_duration_seconds / totalAppSeconds) * 100 : 0;

              return (
                <div key={app.process_name} className="p-4 hover:bg-slate-800/30 transition-colors">
                  <div
                    onClick={() => setExpandedApp(isExpanded ? null : app.process_name)}
                    className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 cursor-pointer select-none"
                  >
                    <div className="flex items-center space-x-3">
                      <div className="p-2 rounded-lg bg-slate-800 text-purple-400 shrink-0 border border-slate-700">
                        {isExpanded ? <ChevronDown className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />}
                      </div>
                      <div>
                        <div className="text-sm font-semibold text-white flex items-center space-x-2">
                          <span>{app.application_display_name}</span>
                          <span className="text-xs font-mono text-slate-400 font-normal">({app.process_name})</span>
                        </div>
                        <p className="text-xs text-slate-400">
                          {app.session_count} session{app.session_count === 1 ? '' : 's'}
                        </p>
                      </div>
                    </div>

                    <div className="flex items-center space-x-4 self-end sm:self-auto">
                      <div className="w-32 hidden md:block">
                        <div className="h-2 w-full bg-slate-800 rounded-full overflow-hidden">
                          <div
                            className="h-full bg-purple-500 rounded-full"
                            style={{ width: `${Math.max(percentage, 2)}%` }}
                          />
                        </div>
                      </div>
                      <div className="text-right">
                        <span className="text-sm font-mono font-bold text-purple-300">
                          {formatDuration(app.total_duration_seconds)}
                        </span>
                        <div className="text-[11px] text-slate-400 font-mono">
                          {percentage.toFixed(1)}% of app time
                        </div>
                      </div>
                    </div>
                  </div>

                  {/* Expanded Session Details */}
                  {isExpanded && (
                    <div className="mt-3.5 pl-11 pr-2 pt-3 border-t border-slate-800/60">
                      <h4 className="text-xs font-semibold uppercase text-slate-400 mb-2">Individual Session Intervals</h4>
                      <div className="space-y-1.5 max-h-48 overflow-y-auto pr-1">
                        {app.sessions.map((s) => (
                          <div
                            key={s.session_id}
                            className="flex items-center justify-between text-xs py-1 px-2.5 rounded bg-slate-800/50 text-slate-300 font-mono"
                          >
                            <div>
                              <span>{new Date(s.start_time_utc).toLocaleTimeString()}</span>
                              <span className="text-slate-500 mx-2">&rarr;</span>
                              <span>{s.end_time_utc ? new Date(s.end_time_utc).toLocaleTimeString() : 'Active'}</span>
                            </div>
                            <span className="font-semibold text-purple-400">{formatDuration(s.duration_seconds)}</span>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
};
