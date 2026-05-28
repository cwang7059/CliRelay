const http = require("http");
const fs = require("fs");
const { spawn } = require("child_process");

const API_BASE = (process.env.CLIRELAY_MANAGEMENT_BASE || "http://127.0.0.1:8317/v0/management").replace(/\/+$/, "");
const MANAGEMENT_KEY = process.env.CLIRELAY_MANAGEMENT_KEY || "admin123";
const PROVIDER = (process.env.CLIRELAY_OAUTH_PROVIDER || "codex").trim().toLowerCase();
const CALLBACK_PORT = Number(process.env.CLIRELAY_OAUTH_CALLBACK_PORT || 1455);
const CALLBACK_HOST = process.env.CLIRELAY_OAUTH_CALLBACK_HOST || "localhost";
const POLL_INTERVAL_MS = 3000;
const POLL_TIMEOUT_MS = 10 * 60 * 1000;

const WEBUI_PROVIDERS = new Set(["codex", "anthropic", "antigravity", "gemini-cli"]);
const CALLBACK_PROVIDER_MAP = {
  "gemini-cli": "gemini"
};

function log(message) {
  console.log(`[CliRelay OAuth] ${message}`);
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function headers() {
  return {
    Authorization: `Bearer ${MANAGEMENT_KEY}`,
    "Content-Type": "application/json"
  };
}

async function readJsonResponse(response) {
  const text = await response.text();
  if (!text) {
    return {};
  }
  try {
    return JSON.parse(text);
  } catch {
    return { raw: text };
  }
}

function apiError(status, payload) {
  if (payload && typeof payload === "object") {
    return payload.error || payload.message || payload.detail || `HTTP ${status}`;
  }
  return `HTTP ${status}`;
}

async function requestJson(path, options = {}) {
  const response = await fetch(`${API_BASE}${path}`, {
    method: options.method || "GET",
    headers: headers(),
    body: options.body ? JSON.stringify(options.body) : undefined
  });
  const payload = await readJsonResponse(response);
  if (!response.ok) {
    throw new Error(apiError(response.status, payload));
  }
  return payload;
}

function extractAuthUrl(payload) {
  return String(payload?.url || payload?.auth_url || payload?.authUrl || "").trim();
}

function buildStartPath() {
  const params = new URLSearchParams();
  if (WEBUI_PROVIDERS.has(PROVIDER)) {
    params.set("is_webui", "true");
  }
  const query = params.toString();
  return `/${PROVIDER}-auth-url${query ? `?${query}` : ""}`;
}

function findChrome() {
  const candidates = [
    `${process.env.ProgramFiles || "C:\\Program Files"}\\Google\\Chrome\\Application\\chrome.exe`,
    `${process.env["ProgramFiles(x86)"] || "C:\\Program Files (x86)"}\\Google\\Chrome\\Application\\chrome.exe`,
    `${process.env.LOCALAPPDATA || ""}\\Google\\Chrome\\Application\\chrome.exe`
  ];
  return candidates.find((candidate) => candidate && fs.existsSync(candidate));
}

function openChrome(url) {
  const chrome = findChrome();
  if (chrome) {
    const child = spawn(chrome, ["--new-window", url], {
      detached: true,
      stdio: "ignore"
    });
    child.unref();
    return;
  }
  const child = spawn("cmd.exe", ["/c", "start", "", url], {
    detached: true,
    stdio: "ignore"
  });
  child.unref();
}

function callbackHtml(title, message) {
  return `<!doctype html>
<meta charset="utf-8">
<title>${title}</title>
<style>
body{font-family:Segoe UI,Microsoft YaHei,sans-serif;background:#f6f8fb;color:#111827;margin:0;display:grid;min-height:100vh;place-items:center}
main{width:min(680px,calc(100vw - 40px));background:#fff;border:1px solid #dbe3ef;border-radius:8px;padding:28px;box-shadow:0 24px 80px rgba(15,23,42,.14)}
h1{margin:0 0 12px;font-size:28px}p{margin:0;color:#475569;font-size:16px;line-height:1.6}
</style>
<main><h1>${title}</h1><p>${message}</p></main>`;
}

function createCallbackServer(expectedState) {
  let handled = false;

  return new Promise((resolve, reject) => {
    const server = http.createServer((req, res) => {
      const requestUrl = new URL(req.url, `http://${CALLBACK_HOST}:${CALLBACK_PORT}`);
      if (requestUrl.pathname !== "/auth/callback") {
        res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
        res.end("Not found");
        return;
      }

      const state = requestUrl.searchParams.get("state") || "";
      const code = requestUrl.searchParams.get("code") || "";
      const error = requestUrl.searchParams.get("error") || requestUrl.searchParams.get("error_description") || "";

      if (expectedState && state !== expectedState) {
        res.writeHead(400, { "Content-Type": "text/html; charset=utf-8" });
        res.end(callbackHtml("OAuth 回调不匹配", "state 和当前会话不一致，请关闭此页后重新启动脚本。"));
        return;
      }
      if (!code && !error) {
        res.writeHead(400, { "Content-Type": "text/html; charset=utf-8" });
        res.end(callbackHtml("OAuth 回调缺少授权码", "没有收到 code 或 error 参数，请重新授权。"));
        return;
      }
      if (handled) {
        res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
        res.end(callbackHtml("OAuth 回调已收到", "可以关闭这个页面。"));
        return;
      }

      handled = true;
      const redirectUrl = `http://${CALLBACK_HOST}:${CALLBACK_PORT}${req.url}`;
      res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
      res.end(callbackHtml("OAuth 回调已收到", "正在自动提交给 CliRelay，可以关闭这个页面。"));
      resolve({ server, redirectUrl, state, code, error });
    });

    server.on("error", (error) => {
      if (error.code === "EADDRINUSE") {
        reject(new Error(`端口 ${CALLBACK_PORT} 已被占用，请关闭旧的 OAuth 自动化窗口后重试。`));
      } else {
        reject(error);
      }
    });
    server.listen(CALLBACK_PORT, CALLBACK_HOST, () => {
      log(`已监听 http://${CALLBACK_HOST}:${CALLBACK_PORT}/auth/callback`);
    });
  });
}

async function pollStatus(state) {
  const startedAt = Date.now();
  while (Date.now() - startedAt < POLL_TIMEOUT_MS) {
    const payload = await requestJson(`/get-auth-status?state=${encodeURIComponent(state)}`);
    if (payload.status === "ok") {
      return payload;
    }
    if (payload.status === "error") {
      throw new Error(payload.error || "CliRelay 返回认证失败");
    }
    await sleep(POLL_INTERVAL_MS);
  }
  throw new Error("等待 CliRelay 处理 OAuth 超时");
}

async function main() {
  log(`管理接口：${API_BASE}`);
  log(`提供商：${PROVIDER}`);
  await requestJson("/auth-files");
  log("CliRelay 管理接口连接正常");

  const startPayload = await requestJson(buildStartPath());
  const authUrl = extractAuthUrl(startPayload);
  const state = String(startPayload.state || new URL(authUrl).searchParams.get("state") || "").trim();
  if (!authUrl || !state) {
    throw new Error("CliRelay 没有返回有效 OAuth 授权链接");
  }

  const callbackPromise = createCallbackServer(state);
  log("正在打开 Chrome 授权页");
  openChrome(authUrl);

  const callback = await callbackPromise;
  log("已收到 OAuth 回调，正在提交给 CliRelay");
  try {
    const provider = CALLBACK_PROVIDER_MAP[PROVIDER] || PROVIDER;
    await requestJson("/oauth-callback", {
      method: "POST",
      body: {
        provider,
        redirect_url: callback.redirectUrl
      }
    });
  } finally {
    callback.server.close();
  }

  log("回调已提交，等待 CliRelay 完成导入");
  await pollStatus(state);
  log("认证成功，OAuth 已导入 CliRelay。");
}

main().catch((error) => {
  console.error(`[CliRelay OAuth] 失败：${error.message}`);
  process.exitCode = 1;
});
