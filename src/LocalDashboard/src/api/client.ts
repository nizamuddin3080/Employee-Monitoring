/**
 * Hardened REST API Client for Local Activity Tracker Dashboard.
 * Automatically acquires local capability token on bootstrap and enforces
 * custom CSRF header 'X-LAT-Requested-With: LocalActivityTracker'.
 */

const CSRF_HEADER_NAME = 'X-LAT-Requested-With';
const CSRF_HEADER_VALUE = 'LocalActivityTracker';

let cachedToken: string | null = null;

export async function getCapabilityToken(): Promise<string> {
  if (cachedToken) {
    return cachedToken;
  }

  try {
    const res = await fetch('/api/auth/token', {
      headers: {
        'Accept': 'application/json'
      }
    });

    if (res.ok) {
      const data = await res.json();
      if (data.token) {
        cachedToken = data.token;
        return data.token;
      }
    }
  } catch (err) {
    console.warn('Failed to auto-bootstrap local capability token:', err);
  }

  return '';
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = await getCapabilityToken();
  const headers = new Headers(options.headers || {});

  headers.set('Accept', 'application/json');

  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const method = (options.method || 'GET').toUpperCase();
  if (method === 'POST' || method === 'PUT' || method === 'DELETE') {
    headers.set(CSRF_HEADER_NAME, CSRF_HEADER_VALUE);
    if (!headers.has('Content-Type') && options.body && typeof options.body === 'string') {
      headers.set('Content-Type', 'application/json');
    }
  }

  const res = await fetch(path, {
    ...options,
    headers
  });

  if (!res.ok) {
    const errorText = await res.text();
    throw new Error(errorText || `HTTP ${res.status}: ${res.statusText}`);
  }

  if (res.status === 204) {
    return null as T;
  }

  return res.json() as Promise<T>;
}

export interface OverviewMetrics {
  date_local: string;
  chrome_foreground_seconds: number;
  active_url_seconds: number;
  non_chrome_app_seconds: number;
  enrolled_profiles_count: number;
  total_url_visits: number;
  screenshots_successful: number;
  screenshots_failed: number;
  last_event_time_utc: string;
  is_tracking_paused: boolean;
}

export interface ActivityRecord {
  session_id: string;
  profile_id: string;
  profile_name: string;
  url: string;
  domain: string;
  page_title: string;
  start_time_utc: string;
  end_time_utc?: string;
  duration_seconds: number;
  session_status: string;
}

export interface ActivityResponse {
  total_records: number;
  page: number;
  page_size: number;
  records: ActivityRecord[];
}

export interface ApplicationSessionItem {
  session_id: string;
  start_time_utc: string;
  end_time_utc?: string;
  duration_seconds: number;
  session_status: string;
}

export interface ApplicationUsageGroup {
  application_display_name: string;
  process_name: string;
  total_duration_seconds: number;
  session_count: number;
  sessions: ApplicationSessionItem[];
}

export interface ChromeProfileRecord {
  profileId: string;
  profileName: string;
  isEnrolled: boolean;
  connectionStatus: string;
  lastSeenUtc: string;
}

export interface UnmonitoredProfile {
  profileDirectoryName: string;
  displayName: string;
  email?: string;
  localStateExists: boolean;
  isEnrolled: boolean;
}

export interface ProfilesResponse {
  enrolled_profiles: ChromeProfileRecord[];
  unmonitored_profiles: UnmonitoredProfile[];
  has_unmonitored_profiles: boolean;
}

export interface ScreenshotRecord {
  screenshot_id: string;
  profile_id: string;
  url: string;
  capture_time_utc: string;
  capture_status: string;
  failure_reason?: string;
  file_size_bytes: number;
  has_image: boolean;
}

export interface ScreenshotsResponse {
  total_records: number;
  page: number;
  page_size: number;
  records: ScreenshotRecord[];
}

export interface CsvExportRequest {
  start_date?: string;
  end_date?: string;
  profile_ids?: string[];
  include_report_metadata?: boolean;
  include_profile_summary?: boolean;
  include_url_activity?: boolean;
  include_screenshot_metadata?: boolean;
  include_non_chrome_apps?: boolean;
}

export interface CsvRecordCounts {
  profiles_included: number;
  url_sessions_exported: number;
  screenshot_records_exported: number;
  application_sessions_exported: number;
}

export interface CsvExportResponse {
  status: string;
  file_name: string;
  file_path: string;
  record_counts: CsvRecordCounts;
}

export const api = {
  getOverview: () => request<OverviewMetrics>('/api/overview'),

  getActivity: (params: { startDate?: string; endDate?: string; profileId?: string; domain?: string; page?: number; pageSize?: number }) => {
    const q = new URLSearchParams();
    if (params.startDate) q.set('startDate', params.startDate);
    if (params.endDate) q.set('endDate', params.endDate);
    if (params.profileId) q.set('profileId', params.profileId);
    if (params.domain) q.set('domain', params.domain);
    if (params.page) q.set('page', params.page.toString());
    if (params.pageSize) q.set('pageSize', params.pageSize.toString());
    return request<ActivityResponse>(`/api/activity?${q.toString()}`);
  },

  getApplications: (startDate?: string, endDate?: string) => {
    const q = new URLSearchParams();
    if (startDate) q.set('startDate', startDate);
    if (endDate) q.set('endDate', endDate);
    return request<ApplicationUsageGroup[]>(`/api/applications?${q.toString()}`);
  },

  getProfiles: () => request<ProfilesResponse>('/api/profiles'),

  updateProfileName: (id: string, name: string) =>
    request<ChromeProfileRecord>(`/api/profiles/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: JSON.stringify({ profile_name: name })
    }),

  getScreenshots: (params: { profileId?: string; startDate?: string; endDate?: string; page?: number; pageSize?: number }) => {
    const q = new URLSearchParams();
    if (params.profileId) q.set('profileId', params.profileId);
    if (params.startDate) q.set('startDate', params.startDate);
    if (params.endDate) q.set('endDate', params.endDate);
    if (params.page) q.set('page', params.page.toString());
    if (params.pageSize) q.set('pageSize', params.pageSize.toString());
    return request<ScreenshotsResponse>(`/api/screenshots?${q.toString()}`);
  },

  deleteScreenshot: (id: string) =>
    request<void>(`/api/screenshots/${encodeURIComponent(id)}`, {
      method: 'DELETE'
    }),

  triggerCapture: (profileId?: string) =>
    request<any>('/api/screenshots/capture', {
      method: 'POST',
      body: JSON.stringify(profileId ? { profile_id: profileId } : {})
    }),

  getSettings: () => request<Record<string, string>>('/api/settings'),

  saveSettings: (settings: Record<string, string>) =>
    request<Record<string, string>>('/api/settings', {
      method: 'POST',
      body: JSON.stringify(settings)
    }),

  pauseTracking: () =>
    request<{ is_tracking_paused: boolean }>('/api/tracking/pause', {
      method: 'POST'
    }),

  resumeTracking: () =>
    request<{ is_tracking_paused: boolean }>('/api/tracking/resume', {
      method: 'POST'
    }),

  exportCsv: (params: CsvExportRequest) =>
    request<CsvExportResponse>('/api/export/csv', {
      method: 'POST',
      body: JSON.stringify(params)
    }),

  createBackup: () =>
    request<{ status: string; backup_path: string; file_name: string }>('/api/data/backup', {
      method: 'POST'
    }),

  purgeData: (params: { older_than_days?: number; purge_all?: boolean }) =>
    request<{ status: string; purged_all: boolean; older_than_days?: number; deleted_rows?: number }>('/api/data/purge', {
      method: 'POST',
      body: JSON.stringify(params)
    }),

  revealExport: (filePath?: string) =>
    request<{ revealed: boolean; path: string }>('/api/export/reveal', {
      method: 'POST',
      body: JSON.stringify({ file_path: filePath })
    }),

  getExportDownloadUrl: (fileName: string) =>
    `/api/export/download?file=${encodeURIComponent(fileName)}`
};
