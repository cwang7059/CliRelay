const PROVIDER_LABELS = {
  codex: "Codex",
  anthropic: "Anthropic",
  antigravity: "Antigravity",
  "gemini-cli": "Gemini CLI",
  qwen: "Qwen",
  kimi: "Kimi"
};

const STATUS_BADGES = {
  waiting: "WAIT",
  captured: "SEND",
  submitting: "SEND",
  success: "OK",
  error: "ERR"
};

const elements = {
  title: document.getElementById("title"),
  badge: document.getElementById("badge"),
  message: document.getElementById("message"),
  providerValue: document.getElementById("providerValue"),
  stateValue: document.getElementById("stateValue"),
  callbackValue: document.getElementById("callbackValue"),
  callbackInput: document.getElementById("callbackInput"),
  submitCallbackBtn: document.getElementById("submitCallbackBtn"),
  restartBtn: document.getElementById("restartBtn"),
  openAuthBtn: document.getElementById("openAuthBtn"),
  clearBtn: document.getElementById("clearBtn"),
  steps: [
    document.getElementById("stepApi"),
    document.getElementById("stepStart"),
    document.getElementById("stepAuth"),
    document.getElementById("stepDone")
  ]
};

let settings = null;
let session = null;
let busy = false;
let pollTimer = 0;
let autostarted = false;

function normalizeString(value) {
  return typeof value === "string" ? value.trim() : "";
}

function isTerminalStatus(status) {
  return status === "success" || status === "error";
}

function sendMessage(message) {
  return chrome.runtime.sendMessage(message).then((response) => {
    if (!response?.ok) {
      throw new Error(response?.error || "操作失败");
    }
    return response;
  });
}

function setBusy(value) {
  busy = value;
  [
    elements.submitCallbackBtn,
    elements.restartBtn,
    elements.openAuthBtn,
    elements.clearBtn
  ].forEach((button) => {
    button.disabled = busy;
  });
}

function setSteps(index, status) {
  elements.steps.forEach((step, stepIndex) => {
    step.className = "dot";
    if (status === "error" && stepIndex === index) {
      step.classList.add("error");
    } else if (stepIndex < index) {
      step.classList.add("done");
    } else if (stepIndex === index) {
      step.classList.add(status === "done" ? "done" : "active");
    }
  });
}

function renderStatus(status, message) {
  const normalized = normalizeString(status) || "waiting";
  elements.badge.className = `badge ${normalized}`;
  elements.badge.textContent = STATUS_BADGES[normalized] || normalized.toUpperCase();
  elements.message.textContent = message || "等待中";
}

function renderSession(nextSession) {
  session = nextSession || null;
  const provider = session?.provider || settings?.provider || "codex";
  const providerLabel = PROVIDER_LABELS[provider] || provider;
  const status = normalizeString(session?.status) || "waiting";
  const message = normalizeString(session?.message) || "等待授权完成";

  elements.providerValue.textContent = providerLabel;
  elements.stateValue.textContent = session?.state || "-";
  elements.callbackValue.textContent = session?.callbackUrl ? "已捕获" : "等待中";
  if (session?.callbackUrl && !normalizeString(elements.callbackInput.value)) {
    elements.callbackInput.value = session.callbackUrl;
  }

  if (!session?.state) {
    elements.title.textContent = "准备启动 OAuth";
    renderStatus("waiting", "正在准备 OAuth 会话...");
    setSteps(0, "active");
    return;
  }

  if (status === "success") {
    elements.title.textContent = "认证完成";
    renderStatus("success", `${providerLabel} OAuth 已导入 CliRelay`);
    setSteps(3, "done");
  } else if (status === "error") {
    elements.title.textContent = "认证失败";
    renderStatus("error", message);
    setSteps(3, "error");
  } else if (status === "captured" || status === "submitting") {
    elements.title.textContent = "正在提交回调";
    renderStatus(status, message);
    setSteps(2, "active");
  } else {
    elements.title.textContent = "等待浏览器授权";
    renderStatus("waiting", message);
    setSteps(2, "active");
  }
}

async function loadState() {
  const response = await sendMessage({ type: "GET_STATE" });
  settings = response.settings || {};
  renderSession(response.session);
}

async function startOAuth() {
  setSteps(0, "active");
  const testResponse = await sendMessage({ type: "TEST_CONNECTION", settings });
  settings = testResponse.settings || settings;
  setSteps(1, "active");
  const response = await sendMessage({ type: "START_OAUTH", settings });
  renderSession(response.session);
}

async function pollStatus() {
  if (!session?.state || isTerminalStatus(session.status)) {
    return;
  }
  const response = await sendMessage({ type: "POLL_STATUS" });
  renderSession(response.session);
}

async function submitCallback() {
  const callbackUrl = normalizeString(elements.callbackInput.value);
  if (!callbackUrl) {
    renderStatus("error", "请先粘贴回调地址");
    return;
  }
  const response = await sendMessage({ type: "SUBMIT_CALLBACK", callbackUrl });
  renderSession(response.session);
}

async function restart() {
  await sendMessage({ type: "CLEAR_SESSION" });
  session = null;
  await startOAuth();
}

async function clearSession() {
  await sendMessage({ type: "CLEAR_SESSION" });
  session = null;
  renderSession(null);
}

function openAuthUrl() {
  const authUrl = normalizeString(session?.authUrl);
  if (authUrl) {
    chrome.tabs.create({ url: authUrl, active: true });
  }
}

async function withBusy(task) {
  if (busy) {
    return;
  }
  setBusy(true);
  try {
    await task();
  } catch (error) {
    elements.title.textContent = "自动启动失败";
    renderStatus("error", error?.message || String(error));
    setSteps(0, "error");
  } finally {
    setBusy(false);
  }
}

function startPolling() {
  clearInterval(pollTimer);
  pollTimer = setInterval(() => {
    if (!busy) {
      pollStatus().catch(() => {});
    }
  }, 3000);
}

async function init() {
  await loadState();
  const params = new URLSearchParams(location.search);
  if (params.get("autostart") === "1" && !autostarted) {
    autostarted = true;
    await restart();
  }
}

elements.submitCallbackBtn.addEventListener("click", () => withBusy(submitCallback));
elements.restartBtn.addEventListener("click", () => withBusy(restart));
elements.openAuthBtn.addEventListener("click", openAuthUrl);
elements.clearBtn.addEventListener("click", () => withBusy(clearSession));

init()
  .catch((error) => {
    elements.title.textContent = "初始化失败";
    renderStatus("error", error?.message || String(error));
    setSteps(0, "error");
  })
  .finally(startPolling);
