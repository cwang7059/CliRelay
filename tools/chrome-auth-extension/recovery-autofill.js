(function clirelayRecoveryAutofill() {
  const AUTOFILL_MARK = "clirelayRecoveryAutofilled";

  function normalizeString(value) {
    return typeof value === "string" ? value.trim() : "";
  }

  function currentState() {
    try {
      return normalizeString(new URL(location.href).searchParams.get("state"));
    } catch {
      return "";
    }
  }

  function isVisible(element) {
    if (!element) {
      return false;
    }
    const style = getComputedStyle(element);
    if (style.display === "none" || style.visibility === "hidden") {
      return false;
    }
    const rect = element.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  }

  function setInputValue(input, value) {
    if (!input || value === undefined || value === null) {
      return false;
    }
    const prototype = input instanceof HTMLTextAreaElement
      ? HTMLTextAreaElement.prototype
      : HTMLInputElement.prototype;
    const descriptor = Object.getOwnPropertyDescriptor(prototype, "value");
    if (descriptor?.set) {
      descriptor.set.call(input, value);
    } else {
      input.value = value;
    }
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
    return true;
  }

  function isEmptyInput(input) {
    return normalizeString(input?.value) === "";
  }

  function clickElement(element) {
    if (!element || element.disabled) {
      return false;
    }
    element.dispatchEvent(new MouseEvent("mousedown", { bubbles: true }));
    element.dispatchEvent(new MouseEvent("mouseup", { bubbles: true }));
    element.click();
    return true;
  }

  function fieldScore(input, mode) {
    const text = [
      input.id,
      input.name,
      input.type,
      input.autocomplete,
      input.placeholder,
      input.getAttribute("aria-label"),
      input.getAttribute("data-testid"),
    ].map((item) => normalizeString(item).toLowerCase()).join(" ");
    if (mode === "password") {
      return input.type === "password" ? 100 : /password|passcode/.test(text) ? 60 : 0;
    }
    if (input.type === "password" || input.type === "hidden") {
      return 0;
    }
    if (/email|username|identifier|login|account/.test(text)) {
      return 100;
    }
    if (!isEmptyInput(input)) {
      return 0;
    }
    if (["email", "text", "search"].includes(input.type || "text")) {
      return 30;
    }
    return 0;
  }

  function findInput(mode) {
    return Array.from(document.querySelectorAll("input, textarea"))
      .filter(isVisible)
      .map((input) => ({ input, score: fieldScore(input, mode) }))
      .filter((item) => item.score > 0)
      .sort((left, right) => right.score - left.score)[0]?.input || null;
  }

  function findSubmitButton() {
    const candidates = Array.from(document.querySelectorAll("button, input[type='submit'], [role='button']"))
      .filter(isVisible)
      .filter((element) => !element.disabled && element.getAttribute("aria-disabled") !== "true");
    return candidates.find((element) => {
      const text = normalizeString([
        element.innerText,
        element.textContent,
        element.value,
        element.getAttribute("aria-label"),
        element.getAttribute("data-testid"),
      ].join(" ")).toLowerCase();
      return /continue|next|log in|login|sign in|submit|authorize|allow|继续|下一步|登录|授权|允许/.test(text);
    }) || candidates[0] || null;
  }

  async function waitFor(condition, timeoutMs = 12000) {
    const started = Date.now();
    while (Date.now() - started < timeoutMs) {
      const result = condition();
      if (result) {
        return result;
      }
      await new Promise((resolve) => setTimeout(resolve, 300));
    }
    return null;
  }

  function requestCredentials(state) {
    return chrome.runtime.sendMessage({ type: "GET_RECOVERY_CREDENTIALS", state })
      .then((response) => response?.ok ? response.credentials : null)
      .catch(() => null);
  }

  async function autofillOnce(credentials, state) {
    if (!credentials?.email || document.documentElement.dataset[AUTOFILL_MARK] === state) {
      return;
    }
    document.documentElement.dataset[AUTOFILL_MARK] = state;

    const emailInput = await waitFor(() => findInput("email"));
    if (emailInput) {
      emailInput.focus();
      setInputValue(emailInput, credentials.email);
      clickElement(findSubmitButton());
    }

    if (credentials.password) {
      const passwordInput = await waitFor(() => findInput("password"));
      if (passwordInput) {
        passwordInput.focus();
        setInputValue(passwordInput, credentials.password);
        setTimeout(() => clickElement(findSubmitButton()), 350);
      }
    }
  }

  async function run() {
    const state = currentState();
    if (!state) {
      return;
    }
    const credentials = await requestCredentials(state);
    if (!credentials?.email) {
      return;
    }
    await autofillOnce(credentials, state);
  }

  chrome.runtime.onMessage.addListener((message) => {
    if (message?.type !== "CLIRELAY_RECOVERY_CREDENTIALS") {
      return;
    }
    const state = normalizeString(message.state) || currentState();
    autofillOnce({ email: message.email, password: message.password }, state).catch(() => {});
  });

  run().catch(() => {});
})();
