const BASE_ENDPOINTS = [
  "http://127.0.0.1:47633",
  "http://localhost:47633"
];

const CLIENT_HEADER_NAME = "X-CipherVault-Client";
const CLIENT_HEADER_VALUE = "BrowserExtension";
const SESSION_HEADER_NAME = "X-CipherVault-Session";

let sessionToken = null;
let sessionTokenExpiresAtMs = 0;

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (!message || !message.type || !message.payload) {
    return false;
  }

  if (message.type === "cipherpw-capture") {
    relayToCipherVault("/capture", message.payload)
      .then((result) => sendResponse({ ok: result.ok }))
      .catch(() => sendResponse({ ok: false }));

    return true;
  }

  if (message.type === "ciphervault-autofill-query") {
    relayToCipherVault("/autofill/query", message.payload)
      .then((result) =>
        sendResponse({
          ok: result.ok,
          entries: result.data?.entries || []
        })
      )
      .catch(() => sendResponse({ ok: false, entries: [] }));

    return true;
  }

  return false;
});

async function relayToCipherVault(path, payload) {
  const body = JSON.stringify(payload);

  for (const base of BASE_ENDPOINTS) {
    try {
      const result = await postWithSessionToken(base, path, body);
      if (result.ok) {
        return result;
      }
    } catch (_err) {
      // Ignore and try next endpoint.
    }
  }

  return { ok: false, data: null };
}

async function postWithSessionToken(base, path, body) {
  let token = await ensureSessionToken(base, false);
  if (!token) {
    return { ok: false, data: null };
  }

  let response = await fetchWithSessionToken(base, path, body, token);

  if (response.status === 401) {
    invalidateSessionToken();
    token = await ensureSessionToken(base, true);
    if (!token) {
      return { ok: false, data: null };
    }

    response = await fetchWithSessionToken(base, path, body, token);
  }

  if (!response.ok) {
    return { ok: false, data: null };
  }

  let data = null;
  try {
    data = await response.json();
  } catch (_jsonErr) {
    // Non-JSON responses are treated as success with empty payload.
  }

  return { ok: true, data };
}

async function fetchWithSessionToken(base, path, body, token) {
  const headers = {
    "Content-Type": "application/json",
    [CLIENT_HEADER_NAME]: CLIENT_HEADER_VALUE,
    [SESSION_HEADER_NAME]: token
  };

  return fetch(`${base}${path}`, {
    method: "POST",
    headers,
    body,
    cache: "no-store"
  });
}

async function ensureSessionToken(base, forceRefresh) {
  const now = Date.now();
  if (!forceRefresh && sessionToken && now < sessionTokenExpiresAtMs) {
    return sessionToken;
  }

  const tokenResult = await fetchSessionToken(base);
  if (!tokenResult) {
    invalidateSessionToken();
    return null;
  }

  sessionToken = tokenResult.token;
  sessionTokenExpiresAtMs = tokenResult.expiresAtMs;
  return sessionToken;
}

async function fetchSessionToken(base) {
  const headers = {
    "Content-Type": "application/json",
    [CLIENT_HEADER_NAME]: CLIENT_HEADER_VALUE
  };

  try {
    const response = await fetch(`${base}/session/token`, {
      method: "POST",
      headers,
      body: "{}",
      cache: "no-store"
    });

    if (!response.ok) {
      return null;
    }

    const payload = await response.json();
    if (!payload || !payload.ok || !payload.token) {
      return null;
    }

    const parsedExpiry = Date.parse(payload.expiresAtUtc || "");
    const ttlSecondsRaw = Number(payload.ttlSeconds);
    const ttlSeconds = Number.isFinite(ttlSecondsRaw) ? Math.max(1, ttlSecondsRaw) : 60;
    const fallbackExpiry = Date.now() + ttlSeconds * 1000;

    return {
      token: payload.token,
      expiresAtMs: Number.isFinite(parsedExpiry) ? parsedExpiry : fallbackExpiry
    };
  } catch (_err) {
    return null;
  }
}

function invalidateSessionToken() {
  sessionToken = null;
  sessionTokenExpiresAtMs = 0;
}
