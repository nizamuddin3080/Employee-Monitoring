import { getOrInitializeProfile, updateProfileName } from './profile';

document.addEventListener('DOMContentLoaded', async () => {
  const profileNameInput = document.getElementById('profileNameInput') as HTMLInputElement;
  const profileIdText = document.getElementById('profileIdText') as HTMLDivElement;
  const saveNameBtn = document.getElementById('saveNameBtn') as HTMLButtonElement;
  const copyIdBtn = document.getElementById('copyIdBtn') as HTMLButtonElement;
  const statusDot = document.getElementById('statusDot') as HTMLDivElement;
  const statusText = document.getElementById('statusText') as HTMLSpanElement;

  try {
    const config = await getOrInitializeProfile();
    profileIdText.textContent = config.profileId;
    profileNameInput.value = config.profileName;

    // Check connection status from background service worker
    chrome.runtime.sendMessage({ type: 'GET_CONNECTION_STATUS' }, (response) => {
      if (chrome.runtime.lastError || !response) {
        statusDot.className = 'status-dot disconnected';
        statusText.textContent = 'Disconnected from Native Host';
      } else if (response.connected) {
        statusDot.className = 'status-dot';
        statusText.textContent = 'Connected to Native Host';
      } else {
        statusDot.className = 'status-dot disconnected';
        statusText.textContent = response.status || 'Offline / Reconnecting...';
      }
    });
  } catch (err) {
    statusText.textContent = 'Error loading profile';
  }

  saveNameBtn.addEventListener('click', async () => {
    const newName = profileNameInput.value.trim();
    if (!newName) return;

    try {
      await updateProfileName(newName);
      saveNameBtn.textContent = 'Saved!';
      setTimeout(() => { saveNameBtn.textContent = 'Save'; }, 1500);

      // Notify background script to update handshake
      chrome.runtime.sendMessage({ type: 'PROFILE_NAME_UPDATED', profileName: newName });
    } catch (err) {
      alert((err as Error).message);
    }
  });

  copyIdBtn.addEventListener('click', async () => {
    const id = profileIdText.textContent;
    if (id && id !== 'Loading...') {
      await navigator.clipboard.writeText(id);
      copyIdBtn.textContent = 'Copied!';
      setTimeout(() => { copyIdBtn.textContent = 'Copy'; }, 1500);
    }
  });
});
