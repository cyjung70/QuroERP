// Browser helpers for the Quro screen components.

// Saves a file sent from .NET (DotNetStreamReference), e.g. the Excel export of a Multi grid.
window.quroDownload = async (fileName, streamReference) => {
    const buffer = await streamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer]));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
};

// Submits a normal (non-interactive) form, e.g. the culture form after the my info language changes.
window.quroSubmit = (form) => form.requestSubmit();

// Login screen (BC-UC-001 items 2-8). The page is static SSR, so these are plain DOM handlers on document.
// Remember ID keeps only the user ID in localStorage, and only after a successful sign-in (item 6).
(() => {
    const savedIdKey = "quro.login.userId";
    const pendingIdKey = "quro.login.pendingUserId";
    const loginPath = "/bc/login";
    const storage = (area) => { try { return window[area]; } catch { return null; } };

    // Runs on every page load: a sign-in that left the login page succeeded, one that came back to it failed
    const settleRememberedId = () => {
        const session = storage("sessionStorage");
        const pending = session?.getItem(pendingIdKey);
        if (pending === null || pending === undefined) return;
        session.removeItem(pendingIdKey);
        if (location.pathname.toLowerCase() === loginPath) return;
        const local = storage("localStorage");
        if (pending === "") local?.removeItem(savedIdKey); else local?.setItem(savedIdKey, pending);
    };

    const initLoginForm = () => {
        settleRememberedId();
        const form = document.querySelector("[data-login-form]");
        if (!form || form.dataset.loginReady) return;
        form.dataset.loginReady = "1";
        const userId = form.querySelector("[data-login-user-id]");
        const password = form.querySelector("[data-login-password]");
        const remember = form.querySelector("[data-login-remember]");
        const saved = storage("localStorage")?.getItem(savedIdKey);
        if (saved && remember) {
            remember.checked = true;
            if (!userId.value) userId.value = saved;
        }
        (userId.value ? password : userId).focus();
    };

    document.addEventListener("click", (e) => {
        const toggle = e.target.closest?.("[data-login-toggle]");
        if (!toggle) return;
        const password = document.querySelector("[data-login-password]");
        const show = password.type === "password";
        password.type = show ? "text" : "password";
        toggle.setAttribute("aria-pressed", String(show));
        toggle.setAttribute("aria-label", show ? toggle.dataset.hideLabel : toggle.dataset.showLabel);
    });

    // Caps Lock warning only while the password field has focus (item 5, AUTH-009)
    const capsWarning = () => document.querySelector("[data-login-caps]");
    const showCaps = (e) => {
        if (!e.target.matches?.("[data-login-password]") || !capsWarning()) return;
        capsWarning().hidden = !e.getModifierState?.("CapsLock");
    };
    document.addEventListener("keydown", showCaps);
    document.addEventListener("keyup", showCaps);
    document.addEventListener("focusout", (e) => {
        if (e.target.matches?.("[data-login-password]") && capsWarning()) capsWarning().hidden = true;
    });

    // Typing again clears the server message and the field's own message (item 9)
    document.addEventListener("input", (e) => {
        if (!e.target.closest?.("[data-login-form]")) return;
        const error = document.querySelector("[data-login-error]");
        if (error) { error.hidden = true; error.textContent = ""; }
        const fieldError = e.target.closest(".login-field")?.querySelector("[data-login-field-error]");
        if (fieldError) fieldError.textContent = "";
    });

    document.addEventListener("change", (e) => {
        if (e.target.matches?.("[data-login-keep]")) {
            const warning = document.querySelector("[data-login-keep-warning]");
            if (warning) warning.hidden = !e.target.checked;
        }
        if (e.target.matches?.("[data-login-language]")) e.target.form.requestSubmit();
    });

    // Item 8: one request at a time; the password is hidden again; remember ID is settled after the answer
    document.addEventListener("submit", (e) => {
        const form = e.target.closest?.("[data-login-form]");
        if (!form) return;
        const password = form.querySelector("[data-login-password]");
        password.type = "password";
        form.querySelector("[data-login-toggle]")?.setAttribute("aria-pressed", "false");
        const remember = form.querySelector("[data-login-remember]");
        const userId = form.querySelector("[data-login-user-id]").value.trim().toLowerCase();
        storage("sessionStorage")?.setItem(pendingIdKey, remember?.checked ? userId : "");
        const button = form.querySelector("[type=submit]");
        if (button) {
            // Disabled after the submit event so the form still posts
            setTimeout(() => {
                button.disabled = true;
                button.setAttribute("aria-busy", "true");
                button.textContent = form.dataset.busyText;
            });
        }
    }, true);

    document.addEventListener("DOMContentLoaded", () => {
        initLoginForm();
        window.Blazor?.addEventListener?.("enhancedload", initLoginForm);
    });
})();

// Idle session (login.md "세션"). The server keeps the idle time: requests in any tab and Api calls of interactive screens
// extend it, so the timer asks the server before warning and never decides on its own clock alone.
(() => {
    const retryMs = 60000;
    const recheckMs = 30000;
    let handle = 0;

    const timer = () => document.querySelector("[data-session-timer]");

    const expire = (el) => {
        const returnUrl = location.pathname + location.search;
        location.assign(`${el.dataset.expiredUrl}?returnUrl=${encodeURIComponent(returnUrl)}`);
    };

    // remainingSeconds and warnSeconds from the server; 401 when the session already ended
    const schedule = async () => {
        clearTimeout(handle);
        const el = timer();
        if (!el) return;
        let status;
        try {
            const response = await fetch(el.dataset.statusUrl, { headers: { Accept: "application/json" }, credentials: "same-origin" });
            if (response.status === 401) { expire(el); return; }
            if (!response.ok) throw new Error(String(response.status));
            status = await response.json();
        } catch {
            handle = setTimeout(schedule, retryMs);
            return;
        }
        if (status.remainingSeconds <= 0) { expire(el); return; }
        const warn = status.remainingSeconds <= status.warnSeconds;
        el.hidden = !warn;
        if (warn && !el.contains(document.activeElement)) el.querySelector("[data-session-extend]")?.focus();
        const waitMs = warn
            ? Math.min(status.remainingSeconds * 1000, recheckMs)
            : (status.remainingSeconds - status.warnSeconds) * 1000;
        handle = setTimeout(schedule, waitMs);
    };

    document.addEventListener("click", async (e) => {
        if (!e.target.closest?.("[data-session-extend]")) return;
        const el = timer();
        const body = new FormData();
        body.append("__RequestVerificationToken", el.querySelector("input[name=__RequestVerificationToken]")?.value ?? "");
        try {
            await fetch(el.dataset.extendUrl, { method: "POST", body, credentials: "same-origin" });
        } catch {
            // The next status check shows what the server has
        }
        schedule();
    });

    const start = () => { if (timer()) schedule(); else clearTimeout(handle); };
    document.addEventListener("DOMContentLoaded", () => {
        start();
        window.Blazor?.addEventListener?.("enhancedload", start);
    });
})();
