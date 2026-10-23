import React from 'react';
import { Activity, Clock, Layers, Camera, Users, Settings, Play, Pause, RefreshCw, ShieldCheck, FileSpreadsheet } from 'lucide-react';

interface NavbarProps {
  activeTab: string;
  onSelectTab: (tab: string) => void;
  isTrackingPaused: boolean;
  onToggleTracking: () => void;
  onTriggerCapture: () => void;
  isCapturing: boolean;
}

export const Navbar: React.FC<NavbarProps> = ({
  activeTab,
  onSelectTab,
  isTrackingPaused,
  onToggleTracking,
  onTriggerCapture,
  isCapturing
}) => {
  const tabs = [
    { id: 'overview', label: 'Overview', icon: Clock },
    { id: 'activity', label: 'Chrome Activity', icon: Activity },
    { id: 'applications', label: 'App Usage', icon: Layers },
    { id: 'screenshots', label: 'Screenshots', icon: Camera },
    { id: 'profiles', label: 'Profiles', icon: Users },
    { id: 'export', label: 'Export CSV', icon: FileSpreadsheet },
    { id: 'settings', label: 'Settings', icon: Settings }
  ];

  return (
    <header className="bg-slate-900 border-b border-slate-800 sticky top-0 z-40 text-slate-100">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 flex items-center justify-between h-16">
        <div className="flex items-center space-x-3">
          <div className="bg-indigo-600 p-2 rounded-lg text-white shadow-md">
            <ShieldCheck className="w-5 h-5" />
          </div>
          <div>
            <h1 className="text-base font-bold tracking-tight text-white leading-tight">Local Activity Tracker</h1>
            <p className="text-xs text-slate-400 font-mono">100% Offline &bull; 127.0.0.1:48120</p>
          </div>
        </div>

        {/* Navigation Tabs */}
        <nav className="flex space-x-1 sm:space-x-2">
          {tabs.map((tab) => {
            const Icon = tab.icon;
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => onSelectTab(tab.id)}
                className={`flex items-center space-x-1.5 px-3 py-1.5 rounded-md text-xs sm:text-sm font-medium transition-colors ${
                  isActive
                    ? 'bg-indigo-600 text-white shadow-sm'
                    : 'text-slate-300 hover:text-white hover:bg-slate-800'
                }`}
              >
                <Icon className="w-4 h-4" />
                <span className="hidden md:inline">{tab.label}</span>
              </button>
            );
          })}
        </nav>

        {/* Global Action Controls */}
        <div className="flex items-center space-x-2">
          <button
            onClick={onTriggerCapture}
            disabled={isCapturing || isTrackingPaused}
            title="Trigger immediate visible tab capture on active Chrome profile"
            className={`flex items-center space-x-1 px-2.5 py-1.5 rounded-md text-xs font-medium border border-slate-700 transition-colors ${
              isCapturing || isTrackingPaused
                ? 'opacity-50 cursor-not-allowed bg-slate-800 text-slate-400'
                : 'bg-slate-800 text-slate-200 hover:bg-slate-700 hover:text-white'
            }`}
          >
            <Camera className={`w-3.5 h-3.5 ${isCapturing ? 'animate-spin' : ''}`} />
            <span className="hidden sm:inline">Capture</span>
          </button>

          <button
            onClick={onToggleTracking}
            className={`flex items-center space-x-1.5 px-3 py-1.5 rounded-md text-xs font-semibold shadow-sm transition-colors ${
              isTrackingPaused
                ? 'bg-amber-600 hover:bg-amber-500 text-white animate-pulse'
                : 'bg-emerald-600 hover:bg-emerald-500 text-white'
            }`}
          >
            {isTrackingPaused ? (
              <>
                <Play className="w-3.5 h-3.5 fill-current" />
                <span>Resume Tracking</span>
              </>
            ) : (
              <>
                <Pause className="w-3.5 h-3.5 fill-current" />
                <span>Active</span>
              </>
            )}
          </button>
        </div>
      </div>
    </header>
  );
};
