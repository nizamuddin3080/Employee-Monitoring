export interface SanitizedUrlResult {
  rawUrl: string;
  sanitizedUrl: string;
  domain: string;
  hostname: string;
  isRestricted: boolean;
}

const SENSITIVE_QUERY_PARAMS = new Set([
  'token',
  'auth',
  'authtoken',
  'auth_token',
  'bearer',
  'password',
  'pass',
  'pwd',
  'secret',
  'client_secret',
  'private_key',
  'code',
  'access_token',
  'api_key',
  'apikey',
  'session',
  'session_id',
  'sessionid',
  'sessid',
  'sig',
  'signature',
  'key',
  'credential',
  'credentials',
  'jwt',
  'id_token',
  'refresh_token',
  'oauth_token',
  'oauth_verifier',
  'state',
  'ticket',
  'hash',
  'samlresponse',
  'relaystate',
  'assertion',
  'x-amz-signature',
  'x-amz-credential',
  'x-amz-security-token',
  'stripe_token',
  'csrf',
  'xsrf_token',
  'verification_code',
  'cvv',
  'ssn',
  'pin'
]);

const RESTRICTED_SCHEMES = ['chrome:', 'chrome-extension:', 'edge:', 'about:', 'data:', 'file:', 'javascript:'];

/**
 * Strips sensitive authentication parameters and credentials from URLs, enforcing privacy by design.
 */
export function sanitizeUrl(rawUrl: string): SanitizedUrlResult {
  if (!rawUrl || typeof rawUrl !== 'string') {
    return {
      rawUrl: '',
      sanitizedUrl: '',
      domain: '',
      hostname: '',
      isRestricted: true
    };
  }

  // Check restricted schemes
  const lowerUrl = rawUrl.toLowerCase().trim();
  for (const scheme of RESTRICTED_SCHEMES) {
    if (lowerUrl.startsWith(scheme)) {
      return {
        rawUrl,
        sanitizedUrl: lowerUrl.split('?')[0].split('#')[0],
        domain: scheme.replace(':', ''),
        hostname: scheme.replace(':', ''),
        isRestricted: true
      };
    }
  }

  try {
    const parsed = new URL(rawUrl);

    // Strip embedded username and password
    parsed.username = '';
    parsed.password = '';

    // Strip sensitive query parameters
    const paramsToDelete: string[] = [];
    parsed.searchParams.forEach((_, key) => {
      if (SENSITIVE_QUERY_PARAMS.has(key.toLowerCase())) {
        paramsToDelete.push(key);
      }
    });

    for (const key of paramsToDelete) {
      parsed.searchParams.delete(key);
    }

    // Strip fragments containing access tokens or auth info
    if (parsed.hash && (parsed.hash.includes('access_token=') || parsed.hash.includes('token='))) {
      parsed.hash = '';
    }

    const hostname = parsed.hostname.toLowerCase();
    const domain = extractRootDomain(hostname);

    return {
      rawUrl,
      sanitizedUrl: parsed.toString(),
      domain,
      hostname,
      isRestricted: false
    };
  } catch {
    // If unparseable URL, return stripped representation
    const sanitized = rawUrl.split('?')[0].split('#')[0];
    return {
      rawUrl,
      sanitizedUrl: sanitized,
      domain: 'unknown',
      hostname: 'unknown',
      isRestricted: false
    };
  }
}

function extractRootDomain(hostname: string): string {
  if (!hostname) return '';
  const parts = hostname.split('.');
  if (parts.length <= 2) return hostname;
  return parts.slice(-2).join('.');
}
