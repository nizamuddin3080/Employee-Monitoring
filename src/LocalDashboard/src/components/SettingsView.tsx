import React, { useState, useEffect } from 'react';
import { Settings, Save, ShieldCheck, HardDrive, Key, Check, Copy, Database, Trash2, Archive, AlertTriangle } from 'lucide-react';
import { api } from '../api/client';

interface SettingsViewProps {
  isTrackingPaused: boolean;
  onToggleTracking: () => void;
}

export const SettingsView: React.FC<SettingsViewProps> = ({
  isTrackingPaused,
  onToggleTracking
}) => {
  const [screenshotInterval, setScreenshotInterval] = useState('600');
  const [retentionDays, setRetentionDays] = useState('90');
  const [capabilityToken, setCapabilityToken] = useState('');
  const [isCopied, setIsCopied] = useState(false);
  const [isSaved, setIsSaved] = useState(false);
  const [isLoading, setIsLoading] = useState(false);

  // Maintenance state
  const [backupStatus, setBackupStatus] = useState<string | null>(null);
  const [purgeStatus, setPurgeStatus] = useState<string | null>(null);
  const [isBackingUp, setIsBackingUp] = useState(false);
  const [isPurging, setIsPurging] = useState(false);

  useEffect(() => {
    const loadSettings = async () => {
      try {
        const settings = await api.getSettings();
        if (settings.screenshot_interval_seconds) {
          setScreenshotInterval(settings.screenshot_interval_seconds);
        }
        if (settings.retention_days) {
          setRetentionDays(settings.retention_days);
        }

        const tokenRes = await fetch('/api/auth/token');
        if (tokenRes.ok) {
          const tData = await tokenRes.json();
          setCapabilityToken(tData.token || '');
        }
      } catch (err) {
        console.error('Failed to load settings:', err);
      }
    };

    loadSettings();
  }, []);

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);
    try {
      await api.saveSettings({
        screenshot_interval_seconds: screenshotInterval,
        retention_days: retentionDays
      });
      setIsSaved(true);
      setTimeout(() => setIsSaved(false), 2500);
    } catch (err) {
      alert('Failed to save settings: ' + (err as Error).message);
    } finally {
      setIsLoading(false);
    }
  };

  const copyToken = () => {
    navigator.clipboard.writeText(capabilityToken);
    setIsCopied(true);
    setTimeout(() => setIsCopied(false), 2000);
  };

  const handleCreateBackup = async () => {
    setIsBackingUp(true);
    setBackupStatus(null);
    try {
      const res = await api.createBackup();
      setBackupStatus(`Backup created: ${res.file_name}`);
      setTimeout(() => setBackupStatus(null), 5000);
    } catch (err) {
      setBackupStatus(`Backup error: ${(err as Error).message}`);
    } finally {
      setIsBackingUp(false);
    }
  };

  const handlePurgeOldRecords = async () => {
    const days = parseInt(retentionDays, 10) || 30;
    if (!window.confirm(`Are you sure you want to purge records older than ${days} days? Physical screenshot files will be unlinked from disk.`)) {
      return;
    }
    setIsPurging(true);
    setPurgeStatus(null);
    try {
      const res = await api.purgeData({ older_than_days: days });
      setPurgeStatus(`Purged ${res.deleted_rows} old records.`);
      setTimeout(() => setPurgeStatus(null), 5000);
    } catch (err) {
      setPurgeStatus(`Purge error: ${(err as Error).message}`);
    } finally {
      setIsPurging(false);
    }
  };

  const handlePurgeAll = async () => {
    if (!window.confirm('DANGER: This will permanently erase ALL activity records, application sessions, and local screenshots. Click OK to proceed.') || !window.confirm('Confirm final erasure of all local data?')) {
      return;
    }
    setIsPurging(true);
    setPurgeStatus(null);
    try {
      await api.purgeData({ purge_all: true });
      setPurgeStatus('All tracking data and screenshot files have been completely erased.');
      setTimeout(() => setPurgeStatus(null), 5000);
    } catch (err) {
      setPurgeStatus(`Purge error: ${(err as Error).message}`);
    } finally {
      setIsPurging(false);
    }
  };

  return (
    <div className="space-y-6 max-w-4xl">
      {/* Settings Form */}
      <form onSubmit={handleSave} className="bg-slate-900 border border-slate-800 rounded-xl p-6 shadow-sm space-y-6">
        <div className="flex items-center space-x-2 border-b border-slate-800 pb-4">
          <Settings className="w-5 h-5 text-indigo-400" />
          <h2 className="text-base font-bold text-white">Application Settings & Configuration</h2>
        </div>

        <div className="space-y-4">
          {/* Screenshot Interval */}
          <div>
            <label className="block text-sm font-semibold text-white mb-1">
              Visible Tab Screenshot Capture Interval
            </label>
            <p className="text-xs text-slate-400 mb-2">
              Default is 10 minutes (600s). Minimum is 60 seconds. Screenshots are captured only when Chrome is in the OS foreground.
            </p>
            <div className="flex items-center space-x-3">
              <input
                type="range"
                min="60"
                max="3600"
                step="60"
                value={screenshotInterval}
                onChange={(e) => setScreenshotInterval(e.target.value)}
                className="w-64 accent-indigo-600"
              />
              <span className="font-mono text-sm font-bold text-indigo-400 bg-slate-800 px-2.5 py-1 rounded border border-slate-700">
                {Math.round(parseInt(screenshotInterval, 10) / 60)} minutes ({screenshotInterval}s)
              </span>
            </div>
          </div>

          {/* Retention Policy */}
          <div className="pt-3 border-t border-slate-800/80">
            <label className="block text-sm font-semibold text-white mb-1">
              Local Data & Screenshot Retention Period
            </label>
            <p className="text-xs text-slate-400 mb-2">
              Records and screenshots older than this retention period are safely purged during daily database maintenance.
            </p>
            <select
              value={retentionDays}
              onChange={(e) => setRetentionDays(e.target.value)}
              className="bg-slate-800 border border-slate-700 text-slate-100 text-xs sm:text-sm px-3 py-2 rounded-lg focus:outline-none focus:border-indigo-500 w-64"
            >
              <option value="30">30 Days</option>
              <option value="60">60 Days</option>
              <option value="90">90 Days (Recommended)</option>
              <option value="180">180 Days (6 Months)</option>
              <option value="365">365 Days (1 Year)</option>
            </select>
          </div>

          {/* Tracking Master Switch */}
          <div className="pt-3 border-t border-slate-800/80 flex items-center justify-between">
            <div>
              <span className="text-sm font-semibold text-white block">Global Monitoring Switch</span>
              <p className="text-xs text-slate-400">
                Instantly pause or resume foreground monitoring and screenshot captures across all enrolled profiles.
              </p>
            </div>
            <button
              type="button"
              onClick={onToggleTracking}
              className={`px-4 py-2 rounded-lg text-xs font-bold transition-colors ${
                isTrackingPaused
                  ? 'bg-amber-600 hover:bg-amber-500 text-white'
                  : 'bg-emerald-600 hover:bg-emerald-500 text-white'
              }`}
            >
              {isTrackingPaused ? 'Resume Tracking' : 'Active (Tracking)'}
            </button>
          </div>
        </div>

        <div className="pt-4 border-t border-slate-800 flex items-center justify-end space-x-3">
          {isSaved && (
            <span className="text-xs text-emerald-400 font-semibold flex items-center space-x-1">
              <Check className="w-3.5 h-3.5" />
              <span>Settings saved!</span>
            </span>
          )}
          <button
            type="submit"
            disabled={isLoading}
            className="flex items-center space-x-1.5 px-4 py-2 rounded-lg text-xs font-semibold bg-indigo-600 hover:bg-indigo-500 text-white shadow transition-colors"
          >
            <Save className="w-4 h-4" />
            <span>Save Settings</span>
          </button>
        </div>
      </form>

      {/* Local Capability Token */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-6 shadow-sm space-y-3">
        <div className="flex items-center space-x-2">
          <Key className="w-5 h-5 text-amber-400" />
          <h2 className="text-sm font-bold text-white">Cryptographic Capability Bearer Token (auth.token)</h2>
        </div>
        <p className="text-xs text-slate-400">
          This 256-bit token protects all mutation and screenshot endpoints on <code>127.0.0.1:48120</code>. It is stored on disk with restricted NTFS ACLs at:
          <br />
          <code className="text-amber-300 font-mono text-[11px]">%LOCALAPPDATA%\LocalActivityTracker\config\auth.token</code>
        </p>

        <div className="flex items-center space-x-2 bg-slate-950 p-2.5 rounded-lg border border-slate-800">
          <input
            type="password"
            readOnly
            value={capabilityToken}
            className="bg-transparent border-none text-xs font-mono text-slate-300 flex-1 focus:outline-none"
          />
          <button
            type="button"
            onClick={copyToken}
            className="flex items-center space-x-1 px-2.5 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs transition-colors shrink-0"
          >
            {isCopied ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
            <span>{isCopied ? 'Copied' : 'Copy'}</span>
          </button>
        </div>
      </div>

      {/* Database Maintenance & Safe Purge (TASK-066) */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-6 shadow-sm space-y-4">
        <div className="flex items-center space-x-2 border-b border-slate-800 pb-3">
          <Database className="w-5 h-5 text-indigo-400" />
          <h2 className="text-sm font-bold text-white">Database Maintenance & Safe Data Purge</h2>
        </div>

        <p className="text-xs text-slate-400">
          Create live SQLite backups using zero-locking online backups (<code>VACUUM INTO</code>), or securely purge expired data. Unlinked screenshot files are safely removed from disk before database records are deleted.
        </p>

        <div className="flex flex-wrap gap-3 pt-2">
          <button
            type="button"
            onClick={handleCreateBackup}
            disabled={isBackingUp}
            className="flex items-center space-x-2 px-3.5 py-2 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg text-xs font-semibold shadow transition-colors"
          >
            <Archive className="w-3.5 h-3.5" />
            <span>{isBackingUp ? 'Creating Backup...' : 'Create Online Backup'}</span>
          </button>

          <button
            type="button"
            onClick={handlePurgeOldRecords}
            disabled={isPurging}
            className="flex items-center space-x-2 px-3.5 py-2 bg-slate-800 hover:bg-slate-700 text-amber-300 border border-slate-700 rounded-lg text-xs font-medium transition-colors"
          >
            <Trash2 className="w-3.5 h-3.5" />
            <span>Purge Records Older Than {retentionDays} Days</span>
          </button>

          <button
            type="button"
            onClick={handlePurgeAll}
            disabled={isPurging}
            className="flex items-center space-x-2 px-3.5 py-2 bg-rose-950/80 hover:bg-rose-900 text-rose-300 border border-rose-800/60 rounded-lg text-xs font-semibold transition-colors"
          >
            <AlertTriangle className="w-3.5 h-3.5" />
            <span>Purge All Tracking Data</span>
          </button>
        </div>

        {backupStatus && (
          <div className="p-2.5 bg-emerald-950/60 border border-emerald-800/60 rounded-lg text-xs text-emerald-300 font-mono">
            {backupStatus}
          </div>
        )}

        {purgeStatus && (
          <div className="p-2.5 bg-amber-950/60 border border-amber-800/60 rounded-lg text-xs text-amber-300 font-mono">
            {purgeStatus}
          </div>
        )}
      </div>

      {/* Architecture & Offline Verification */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-6 shadow-sm space-y-3">
        <div className="flex items-center space-x-2">
          <ShieldCheck className="w-5 h-5 text-emerald-400" />
          <h2 className="text-sm font-bold text-white">Local-Only Security & Offline Architecture</h2>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs text-slate-300">
          <div className="p-3 rounded-lg bg-slate-800/40 border border-slate-800">
            <strong>Network Binding:</strong> Loopback (127.0.0.1) only
          </div>
          <div className="p-3 rounded-lg bg-slate-800/40 border border-slate-800">
            <strong>Storage Engine:</strong> SQLite WAL mode with 5s busy timeout
          </div>
          <div className="p-3 rounded-lg bg-slate-800/40 border border-slate-800">
            <strong>CSRF Defense:</strong> Required X-LAT-Requested-With header
          </div>
          <div className="p-3 rounded-lg bg-slate-800/40 border border-slate-800">
            <strong>External Telemetry:</strong> Strictly 0 remote calls
          </div>
        </div>
      </div>
    </div>
  );
};
