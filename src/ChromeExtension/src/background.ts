import { getOrInitializeProfile, type ProfileConfig } from './profile';
import { sanitizeUrl } from './url-sanitizer';

const NATIVE_HOST_NAME = 'com.localactivitytracker.agent';

let nativePort: chrome.runtime.Port | null = null;
let currentProfile: ProfileConfig | null = null;
let isConnected = false;
let reconnectTimer: any = null;
let reconnectDelayMs = 1000;
let lastReportedTabId: number | null = null;
let lastReportedUrl: string | null = null;

async function init() {
  currentProfile = await getOrInitializeProfile();
  connectToNativeHost();

  // Setup periodic heartbeat (25s)
  setInterval(() => {
    if (isConnected && nativePort && currentProfile) {
      postNativeMessage({
        type: 'HEARTBEAT',
        payload: {
          profile_id: currentProfile.profileId,
          timestamp_utc: new Date().toISOString()
        }
      });
    }
  }, 25000);
}

function connectToNativeHost() {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }

  try {
    nativePort = chrome.runtime.connectNative(NATIVE_HOST_NAME);

    nativePort.onMessage.addListener(handleNativeMessage);

    nativePort.onDisconnect.addListener(() => {
      isConnected = false;
      nativePort = null;
      scheduleReconnect();
    });

    // Send initial CLIENT_HELLO handshake
    if (currentProfile) {
      postNativeMessage({
        type: 'CLIENT_HELLO',
        payload: {
          profile_id: currentProfile.profileId,
          profile_name: currentProfile.profileName,
          extension_version: '1.0.0',
          chrome_version: navigator.userAgent,
          client_timestamp_utc: new Date().toISOString()
        }
      });
    }
  } catch {
    isConnected = false;
    scheduleReconnect();
  }
}

function scheduleReconnect() {
  if (reconnectTimer) return;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    reconnectDelayMs = Math.min(reconnectDelayMs * 2, 10000);
    connectToNativeHost();
  }, reconnectDelayMs);
}

function postNativeMessage(message: any) {
  if (nativePort) {
    try {
      nativePort.postMessage(message);
    } catch {
      // Port disconnected
    }
  }
}

async function handleNativeMessage(message: any) {
  if (!message || typeof message !== 'object') return;

  if (message.type === 'COORDINATOR_STATUS_CHANGED') {
    isConnected = message.payload?.status === 'CONNECTED';
    if (isConnected) {
      lastReportedTabId = null;
      lastReportedUrl = null;
      reportActiveTab();
    }
    return;
  }

  if (message.type === 'CLIENT_HELLO_ACK') {
    isConnected = message.payload?.status === 'CONNECTED';
    reconnectDelayMs = 1000; // Reset backoff on successful ack

    if (isConnected) {
      // Reset reported state on reconnect to guarantee announcement
      lastReportedTabId = null;
      lastReportedUrl = null;

      // Immediately poll and send active tab state
      reportActiveTab();
    }
    return;
  }

  if (message.type === 'CAPTURE_SCREENSHOT_REQUEST') {
    await handleScreenshotRequest(message.payload);
  }
}

async function handleScreenshotRequest(payload: any) {
  const captureId = payload?.capture_id || crypto.randomUUID();

  try {
    const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    const activeTab = tabs[0];

    if (!activeTab || !activeTab.id || activeTab.incognito) {
      postNativeMessage({
        type: 'SCREENSHOT_FAILED',
        payload: {
          capture_id: captureId,
          profile_id: currentProfile?.profileId,
          failure_code: 'WINDOW_OCCLUDED_OR_MINIMIZED',
          failure_reason: 'No visible active tab found in focused window.'
        }
      });
      return;
    }

    const sanitized = sanitizeUrl(activeTab.url || '');
    if (sanitized.isRestricted) {
      postNativeMessage({
        type: 'SCREENSHOT_FAILED',
        payload: {
          capture_id: captureId,
          profile_id: currentProfile?.profileId,
          failure_code: 'RESTRICTED_URL',
          failure_reason: 'Cannot capture internal or restricted browser page.'
        }
      });
      return;
    }

    const dataUrl = await chrome.tabs.captureVisibleTab(null, { format: 'png' });
    const base64Data = dataUrl.includes(',') ? dataUrl.split(',')[1] : dataUrl;

    postNativeMessage({
      type: 'SCREENSHOT_CAPTURED',
      payload: {
        capture_id: captureId,
        profile_id: currentProfile?.profileId,
        window_id: activeTab.windowId,
        tab_id: activeTab.id,
        url: sanitized.sanitizedUrl,
        timestamp_utc: new Date().toISOString(),
        image_format: 'png',
        image_data_base64: base64Data
      }
    });
  } catch (err) {
    postNativeMessage({
      type: 'SCREENSHOT_FAILED',
      payload: {
        capture_id: captureId,
        profile_id: currentProfile?.profileId,
        failure_code: 'CAPTURE_TIMEOUT',
        failure_reason: (err as Error)?.message || 'Failed to capture visible tab.'
      }
    });
  }
}

async function reportActiveTab() {
  if (!currentProfile) return;

  try {
    let tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    if (!tabs || tabs.length === 0) {
      tabs = await chrome.tabs.query({ active: true, currentWindow: true });
    }
    if (!tabs || tabs.length === 0) {
      tabs = await chrome.tabs.query({ active: true });
    }
    const tab = tabs[0];
    if (tab && tab.id && !tab.incognito) {
      dispatchTabNavigated(tab);
    }
  } catch {
    // Suppress tab query errors
  }
}

function dispatchTabNavigated(tab: chrome.tabs.Tab) {
  if (!currentProfile || !tab.id || tab.incognito) return;

  const sanitized = sanitizeUrl(tab.url || '');

  // Suppress duplicate spam for same tab and identical URL
  if (lastReportedTabId === tab.id && lastReportedUrl === sanitized.sanitizedUrl) {
    return;
  }

  lastReportedTabId = tab.id;
  lastReportedUrl = sanitized.sanitizedUrl;

  postNativeMessage({
    type: 'TAB_NAVIGATED',
    payload: {
      profile_id: currentProfile.profileId,
      window_id: tab.windowId,
      tab_id: tab.id,
      raw_url: tab.url || '',
      sanitized_url: sanitized.sanitizedUrl,
      domain: sanitized.domain,
      page_title: tab.title || sanitized.hostname,
      is_incognito: false,
      timestamp_utc: new Date().toISOString()
    }
  });
}

// --- Tab & Window Event Listeners ---

// 1. Tab activated (tab switch)
chrome.tabs.onActivated.addListener(async (activeInfo) => {
  if (!nativePort) {
    connectToNativeHost();
  }
  if (!currentProfile) return;

  try {
    const tab = await chrome.tabs.get(activeInfo.tabId);
    if (!tab || tab.incognito) return;

    const sanitized = sanitizeUrl(tab.url || '');

    lastReportedTabId = tab.id ?? null;
    lastReportedUrl = sanitized.sanitizedUrl;

    postNativeMessage({
      type: 'TAB_ACTIVATED',
      payload: {
        profile_id: currentProfile.profileId,
        window_id: activeInfo.windowId,
        tab_id: activeInfo.tabId,
        raw_url: tab.url || '',
        sanitized_url: sanitized.sanitizedUrl,
        domain: sanitized.domain,
        page_title: tab.title || sanitized.hostname,
        timestamp_utc: new Date().toISOString()
      }
    });
  } catch {
    // Ignore invalid tab handles
  }
});

// 2. Tab updated (navigation or title change)
chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (!currentProfile || tab.incognito) return;

  // Track on status complete or URL modification
  if (changeInfo.status === 'complete' || changeInfo.url) {
    if (tab.active) {
      dispatchTabNavigated(tab);
    }
  }
});

// 3. Tab removed (tab closed)
chrome.tabs.onRemoved.addListener((tabId, removeInfo) => {
  if (!currentProfile) return;

  if (lastReportedTabId === tabId) {
    lastReportedTabId = null;
    lastReportedUrl = null;
  }

  postNativeMessage({
    type: 'TAB_CLOSED',
    payload: {
      profile_id: currentProfile.profileId,
      window_id: removeInfo.windowId,
      tab_id: tabId,
      timestamp_utc: new Date().toISOString()
    }
  });
});

// 4. Window focus changed
chrome.windows.onFocusChanged.addListener((windowId) => {
  if (!nativePort) {
    connectToNativeHost();
  }
  if (!currentProfile) return;

  postNativeMessage({
    type: 'WINDOW_FOCUSED',
    payload: {
      profile_id: currentProfile.profileId,
      window_id: windowId,
      timestamp_utc: new Date().toISOString()
    }
  });

  if (windowId !== chrome.windows.WINDOW_ID_NONE) {
    reportActiveTab();
  }
});

// 5. Popup messaging communication
chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.type === 'GET_CONNECTION_STATUS') {
    if (!nativePort || !isConnected) {
      connectToNativeHost();
    }
    sendResponse({
      connected: isConnected,
      profileId: currentProfile?.profileId,
      status: isConnected ? 'CONNECTED' : 'RECONNECTING'
    });
    return false;
  }

  if (message.type === 'PROFILE_NAME_UPDATED' && currentProfile) {
    currentProfile.profileName = message.profileName;
    if (isConnected) {
      postNativeMessage({
        type: 'CLIENT_HELLO',
        payload: {
          profile_id: currentProfile.profileId,
          profile_name: currentProfile.profileName,
          extension_version: '1.0.0',
          chrome_version: navigator.userAgent,
          client_timestamp_utc: new Date().toISOString()
        }
      });
    }
    return false;
  }
});

// Start the extension service worker
init();
