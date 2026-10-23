import React, { useState, useEffect } from 'react';
import { Navbar } from './components/Navbar';
import { OverviewView } from './components/OverviewView';
import { ActivityView } from './components/ActivityView';
import { ApplicationsView } from './components/ApplicationsView';
import { ScreenshotsView } from './components/ScreenshotsView';
import { ProfilesView } from './components/ProfilesView';
import { ExportView } from './components/ExportView';
import { SettingsView } from './components/SettingsView';
import { api, OverviewMetrics, ProfilesResponse } from './api/client';

export const App: React.FC = () => {
  const [activeTab, setActiveTab] = useState('overview');
  const [metrics, setMetrics] = useState<OverviewMetrics | null>(null);
  const [profilesData, setProfilesData] = useState<ProfilesResponse | null>(null);
  const [isCapturing, setIsCapturing] = useState(false);
  const [toastMessage, setToastMessage] = useState<string | null>(null);

  const showToast = (msg: string) => {
    setToastMessage(msg);
    setTimeout(() => setToastMessage(null), 3500);
  };

  const loadData = async () => {
    try {
      const [m, p] = await Promise.all([
        api.getOverview().catch(() => null),
        api.getProfiles().catch(() => null)
      ]);
      if (m) setMetrics(m);
      if (p) setProfilesData(p);
    } catch {
      // Suppress network errors
    }
  };

  useEffect(() => {
    loadData();
    const timer = setInterval(loadData, 10000);
    return () => clearInterval(timer);
  }, []);

  const handleToggleTracking = async () => {
    if (!metrics) return;
    try {
      if (metrics.is_tracking_paused) {
        await api.resumeTracking();
        showToast('Tracking resumed across all enrolled profiles.');
      } else {
        await api.pauseTracking();
        showToast('Tracking paused. Zero duration will be accrued.');
      }
      loadData();
    } catch (err) {
      alert('Failed to change tracking state: ' + (err as Error).message);
    }
  };

  const handleTriggerCapture = async () => {
    setIsCapturing(true);
    try {
      const res = await api.triggerCapture();
      if (res && res.capture_status === 'SUCCESS') {
        showToast('Visible tab screenshot captured successfully!');
      } else if (res && res.failure_reason) {
        showToast(`Capture completed: ${res.failure_reason}`);
      }
      loadData();
    } catch (err) {
      showToast('Capture error: ' + (err as Error).message);
    } finally {
      setIsCapturing(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col font-sans selection:bg-indigo-600 selection:text-white">
      <Navbar
        activeTab={activeTab}
        onSelectTab={setActiveTab}
        isTrackingPaused={metrics?.is_tracking_paused ?? false}
        onToggleTracking={handleToggleTracking}
        onTriggerCapture={handleTriggerCapture}
        isCapturing={isCapturing}
      />

      {/* Main Content Area */}
      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-6">
        {activeTab === 'overview' && (
          <OverviewView
            metrics={metrics}
            profilesData={profilesData}
            onNavigateToProfiles={() => setActiveTab('profiles')}
          />
        )}
        {activeTab === 'activity' && (
          <ActivityView profiles={profilesData?.enrolled_profiles ?? []} />
        )}
        {activeTab === 'applications' && <ApplicationsView />}
        {activeTab === 'screenshots' && (
          <ScreenshotsView profiles={profilesData?.enrolled_profiles ?? []} />
        )}
        {activeTab === 'profiles' && (
          <ProfilesView
            enrolledProfiles={profilesData?.enrolled_profiles ?? []}
            unmonitoredProfiles={profilesData?.unmonitored_profiles ?? []}
            onRefresh={loadData}
          />
        )}
        {activeTab === 'export' && (
          <ExportView profiles={profilesData?.enrolled_profiles ?? []} />
        )}
        {activeTab === 'settings' && (
          <SettingsView
            isTrackingPaused={metrics?.is_tracking_paused ?? false}
            onToggleTracking={handleToggleTracking}
          />
        )}
      </main>

      {/* Toast Notification */}
      {toastMessage && (
        <div className="fixed bottom-5 right-5 z-50 bg-slate-800 border border-slate-700 text-white text-xs sm:text-sm px-4 py-2.5 rounded-xl shadow-xl flex items-center space-x-2 animate-bounce">
          <span>{toastMessage}</span>
        </div>
      )}

      {/* Footer */}
      <footer className="border-t border-slate-900 bg-slate-950 py-4 text-center text-xs text-slate-500 font-mono">
        Local Activity Tracker &bull; Windows 10/11 &bull; SQLite WAL &bull; Manifest V3 &bull; All data stays on this device
      </footer>
    </div>
  );
};
