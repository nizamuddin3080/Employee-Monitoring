export interface ProfileConfig {
  profileId: string;
  profileName: string;
  isEnrolled: boolean;
  enrolledAtUtc: string;
}

const STORAGE_KEY_PROFILE_ID = 'lat_profile_id';
const STORAGE_KEY_PROFILE_NAME = 'lat_profile_name';
const STORAGE_KEY_ENROLLED_AT = 'lat_enrolled_at_utc';

/**
 * Retrieves the persistent profile configuration for this Chrome profile instance.
 * If no profile ID exists in local storage, generates a new stable UUIDv4.
 */
export async function getOrInitializeProfile(): Promise<ProfileConfig> {
  const data = await chrome.storage.local.get([
    STORAGE_KEY_PROFILE_ID,
    STORAGE_KEY_PROFILE_NAME,
    STORAGE_KEY_ENROLLED_AT
  ]);

  let profileId = data[STORAGE_KEY_PROFILE_ID];
  let profileName = data[STORAGE_KEY_PROFILE_NAME];
  let enrolledAt = data[STORAGE_KEY_ENROLLED_AT];

  if (!profileId) {
    profileId = crypto.randomUUID();
    profileName = 'Default Profile';
    enrolledAt = new Date().toISOString();

    await chrome.storage.local.set({
      [STORAGE_KEY_PROFILE_ID]: profileId,
      [STORAGE_KEY_PROFILE_NAME]: profileName,
      [STORAGE_KEY_ENROLLED_AT]: enrolledAt
    });
  }

  return {
    profileId,
    profileName: profileName || 'Default Profile',
    isEnrolled: true,
    enrolledAtUtc: enrolledAt || new Date().toISOString()
  };
}

/**
 * Updates the user-friendly profile name in local storage.
 */
export async function updateProfileName(newName: string): Promise<void> {
  const trimmed = newName.trim();
  if (!trimmed) {
    throw new Error('Profile name cannot be empty.');
  }

  await chrome.storage.local.set({
    [STORAGE_KEY_PROFILE_NAME]: trimmed
  });
}
