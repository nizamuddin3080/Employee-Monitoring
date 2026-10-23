declare namespace chrome {
  namespace storage {
    interface StorageArea {
      get(keys?: string | string[] | Record<string, any>): Promise<Record<string, any>>;
      set(items: Record<string, any>): Promise<void>;
      remove(keys: string | string[]): Promise<void>;
      clear(): Promise<void>;
    }
    const local: StorageArea;
    const sync: StorageArea;
  }

  namespace runtime {
    interface Port {
      name: string;
      onDisconnect: {
        addListener(callback: (port: Port) => void): void;
        removeListener(callback: (port: Port) => void): void;
      };
      onMessage: {
        addListener(callback: (message: any, port: Port) => void): void;
        removeListener(callback: (message: any, port: Port) => void): void;
      };
      postMessage(message: any): void;
      disconnect(): void;
    }

    const lastError: { message?: string } | undefined;

    function connectNative(application: string): Port;
    function sendMessage(message: any, responseCallback?: (response: any) => void): void;

    const onMessage: {
      addListener(callback: (message: any, sender: any, sendResponse: (response?: any) => void) => boolean | void): void;
    };
  }

  namespace tabs {
    interface Tab {
      id?: number;
      windowId: number;
      url?: string;
      title?: string;
      active: boolean;
      incognito: boolean;
      status?: string;
    }

    interface TabActiveInfo {
      tabId: number;
      windowId: number;
    }

    interface TabChangeInfo {
      status?: string;
      url?: string;
      title?: string;
    }

    function query(queryInfo: { active?: boolean; currentWindow?: boolean; lastFocusedWindow?: boolean }): Promise<Tab[]>;
    function get(tabId: number): Promise<Tab>;
    function captureVisibleTab(windowId?: number | null, options?: { format?: 'jpeg' | 'png'; quality?: number }): Promise<string>;

    const onActivated: {
      addListener(callback: (activeInfo: TabActiveInfo) => void): void;
    };

    const onUpdated: {
      addListener(callback: (tabId: number, changeInfo: TabChangeInfo, tab: Tab) => void): void;
    };

    const onRemoved: {
      addListener(callback: (tabId: number, removeInfo: { windowId: number; isWindowClosing: boolean }) => void): void;
    };
  }

  namespace windows {
    const WINDOW_ID_NONE: number;

    const onFocusChanged: {
      addListener(callback: (windowId: number) => void): void;
    };
  }
}
