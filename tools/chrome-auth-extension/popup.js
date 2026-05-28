const PROVIDER_LABELS = {
  codex: "Codex",
  anthropic: "Anthropic",
  antigravity: "Antigravity",
  "gemini-cli": "Gemini CLI",
  qwen: "Qwen",
  kimi: "Kimi"
};

const STATUS_LABELS = {
  waiting: "等待",
  captured: "已捕获",
  submitting: "提交中",
  success: "成功",
  error: "失败"
};

const elements = {
  apiBase: document.getElementById("apiBase"),
  managementKey: document.getElementById("managementKey"),
  provider: document.getElementById("provider"),
  projectId: document.getElementById("projectId"),
  projectIdRow: document.getElementById("projectIdRow"),
  proxyId: document.getElementById("proxyId"),
  autoCloseCallbackTab: document.getElementById("autoCloseCallbackTab"),
  statusText: document.getElementById("statusText"),
  statusPill: document.getElementById("statusPill"),
  sessionPanel: document.getElementById("sessionPanel"),
  stateValue: document.getElementById("stateValue"),
  authUrl: document.getElementById("authUrl"),
  callbackUrl: document.getElementById("callbackUrl"),
  saveBtn: document.getElementById("saveBtn"),
  testBtn: document.getElementById("testBtn"),
  startBtn: document.getElementById("startBtn"),
  pollBtn: document.getElementById("pollBtn"),
  clearBtn: document.getElementById("clearBtn"),
  copyUrlBtn: document.getElementById("copyUrlBtn"),
  submitCallbackBtn: document.getElementById("submitCallbackBtn")
};

let currentSession = null;
let busy = false;
let pollTimer = 0;

function normalizeString(value) {
  return typeof value === "string" ? value.trim() : "";
}

function sendMessage(message) {
  return chrome.runtime.sendMessage(message).then((response) => {
    if (!response?.ok) {
      throw new Error(response?.error || "操作失败");
    }
    return response;
  });
}

function collectSettings() {
  return {
    apiBase: elements.apiBase.value,
    managementKey: elements.managementKey.value,
    provider: elements.provider.value,
    projectId: elements.projectId.value,
    proxyId: elements.proxyId.value,
    autoCloseCallbackTab: elements.autoCloseCallbackTab.checked
  };
}

function setBusy(value) {
  busy = value;
  [
    elements.saveBtn,
    elements.testBtn,
    elements.startBtn,
    elements.pollBtn,
    elements.clearBtn,
    elements.copyUrlBtn,
    elements.submitCallbackBtn
  ].forEach((button) => {
    button.disabled = busy;
  });
}

function setStatus(status, message) {
  const normalized = normalizeString(status) || "neutral";
  elements.statusPill.className = `pill ${normalized}`;
  elements.statusPill.textContent = STATUS_LABELS[normalized] || normalized.toUpperCase();
  elements.statusText.textContent = message || "就绪";
}

function renderSettings(settings = {}) {
  elements.apiBase.value = settings.apiBase || "http://127.0.0.1:8317/v0/management";
  elements.managementKey.value = settings.managementKey || "";
  elements.provider.value = settings.provider || "codex";
  elements.projectId.value = settings.projectId || "";
  elements.proxyId.value = settings.proxyId || "";
  elements.autoCloseCallbackTab.checked = settings.autoCloseCallbackTab !== false;
  renderProviderFields();
}

function renderProviderFields() {
  elements.projectIdRow.classList.toggle("hidden", elements.provider.value !== "gemini-cli");
}

function renderSession(session) {
  currentSession = session || null;
  const hasSession = Boolean(currentSession?.state);
  elements.sessionPanel.classList.toggle("hidden", !hasSession);
  if (!hasSession) {
    setStatus("neutral", "没有正在进行的 OAuth 会话");
    return;
  }

  const providerLabel = PROVIDER_LABELS[currentSession.provider] || currentSession.provider || "OAuth";
  const status = normalizeString(currentSession.status) || "waiting";
  const message = normalizeString(currentSession.message) || "等待授权完成";
  setStatus(status, `${providerLabel}：${message}`);

  elements.stateValue.textContent = currentSession.state || "-";
  elements.authUrl.href = currentSession.authUrl || "#";
  elements.authUrl.textContent = currentSession.authUrl || "没有授权链接";
  if (currentSession.callbackUrl && !normalizeString(elements.callbackUrl.value)) {
    elements.callbackUrl.value = currentSession.callbackUrl;
  }
}

async function refreshState() {
  const response = await sendMessage({ type: "GET_STATE" });
  renderSettings(response.settings);
  renderSession(response.session);
}

async function withBusy(task) {
  if (busy) {
    return;
  }
  setBusy(true);
  try {
    await task();
  } catch (error) {
    setStatus("error", error?.message || String(error));
  } finally {
    setBusy(false);
  }
}

async function saveSettings() {
  const response = await sendMessage({ type: "SAVE_SETTINGS", settings: collectSettings() });
  renderSettings(response.settings);
  setStatus("neutral", "配置已保存");
}

async function testConnection() {
  const response = await sendMessage({ type: "TEST_CONNECTION", settings: collectSettings() });
  renderSettings(response.settings);
  setStatus("success", "CliRelay 管理接口连接正常");
}

async function startOAuth() {
  const response = await sendMessage({ type: "START_OAUTH", settings: collectSettings() });
  renderSession(response.session);
}

async function pollStatus() {
  const response = await sendMessage({ type: "POLL_STATUS" });
  renderSession(response.session);
}

async function clearSession() {
  await sendMessage({ type: "CLEAR_SESSION" });
  renderSession(null);
}

async function copyAuthUrl() {
  const url = normalizeString(currentSession?.authUrl);
  if (!url) {
    setStatus("error", "没有可复制的授权链接");
    return;
  }
  await navigator.clipboard.writeText(url);
  setStatus(normalizeString(currentSession?.status) || "waiting", "授权链接已复制");
}

async function submitCallback() {
  const callbackUrl = normalizeString(elements.callbackUrl.value);
  if (!callbackUrl) {
    setStatus("error", "请先粘贴回调地址");
    return;
  }
  const response = await sendMessage({ type: "SUBMIT_CALLBACK", callbackUrl });
  renderSession(response.session);
}

function startPopupPolling() {
  clearInterval(pollTimer);
  pollTimer = setInterval(() => {
    if (!busy && currentSession?.state && !["success", "error"].includes(currentSession.status)) {
      pollStatus().catch(() => {});
    }
  }, 3000);
}

elements.provider.addEventListener("change", renderProviderFields);
elements.saveBtn.addEventListener("click", () => withBusy(saveSettings));
elements.testBtn.addEventListener("click", () => withBusy(testConnection));
elements.startBtn.addEventListener("click", () => withBusy(startOAuth));
elements.pollBtn.addEventListener("click", () => withBusy(pollStatus));
elements.clearBtn.addEventListener("click", () => withBusy(clearSession));
elements.copyUrlBtn.addEventListener("click", () => withBusy(copyAuthUrl));
elements.submitCallbackBtn.addEventListener("click", () => withBusy(submitCallback));

refreshState()
  .catch((error) => setStatus("error", error?.message || String(error)))
  .finally(startPopupPolling);
