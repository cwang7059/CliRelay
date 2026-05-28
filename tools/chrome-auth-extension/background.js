const SETTINGS_KEY = "clirelayOAuthSettings";
const SESSION_KEY = "clirelayOAuthSession";
const POLL_ALARM = "clirelay-oauth-poll";

const DEFAULT_SETTINGS = {
  apiBase: "http://127.0.0.1:8317/v0/management",
  managementKey: "admin123",
  provider: "codex",
  projectId: "",
  proxyId: "",
  autoCloseCallbackTab: true
};

const WEBUI_PROVIDERS = new Set(["codex", "anthropic", "antigravity", "gemini-cli"]);
const CALLBACK_PROVIDER_MAP = {
  "gemini-cli": "gemini"
};

function normalizeString(value) {
  return typeof value === "string" ? value.trim() : "";
}

function normalizeApiBase(value) {
  const fallback = DEFAULT_SETTINGS.apiBase;
  const raw = normalizeString(value) || fallback;
  try {
    const url = new URL(raw);
    let pathname = url.pathname.replace(/\/+$/, "");
    if (!pathname || pathname === "/") {
      pathname = "/v0/management";
    } else if (pathname.endsWith("/v0")) {
      pathname = `${pathname}/management`;
    } else if (!pathname.endsWith("/v0/management")) {
      pathname = `${pathname}/v0/management`;
    }
    url.pathname = pathname;
    url.search = "";
    url.hash = "";
    return url.toString().replace(/\/+$/, "");
  } catch {
    return fallback;
  }
}

function normalizeProvider(provider) {
  const value = normalizeString(provider).toLowerCase();
  return value || DEFAULT_SETTINGS.provider;
}

function normalizeSettings(settings = {}) {
  return {
    apiBase: normalizeApiBase(settings.apiBase),
    managementKey: normalizeString(settings.managementKey) || DEFAULT_SETTINGS.managementKey,
    provider: normalizeProvider(settings.provider),
    projectId: normalizeString(settings.projectId),
    proxyId: normalizeString(settings.proxyId),
    autoCloseCallbackTab: settings.autoCloseCallbackTab !== false
  };
}

function getErrorMessage(error) {
  return normalizeString(error?.message) || String(error || "未知错误");
}

async function getStoredSettings() {
  const stored = await chrome.storage.local.get(SETTINGS_KEY);
  return normalizeSettings(stored[SETTINGS_KEY] || {});
}

async function setStoredSettings(settings) {
  const normalized = normalizeSettings(settings);
  await chrome.storage.local.set({ [SETTINGS_KEY]: normalized });
  return normalized;
}

async function getSession() {
  const stored = await chrome.storage.local.get(SESSION_KEY);
  const session = stored[SESSION_KEY];
  return session && typeof session === "object" ? session : null;
}

async function setSession(session) {
  await chrome.storage.local.set({ [SESSION_KEY]: session });
  await updateBadge(session);
  return session;
}

async function clearSession() {
  await chrome.storage.local.remove(SESSION_KEY);
  await chrome.alarms.clear(POLL_ALARM);
  await updateBadge(null);
}

async function updateBadge(session) {
  const status = normalizeString(session?.status);
  let text = "";
  let color = "#64748b";
  if (status === "waiting") {
    text = "WAIT";
    color = "#2563eb";
  } else if (status === "captured" || status === "submitting") {
    text = "SEND";
    color = "#7c3aed";
  } else if (status === "success") {
    text = "OK";
    color = "#16a34a";
  } else if (status === "error") {
    text = "ERR";
    color = "#dc2626";
  }
  await chrome.action.setBadgeText({ text });
  await chrome.action.setBadgeBackgroundColor({ color });
}

function buildHeaders(settings) {
  return {
    "Authorization": `Bearer ${settings.managementKey}`,
    "Content-Type": "application/json"
  };
}

async function readResponseBody(response) {
  const text = await response.text();
  if (!text) {
    return null;
  }
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

function describeApiError(status, body) {
  if (body && typeof body === "object") {
    return body.error || body.message || body.detail || `HTTP ${status}`;
  }
  if (typeof body === "string" && body.trim()) {
    return body.trim();
  }
  return `HTTP ${status}`;
}

async function requestJson(settings, path, options = {}) {
  const method = options.method || "GET";
  const response = await fetch(`${settings.apiBase}${path}`, {
    method,
    headers: buildHeaders(settings),
    body: options.body ? JSON.stringify(options.body) : undefined
  });
  const body = await readResponseBody(response);
  if (!response.ok) {
    throw new Error(describeApiError(response.status, body));
  }
  return body || {};
}

function extractStateFromUrl(rawUrl) {
  try {
    return normalizeString(new URL(rawUrl).searchParams.get("state"));
  } catch {
    return "";
  }
}

function extractAuthUrl(payload = {}) {
  return normalizeString(payload.url)
    || normalizeString(payload.auth_url)
    || normalizeString(payload.authUrl);
}

function buildStartPath(settings) {
  const provider = normalizeProvider(settings.provider);
  const params = new URLSearchParams();
  if (WEBUI_PROVIDERS.has(provider)) {
    params.set("is_webui", "true");
  }
  if (provider === "gemini-cli" && settings.projectId) {
    params.set("project_id", settings.projectId);
  }
  if (settings.proxyId) {
    params.set("proxy_id", settings.proxyId);
  }
  const query = params.toString();
  return `/${provider}-auth-url${query ? `?${query}` : ""}`;
}

async function testConnection(settingsInput) {
  const settings = normalizeSettings(settingsInput);
  await requestJson(settings, "/auth-files");
  await setStoredSettings(settings);
  return { ok: true, settings };
}

async function startOAuth(settingsInput) {
  const settings = await setStoredSettings(settingsInput);
  const payload = await requestJson(settings, buildStartPath(settings));
  const authUrl = extractAuthUrl(payload);
  const state = normalizeString(payload.state) || extractStateFromUrl(authUrl);
  if (!authUrl) {
    throw new Error("CliRelay 没有返回授权链接");
  }
  if (!state) {
    throw new Error("授权链接缺少 state");
  }

  const tab = await chrome.tabs.create({ url: authUrl, active: true });
  const session = await setSession({
    status: "waiting",
    message: "等待浏览器授权",
    provider: settings.provider,
    state,
    authUrl,
    tabId: tab?.id || 0,
    proxyId: settings.proxyId,
    startedAt: Date.now(),
    updatedAt: Date.now(),
    lastPayload: payload
  });

  await chrome.alarms.create(POLL_ALARM, { periodInMinutes: 0.5 });
  return { ok: true, session };
}

function isTerminalStatus(status) {
  return status === "success" || status === "error";
}

function normalizeStatusPayload(payload = {}) {
  const status = normalizeString(payload.status).toLowerCase();
  if (status === "ok") {
    return { status: "success", message: "认证成功" };
  }
  if (status === "error") {
    return { status: "error", message: normalizeString(payload.error) || "认证失败" };
  }
  return { status: "waiting", message: "等待授权完成" };
}

async function pollAuthStatus() {
  const session = await getSession();
  if (!session?.state || isTerminalStatus(session.status)) {
    return { ok: true, session };
  }
  const settings = await getStoredSettings();
  const payload = await requestJson(
    settings,
    `/get-auth-status?state=${encodeURIComponent(session.state)}`
  );
  const normalized = normalizeStatusPayload(payload);
  const nextSession = await setSession({
    ...session,
    ...normalized,
    updatedAt: Date.now(),
    lastPayload: payload
  });
  if (isTerminalStatus(nextSession.status)) {
    await chrome.alarms.clear(POLL_ALARM);
  }
  return { ok: true, session: nextSession };
}

function parseCallbackUrl(rawUrl) {
  try {
    const parsed = new URL(rawUrl);
    const host = parsed.hostname.toLowerCase();
    const state = normalizeString(parsed.searchParams.get("state"));
    const code = normalizeString(parsed.searchParams.get("code"));
    const error = normalizeString(parsed.searchParams.get("error"))
      || normalizeString(parsed.searchParams.get("error_description"));
    const isLocal = host === "localhost" || host === "127.0.0.1" || host === "::1";
    const pathLooksRight = /callback|auth/i.test(parsed.pathname);
    if (!isLocal || !pathLooksRight || !state || (!code && !error)) {
      return null;
    }
    return { state, code, error, url: parsed.toString() };
  } catch {
    return null;
  }
}

async function closeTabIfNeeded(tabId, callbackUrl) {
  const settings = await getStoredSettings();
  if (!settings.autoCloseCallbackTab || !tabId) {
    return;
  }
  const parsed = parseCallbackUrl(callbackUrl);
  if (!parsed) {
    return;
  }
  await chrome.tabs.remove(tabId).catch(() => {});
}

async function submitCallbackUrl(callbackUrl, options = {}) {
  const callback = parseCallbackUrl(callbackUrl);
  if (!callback) {
    throw new Error("这不是可识别的 OAuth 回调地址");
  }

  const session = await getSession();
  if (!session?.provider || !session?.state) {
    throw new Error("没有正在进行的 OAuth 会话，请先从插件启动登录");
  }
  if (session.state !== callback.state) {
    throw new Error("回调 state 和当前会话不一致");
  }
  if (session.status === "success") {
    return { ok: true, session };
  }
  if (session.callbackUrl === callback.url && session.status === "submitting") {
    return { ok: true, session };
  }

  const settings = await getStoredSettings();
  const provider = CALLBACK_PROVIDER_MAP[session.provider] || session.provider;
  const proxyId = normalizeString(session.proxyId) || settings.proxyId;
  const submitting = await setSession({
    ...session,
    status: "submitting",
    message: "已捕获回调，正在提交",
    callbackUrl: callback.url,
    updatedAt: Date.now()
  });

  try {
    const payload = await requestJson(settings, "/oauth-callback", {
      method: "POST",
      body: {
        provider,
        redirect_url: callback.url,
        ...(proxyId ? { proxy_id: proxyId } : {})
      }
    });
    await setSession({
      ...submitting,
      status: "captured",
      message: "回调已提交，等待 CliRelay 处理",
      callbackUrl: callback.url,
      updatedAt: Date.now(),
      lastCallbackPayload: payload
    });
    await closeTabIfNeeded(options.tabId, callback.url);
    return await pollAuthStatus();
  } catch (error) {
    const nextSession = await setSession({
      ...submitting,
      status: "error",
      message: `回调提交失败：${getErrorMessage(error)}`,
      updatedAt: Date.now()
    });
    await chrome.alarms.clear(POLL_ALARM);
    return { ok: false, error: getErrorMessage(error), session: nextSession };
  }
}

async function maybeCaptureCallback(rawUrl, tabId) {
  const callback = parseCallbackUrl(rawUrl);
  if (!callback) {
    return null;
  }
  const session = await getSession();
  if (!session?.state || session.state !== callback.state || isTerminalStatus(session.status)) {
    return null;
  }
  return submitCallbackUrl(callback.url, { tabId });
}

chrome.runtime.onInstalled.addListener(async () => {
  await setStoredSettings(await getStoredSettings());
  await updateBadge(await getSession());
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === POLL_ALARM) {
    pollAuthStatus().catch(async (error) => {
      const session = await getSession();
      if (session) {
        await setSession({
          ...session,
          status: "error",
          message: `状态检查失败：${getErrorMessage(error)}`,
          updatedAt: Date.now()
        });
      }
      await chrome.alarms.clear(POLL_ALARM);
    });
  }
});

chrome.webNavigation.onCommitted.addListener((details) => {
  if (details.frameId === 0 && details.url) {
    maybeCaptureCallback(details.url, details.tabId).catch(() => {});
  }
});

chrome.webNavigation.onHistoryStateUpdated.addListener((details) => {
  if (details.frameId === 0 && details.url) {
    maybeCaptureCallback(details.url, details.tabId).catch(() => {});
  }
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.url) {
    maybeCaptureCallback(changeInfo.url, tabId).catch(() => {});
  }
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  const type = normalizeString(message?.type);
  (async () => {
    if (type === "GET_STATE") {
      return {
        ok: true,
        settings: await getStoredSettings(),
        session: await getSession()
      };
    }
    if (type === "SAVE_SETTINGS") {
      return { ok: true, settings: await setStoredSettings(message.settings || {}) };
    }
    if (type === "TEST_CONNECTION") {
      return testConnection(message.settings || {});
    }
    if (type === "START_OAUTH") {
      return startOAuth(message.settings || {});
    }
    if (type === "POLL_STATUS") {
      return pollAuthStatus();
    }
    if (type === "SUBMIT_CALLBACK") {
      return submitCallbackUrl(message.callbackUrl, { tabId: sender?.tab?.id || 0 });
    }
    if (type === "CLEAR_SESSION") {
      await clearSession();
      return { ok: true };
    }
    throw new Error(`未知消息：${type || "(empty)"}`);
  })()
    .then((payload) => sendResponse(payload))
    .catch((error) => sendResponse({ ok: false, error: getErrorMessage(error) }));
  return true;
});
