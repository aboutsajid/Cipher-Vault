(() => {
  const INSTALL_FLAG = "__ciphervaultCaptureInstalled";
  if (window[INSTALL_FLAG]) return;
  window[INSTALL_FLAG] = true;

  const recentCaptures = new Map();
  const DEDUPE_WINDOW_MS = 8000;

  const autofillCache = new Map();
  const AUTOFILL_CACHE_MS = 15000;
  const AUTOFILL_LIMIT = 6;

  let autofillPanel = null;
  let panelAnchorField = null;

  document.addEventListener(
    "submit",
    (event) => {
      const form = event.target;
      if (!(form instanceof HTMLFormElement)) return;
      captureWithDelay(form, "submit");
    },
    true
  );

  document.addEventListener(
    "click",
    (event) => {
      const target = event.target instanceof Element ? event.target : null;
      if (!target) return;

      const inputTarget = target.closest("input");
      if (inputTarget instanceof HTMLInputElement && isAutofillTriggerField(inputTarget)) {
        requestAutofillSuggestions(inputTarget);
      }

      const submitControl = target.closest("button, input[type='submit'], input[type='button']");
      if (!submitControl) return;

      const form = resolveForm(submitControl);
      captureWithDelay(form || document, "click");
    },
    true
  );

  document.addEventListener(
    "keydown",
    (event) => {
      if (event.key === "Escape") {
        hideAutofillPanel();
        return;
      }

      if (event.key !== "Enter") return;
      const target = event.target instanceof Element ? event.target : null;
      if (!target) return;

      const form = resolveForm(target);
      captureWithDelay(form || document, "enter");
    },
    true
  );

  document.addEventListener(
    "focusin",
    (event) => {
      const target = event.target;
      if (!(target instanceof HTMLInputElement)) return;
      if (!isAutofillTriggerField(target)) return;

      requestAutofillSuggestions(target);
    },
    true
  );

  document.addEventListener(
    "mousedown",
    (event) => {
      if (!autofillPanel) return;

      const target = event.target;
      if (target instanceof Node && autofillPanel.contains(target)) return;
      hideAutofillPanel();
    },
    true
  );

  window.addEventListener(
    "pagehide",
    () => {
      const forms = Array.from(document.querySelectorAll("form"));
      if (forms.length) {
        forms.forEach((form) => sendCapture(form, "pagehide"));
      } else {
        sendCapture(document, "pagehide");
      }
      hideAutofillPanel();
    },
    true
  );

  window.addEventListener("resize", () => positionAutofillPanel(), true);
  window.addEventListener("scroll", () => positionAutofillPanel(), true);
  document.addEventListener("visibilitychange", maybeAutofillActiveElement, true);
  setTimeout(maybeAutofillActiveElement, 350);

  function captureWithDelay(root, reason) {
    // Capture immediately before sites mutate or clear form fields,
    // then retry shortly after for JS-driven submit flows.
    sendCapture(root, `${reason}:immediate`);
    setTimeout(() => sendCapture(root, reason), 80);
  }

  function sendCapture(root, reason) {
    const payload = buildCapturePayload(root, reason);
    if (!payload) return;

    const now = Date.now();
    const fingerprint = `${payload.url}|${payload.username}|${payload.password}`;
    const previous = recentCaptures.get(fingerprint);
    if (previous && now - previous < DEDUPE_WINDOW_MS) return;

    recentCaptures.set(fingerprint, now);

    chrome.runtime.sendMessage({ type: "cipherpw-capture", payload }, () => {
      void chrome.runtime.lastError;
    });
  }

  function buildCapturePayload(root, reason) {
    const passwordField = findPasswordField(root);
    if (!passwordField || !passwordField.value) return null;

    const usernameField = findUsernameField(passwordField.form || root || document, passwordField)
      || findUsernameField(document, passwordField);

    return {
      title: truncate(document.title || window.location.hostname, 128),
      url: truncate(window.location.href, 1024),
      username: truncate(usernameField?.value || "", 256),
      password: truncate(passwordField.value, 512),
      sourceBrowser: `${detectBrowser()} (${reason})`
    };
  }

  function requestAutofillSuggestions(anchorField) {
    const host = (window.location.hostname || "").toLowerCase();
    if (!host) return;

    const now = Date.now();
    const cached = autofillCache.get(host);
    if (cached && now - cached.time < AUTOFILL_CACHE_MS) {
      showAutofillPanel(anchorField, cached.entries);
      return;
    }

    chrome.runtime.sendMessage(
      {
        type: "ciphervault-autofill-query",
        payload: {
          url: window.location.href,
          limit: AUTOFILL_LIMIT
        }
      },
      (response) => {
        if (chrome.runtime.lastError || !response || !response.ok) return;

        const entries = Array.isArray(response.entries)
          ? response.entries
              .filter((entry) => entry && entry.password)
              .slice(0, AUTOFILL_LIMIT)
          : [];

        autofillCache.set(host, { time: Date.now(), entries });
        showAutofillPanel(anchorField, entries);
      }
    );
  }

  function showAutofillPanel(anchorField, entries) {
    hideAutofillPanel();
    const hasEntries = Array.isArray(entries) && entries.length > 0;

    panelAnchorField = anchorField;

    const panel = document.createElement("div");
    panel.style.position = "fixed";
    panel.style.zIndex = "2147483647";
    panel.style.width = "320px";
    panel.style.maxHeight = "260px";
    panel.style.overflowY = "auto";
    panel.style.background = "#ffffff";
    panel.style.border = "1px solid #c9d8ea";
    panel.style.borderRadius = "10px";
    panel.style.boxShadow = "0 12px 24px rgba(7, 31, 61, 0.18)";
    panel.style.padding = "8px";
    panel.style.fontFamily = "Rajdhani, Segoe UI, Arial, sans-serif";

    const heading = document.createElement("div");
    heading.textContent = "Cipher™ Autofill";
    heading.style.fontSize = "12px";
    heading.style.fontWeight = "600";
    heading.style.color = "#00f5c4";
    heading.style.margin = "2px 4px 8px";
    heading.style.letterSpacing = "0.12em";
    heading.style.borderBottom = "1px solid rgba(0, 245, 196, 0.45)";
    heading.style.paddingBottom = "3px";
    panel.appendChild(heading);

    if (!hasEntries) {
      const empty = document.createElement("div");
      empty.textContent = "No saved account for this site yet.";
      empty.style.fontSize = "11px";
      empty.style.color = "#5c7795";
      empty.style.padding = "6px 4px 8px";
      panel.appendChild(empty);

      const hint = document.createElement("div");
      hint.textContent = "Save once via login, then autofill will appear here.";
      hint.style.fontSize = "10px";
      hint.style.color = "#6b88a5";
      hint.style.padding = "2px 4px";
      panel.appendChild(hint);
    } else {
      entries.forEach((entry) => {
        const button = document.createElement("button");
        button.type = "button";
        button.style.display = "block";
        button.style.width = "100%";
        button.style.textAlign = "left";
        button.style.border = "1px solid #e1edf8";
        button.style.background = "#f7fbff";
        button.style.borderRadius = "8px";
        button.style.padding = "8px";
        button.style.marginBottom = "6px";
        button.style.cursor = "pointer";

        const title = document.createElement("div");
        title.textContent = truncate(entry.title || "Saved Account", 60);
        title.style.fontSize = "12px";
        title.style.fontWeight = "600";
        title.style.color = "#15324d";

        const user = document.createElement("div");
        user.textContent = truncate(entry.username || "(password-only entry)", 70);
        user.style.fontSize = "11px";
        user.style.color = "#4d6e8f";

        button.appendChild(title);
        button.appendChild(user);

        button.addEventListener("mousedown", (event) => {
          event.preventDefault();
        });

        button.addEventListener("click", (event) => {
          event.preventDefault();
          applyAutofill(anchorField, entry);
          hideAutofillPanel();
        });

        panel.appendChild(button);
      });

      const hint = document.createElement("div");
      hint.textContent = "Tip: Choose account to fill. Press Esc to close.";
      hint.style.fontSize = "10px";
      hint.style.color = "#6b88a5";
      hint.style.padding = "2px 4px";
      panel.appendChild(hint);
    }

    document.documentElement.appendChild(panel);
    autofillPanel = panel;
    positionAutofillPanel();
  }

  function hideAutofillPanel() {
    if (autofillPanel && autofillPanel.parentNode) {
      autofillPanel.parentNode.removeChild(autofillPanel);
    }

    autofillPanel = null;
    panelAnchorField = null;
  }

  function positionAutofillPanel() {
    if (!autofillPanel || !panelAnchorField || !panelAnchorField.isConnected) {
      hideAutofillPanel();
      return;
    }

    const rect = panelAnchorField.getBoundingClientRect();
    const panelWidth = 320;
    const margin = 12;

    let left = Math.max(margin, Math.min(rect.left, window.innerWidth - panelWidth - margin));
    let top = rect.bottom + 8;

    const panelHeight = autofillPanel.offsetHeight || 240;
    if (top + panelHeight > window.innerHeight - margin) {
      top = Math.max(margin, rect.top - panelHeight - 8);
    }

    autofillPanel.style.left = `${Math.round(left)}px`;
    autofillPanel.style.top = `${Math.round(top)}px`;
  }

  function applyAutofill(anchorField, entry) {
    const form = resolveForm(anchorField) || document;
    const passwordField = findPasswordField(form) || findPasswordField(document);
    const usernameField =
      findUsernameField(form, passwordField)
      || findUsernameField(document, passwordField)
      || (anchorField instanceof HTMLInputElement && isLikelyUsernameField(anchorField) ? anchorField : null);

    if (usernameField && entry.username) {
      setInputValue(usernameField, entry.username || "");
    }

    if (passwordField) {
      setInputValue(passwordField, entry.password || "");
    }
  }

  function maybeAutofillActiveElement() {
    const active = document.activeElement;
    if (!(active instanceof HTMLInputElement)) return;
    if (!isAutofillTriggerField(active)) return;
    requestAutofillSuggestions(active);
  }

  function isAutofillTriggerField(field) {
    if (!(field instanceof HTMLInputElement)) return false;
    if (field.disabled || field.readOnly) return false;

    const type = (field.type || "").toLowerCase();
    if (type === "password") return true;

    if (!["text", "email", "tel", "search", "url"].includes(type))
      return false;

    if (isLikelyUsernameField(field))
      return true;

    const form = resolveForm(field) || document;
    return !!findPasswordField(form);
  }

  function isLikelyUsernameField(field) {
    if (!(field instanceof HTMLInputElement)) return false;

    const marker = `${field.name} ${field.id} ${field.autocomplete} ${field.placeholder}`.toLowerCase();
    if (
      marker.includes("user")
      || marker.includes("email")
      || marker.includes("login")
      || marker.includes("account")
      || marker.includes("identifier")
      || marker.includes("phone")
      || marker.includes("mobile")
    ) {
      return true;
    }

    const form = resolveForm(field);
    if (!form) return false;

    const formMarker =
      `${form.name} ${form.id} ${form.getAttribute("action") || ""} ${form.getAttribute("aria-label") || ""}`.toLowerCase();
    return formMarker.includes("login")
      || formMarker.includes("signin")
      || formMarker.includes("auth")
      || formMarker.includes("account");
  }

  function findPasswordField(root) {
    const scope = root && typeof root.querySelectorAll === "function" ? root : document;
    const fields = Array.from(scope.querySelectorAll("input[type='password']"))
      .filter((input) => input instanceof HTMLInputElement)
      .filter((input) => !input.disabled && !input.readOnly);

    if (!fields.length) return null;

    const filled = fields.filter((input) => !!input.value);
    return (filled[filled.length - 1] || fields[fields.length - 1]) || null;
  }

  function findUsernameField(root, passwordField) {
    const scope = root && typeof root.querySelectorAll === "function" ? root : document;

    const candidates = Array.from(scope.querySelectorAll("input"))
      .filter((input) => input instanceof HTMLInputElement)
      .filter((input) => input !== passwordField)
      .filter((input) => !input.disabled && !input.readOnly)
      .filter((input) => {
        const type = (input.type || "").toLowerCase();
        return ["text", "email", "tel", "search", "url"].includes(type);
      });

    if (!candidates.length) return null;

    const prioritize = (list) =>
      list.find((input) => {
        const marker = `${input.name} ${input.id} ${input.autocomplete} ${input.placeholder}`.toLowerCase();
        return marker.includes("user")
          || marker.includes("email")
          || marker.includes("login")
          || marker.includes("account")
          || marker.includes("identifier");
      }) || list[0];

    if (passwordField) {
      const beforePassword = candidates.filter((input) => {
        return !!(input.compareDocumentPosition(passwordField) & Node.DOCUMENT_POSITION_FOLLOWING);
      });

      if (beforePassword.length) return prioritize(beforePassword);
    }

    return prioritize(candidates);
  }

  function resolveForm(element) {
    if (element instanceof HTMLFormElement) return element;
    if (element instanceof HTMLInputElement && element.form) return element.form;
    return element.closest("form");
  }

  function setInputValue(input, value) {
    if (!(input instanceof HTMLInputElement)) return;

    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value")?.set;
    if (setter) setter.call(input, value);
    else input.value = value;

    input.dispatchEvent(new Event("input", { bubbles: true, composed: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
    input.focus();
  }

  function detectBrowser() {
    const ua = navigator.userAgent || "";
    if (ua.includes("Edg/")) return "Microsoft Edge";
    if (ua.includes("Chrome/")) return "Google Chrome";
    if (ua.includes("Firefox/")) return "Mozilla Firefox";
    return "Browser";
  }

  function truncate(value, maxLength) {
    if (!value) return "";
    const text = String(value).trim();
    return text.length <= maxLength ? text : text.slice(0, maxLength);
  }
})();



