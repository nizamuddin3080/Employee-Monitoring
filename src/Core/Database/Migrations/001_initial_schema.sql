-- Migration 001: Initial Schema for Local Activity Tracker
-- Strictly omits window_title from application_sessions

CREATE TABLE IF NOT EXISTS schema_migrations (
    version INTEGER PRIMARY KEY,
    description TEXT NOT NULL,
    applied_at_utc TEXT NOT NULL,
    checksum TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS chrome_profiles (
    profile_id TEXT PRIMARY KEY,
    profile_name TEXT NOT NULL,
    chrome_profile_dir TEXT NULL,
    is_enrolled INTEGER NOT NULL DEFAULT 1,
    connection_status TEXT NOT NULL DEFAULT 'OFFLINE',
    enrolled_at_utc TEXT NOT NULL,
    last_seen_utc TEXT NOT NULL,
    last_screenshot_at_utc TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_chrome_profiles_last_seen ON chrome_profiles(last_seen_utc);

CREATE TABLE IF NOT EXISTS browser_windows (
    window_session_id TEXT PRIMARY KEY,
    profile_id TEXT NOT NULL,
    chrome_window_id INTEGER NOT NULL,
    window_type TEXT NOT NULL DEFAULT 'normal',
    is_focused INTEGER NOT NULL DEFAULT 0,
    opened_at_utc TEXT NOT NULL,
    closed_at_utc TEXT NULL,
    FOREIGN KEY (profile_id) REFERENCES chrome_profiles(profile_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_browser_windows_profile ON browser_windows(profile_id, opened_at_utc);

CREATE TABLE IF NOT EXISTS browser_sessions (
    session_id TEXT PRIMARY KEY,
    profile_id TEXT NOT NULL,
    start_time_utc TEXT NOT NULL,
    end_time_utc TEXT NULL,
    duration_seconds INTEGER NOT NULL DEFAULT 0,
    is_completed INTEGER NOT NULL DEFAULT 0,
    session_status TEXT NOT NULL DEFAULT 'ACTIVE',
    FOREIGN KEY (profile_id) REFERENCES chrome_profiles(profile_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_browser_sessions_range ON browser_sessions(profile_id, start_time_utc, end_time_utc);

CREATE TABLE IF NOT EXISTS url_activity_sessions (
    session_id TEXT PRIMARY KEY,
    profile_id TEXT NOT NULL,
    window_session_id TEXT NULL,
    chrome_tab_id INTEGER NOT NULL,
    url TEXT NOT NULL,
    sanitized_url TEXT NOT NULL,
    domain TEXT NOT NULL,
    page_title TEXT NOT NULL,
    start_time_utc TEXT NOT NULL,
    end_time_utc TEXT NULL,
    last_heartbeat_utc TEXT NOT NULL,
    active_duration_seconds INTEGER NOT NULL DEFAULT 0,
    is_completed INTEGER NOT NULL DEFAULT 0,
    session_status TEXT NOT NULL DEFAULT 'ACTIVE',
    completeness_notes TEXT NULL,
    FOREIGN KEY (profile_id) REFERENCES chrome_profiles(profile_id) ON DELETE CASCADE,
    FOREIGN KEY (window_session_id) REFERENCES browser_windows(window_session_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_url_sessions_profile_time ON url_activity_sessions(profile_id, start_time_utc, end_time_utc);
CREATE INDEX IF NOT EXISTS idx_url_sessions_domain ON url_activity_sessions(domain);
CREATE INDEX IF NOT EXISTS idx_url_sessions_status ON url_activity_sessions(is_completed, session_status);

CREATE TABLE IF NOT EXISTS tab_events (
    event_id INTEGER PRIMARY KEY AUTOINCREMENT,
    profile_id TEXT NOT NULL,
    session_id TEXT NULL,
    chrome_tab_id INTEGER NOT NULL,
    event_type TEXT NOT NULL,
    event_payload_json TEXT NULL,
    event_time_utc TEXT NOT NULL,
    FOREIGN KEY (profile_id) REFERENCES chrome_profiles(profile_id) ON DELETE CASCADE,
    FOREIGN KEY (session_id) REFERENCES url_activity_sessions(session_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_tab_events_time ON tab_events(profile_id, event_time_utc);

CREATE TABLE IF NOT EXISTS screenshot_metadata (
    screenshot_id TEXT PRIMARY KEY,
    profile_id TEXT NOT NULL,
    url_session_id TEXT NULL,
    capture_time_utc TEXT NOT NULL,
    url TEXT NOT NULL,
    file_path TEXT NOT NULL,
    file_size_bytes INTEGER NOT NULL DEFAULT 0,
    capture_status TEXT NOT NULL,
    failure_reason TEXT NULL,
    FOREIGN KEY (profile_id) REFERENCES chrome_profiles(profile_id) ON DELETE CASCADE,
    FOREIGN KEY (url_session_id) REFERENCES url_activity_sessions(session_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_screenshot_profile_time ON screenshot_metadata(profile_id, capture_time_utc);
CREATE INDEX IF NOT EXISTS idx_screenshot_status ON screenshot_metadata(capture_status);

-- Non-Chrome application usage strictly omitting window_title
CREATE TABLE IF NOT EXISTS application_sessions (
    session_id TEXT PRIMARY KEY,
    application_display_name TEXT NOT NULL,
    process_name TEXT NOT NULL,
    start_time_utc TEXT NOT NULL,
    end_time_utc TEXT NULL,
    last_heartbeat_utc TEXT NOT NULL,
    duration_seconds INTEGER NOT NULL DEFAULT 0,
    is_completed INTEGER NOT NULL DEFAULT 0,
    session_status TEXT NOT NULL DEFAULT 'ACTIVE'
);

CREATE INDEX IF NOT EXISTS idx_app_sessions_time ON application_sessions(start_time_utc, end_time_utc);
CREATE INDEX IF NOT EXISTS idx_app_sessions_process ON application_sessions(process_name);

CREATE TABLE IF NOT EXISTS tracking_events (
    log_id INTEGER PRIMARY KEY AUTOINCREMENT,
    profile_id TEXT NULL,
    event_category TEXT NOT NULL,
    severity TEXT NOT NULL DEFAULT 'INFO',
    message TEXT NOT NULL,
    event_time_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_tracking_events_time ON tracking_events(event_time_utc);

CREATE TABLE IF NOT EXISTS application_settings (
    setting_key TEXT PRIMARY KEY,
    setting_value TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

INSERT OR IGNORE INTO application_settings (setting_key, setting_value, updated_at_utc) VALUES
('screenshot_interval_seconds', '600', datetime('now')),
('retention_days', '90', datetime('now')),
('start_with_windows', 'true', datetime('now')),
('tracking_paused', 'false', datetime('now')),
('redact_query_params', 'true', datetime('now'));
