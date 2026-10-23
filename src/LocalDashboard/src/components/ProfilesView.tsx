import React, { useState } from 'react';
import { Users, CheckCircle, AlertTriangle, Edit2, Save, X, Chrome, ShieldAlert, PlusCircle } from 'lucide-react';
import { api, ChromeProfileRecord, UnmonitoredProfile } from '../api/client';

interface ProfilesViewProps {
  enrolledProfiles: ChromeProfileRecord[];
  unmonitoredProfiles: UnmonitoredProfile[];
  onRefresh: () => void;
}

export const ProfilesView: React.FC<ProfilesViewProps> = ({
  enrolledProfiles,
  unmonitoredProfiles,
  onRefresh
}) => {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const [isSaving, setIsSaving] = useState(false);

  const startEditing = (p: ChromeProfileRecord) => {
    setEditingId(p.profileId);
    setEditName(p.profileName);
  };

  const handleSave = async (id: string) => {
    if (!editName.trim()) return;
    setIsSaving(true);
    try {
      await api.updateProfileName(id, editName.trim());
      setEditingId(null);
      onRefresh();
    } catch (err) {
      alert('Failed to update profile name: ' + (err as Error).message);
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Honest Coverage Warning if Unmonitored Profiles Exist */}
      {unmonitoredProfiles.length > 0 && (
        <div className="bg-red-500/10 border border-red-500/20 rounded-xl p-5 text-red-200 space-y-3">
          <div className="flex items-center space-x-3">
            <ShieldAlert className="w-6 h-6 text-red-400 shrink-0" />
            <div>
              <h3 className="text-base font-bold text-white">Incomplete Coverage Warning: {unmonitoredProfiles.length} Unmonitored Profile(s) Detected</h3>
              <p className="text-xs text-red-300/80">
                The filesystem scanner detected existing Google Chrome profiles on this computer where the extension has not been enrolled.
                In accordance with project integrity requirements, activity in these profiles is NOT monitored.
              </p>
            </div>
          </div>

          <div className="bg-slate-900/60 rounded-lg p-3 divide-y divide-slate-800 text-xs">
            {unmonitoredProfiles.map((u) => (
              <div key={u.profileDirectoryName} className="py-2 flex items-center justify-between first:pt-0 last:pb-0">
                <div className="flex items-center space-x-2">
                  <Chrome className="w-4 h-4 text-slate-500" />
                  <span className="font-semibold text-white">{u.displayName || u.profileDirectoryName}</span>
                  <span className="text-slate-400 font-mono">({u.profileDirectoryName})</span>
                  {u.email && <span className="text-slate-400">&bull; {u.email}</span>}
                </div>
                <span className="px-2 py-0.5 rounded text-[10px] font-bold uppercase tracking-wider bg-red-500/20 text-red-400 border border-red-500/30">
                  Unenrolled
                </span>
              </div>
            ))}
          </div>

          <div className="text-xs text-red-300/80 bg-red-950/40 p-3 rounded-lg border border-red-900/40">
            <strong>Enrollment Guide:</strong> Open each unmonitored profile in Chrome &rarr; Navigate to <code>chrome://extensions</code> &rarr; Enable Developer Mode &rarr; Click <em>"Load unpacked"</em> &rarr; Select <code>src/ChromeExtension/dist</code> &rarr; Click the extension icon in the toolbar to confirm enrollment.
          </div>
        </div>
      )}

      {/* Enrolled Profiles Card */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl overflow-hidden shadow-sm">
        <div className="p-4 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <Users className="w-4 h-4 text-emerald-400" />
            <h2 className="text-sm font-bold text-white">Enrolled Chrome Profiles ({enrolledProfiles.length})</h2>
          </div>
          <span className="text-xs text-slate-400">All profiles write to unified local SQLite database</span>
        </div>

        {enrolledProfiles.length === 0 ? (
          <div className="p-8 text-center text-slate-500 text-sm">
            No Chrome profiles enrolled yet. Please load the extension unpacked in your Chrome profile to enroll.
          </div>
        ) : (
          <div className="divide-y divide-slate-800">
            {enrolledProfiles.map((p) => {
              const isEditing = editingId === p.profileId;
              const isConnected = p.connectionStatus === 'CONNECTED';

              return (
                <div key={p.profileId} className="p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3 hover:bg-slate-800/30 transition-colors">
                  <div className="flex items-center space-x-3">
                    <div className="p-2 rounded-lg bg-slate-800 text-indigo-400 border border-slate-700">
                      <Chrome className="w-5 h-5" />
                    </div>
                    <div>
                      {isEditing ? (
                        <div className="flex items-center space-x-2">
                          <input
                            type="text"
                            value={editName}
                            onChange={(e) => setEditName(e.target.value)}
                            className="bg-slate-800 border border-slate-600 rounded px-2 py-1 text-xs text-white focus:outline-none focus:border-indigo-500"
                            autoFocus
                          />
                          <button
                            onClick={() => handleSave(p.profileId)}
                            disabled={isSaving}
                            className="p-1 rounded bg-indigo-600 hover:bg-indigo-500 text-white"
                          >
                            <Save className="w-3.5 h-3.5" />
                          </button>
                          <button
                            onClick={() => setEditingId(null)}
                            className="p-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-400"
                          >
                            <X className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      ) : (
                        <div className="flex items-center space-x-2">
                          <span className="text-sm font-bold text-white">{p.profileName}</span>
                          <button
                            onClick={() => startEditing(p)}
                            title="Edit profile display name"
                            className="text-slate-500 hover:text-slate-300 p-0.5"
                          >
                            <Edit2 className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      )}
                      <p className="text-xs text-slate-400 font-mono mt-0.5">
                        ID: {p.profileId}
                      </p>
                    </div>
                  </div>

                  <div className="flex items-center space-x-4 self-end sm:self-auto">
                    <div className="text-right text-xs font-mono text-slate-400">
                      <div>Last seen: {new Date(p.lastSeenUtc).toLocaleTimeString()}</div>
                      <div className="text-[11px] text-slate-500">{new Date(p.lastSeenUtc).toLocaleDateString()}</div>
                    </div>

                    <span
                      className={`px-2.5 py-1 rounded-full text-xs font-semibold tracking-wide flex items-center space-x-1.5 ${
                        isConnected
                          ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                          : 'bg-slate-800 text-slate-400 border border-slate-700'
                      }`}
                    >
                      <span className={`w-2 h-2 rounded-full ${isConnected ? 'bg-emerald-400 animate-pulse' : 'bg-slate-500'}`} />
                      <span>{p.connectionStatus}</span>
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
};
