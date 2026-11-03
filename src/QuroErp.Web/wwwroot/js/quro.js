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

// App shell (app-shell.md 3). The layout is static, so the menu toggle, group folding, collapse and the narrow-screen overlay
// are plain DOM handlers. Folded groups and the collapsed state stay in localStorage; enhanced navigation redraws the shell
// from the server, so the state is applied again after every page load.
(() => {
    const collapsedKey = "quro.shell.collapsed";
    const foldedKey = "quro.shell.folded";
    const narrow = window.matchMedia("(max-width: 767.98px)");
    const local = () => { try { return window.localStorage; } catch { return null; } };

    const shell = () => document.querySelector("[data-shell]");
    const toggle = () => document.querySelector("[data-shell-toggle]");
    const nav = () => document.querySelector("[data-shell-nav]");

    const folded = () => {
        try { return new Set(JSON.parse(local()?.getItem(foldedKey) ?? "[]")); } catch { return new Set(); }
    };

    const setGroup = (group, open) => group.querySelector("[data-shell-group-toggle]")?.setAttribute("aria-expanded", String(open));

    const setCollapsed = (collapsed) => {
        const el = shell();
        if (!el) return;
        el.toggleAttribute("data-shell-collapsed", collapsed);
        const button = document.querySelector("[data-shell-collapse]");
        if (button) {
            button.setAttribute("aria-pressed", String(collapsed));
            button.setAttribute("aria-label", collapsed ? button.dataset.expandLabel : button.dataset.collapseLabel);
        }
    };

    const setOpen = (open) => {
        const el = shell();
        if (!el) return;
        el.toggleAttribute("data-shell-open", open);
        toggle()?.setAttribute("aria-expanded", String(open));
        const backdrop = document.querySelector("[data-shell-backdrop]");
        if (backdrop) backdrop.hidden = !open;
        if (open) nav()?.querySelector("a, button")?.focus();
    };

    const close = () => {
        if (!shell()?.hasAttribute("data-shell-open")) return;
        setOpen(false);
        toggle()?.focus();
    };

    const init = () => {
        if (!shell()) return;
        const closed = folded();
        // The group of the open screen always opens (app-shell.md 3)
        document.querySelectorAll("[data-shell-group]").forEach((group) =>
            setGroup(group, group.dataset.current === "true" || !closed.has(group.dataset.shellGroup)));
        setCollapsed(local()?.getItem(collapsedKey) === "1");
        setOpen(false);
    };

    document.addEventListener("click", (e) => {
        const target = e.target;
        if (!target.closest || !shell()) return;
        if (target.closest("[data-shell-toggle]")) {
            setOpen(!shell().hasAttribute("data-shell-open"));
            return;
        }
        if (target.closest("[data-shell-backdrop]")) { close(); return; }
        if (target.closest("[data-shell-collapse]")) {
            const collapsed = !shell().hasAttribute("data-shell-collapsed");
            local()?.setItem(collapsedKey, collapsed ? "1" : "0");
            setCollapsed(collapsed);
            return;
        }
        const groupToggle = target.closest("[data-shell-group-toggle]");
        if (groupToggle) {
            const group = groupToggle.closest("[data-shell-group]");
            // A group icon of the collapsed menu expands the menu with that group open
            if (shell().hasAttribute("data-shell-collapsed") && !narrow.matches) {
                local()?.setItem(collapsedKey, "0");
                setCollapsed(false);
                setGroup(group, true);
            } else {
                setGroup(group, groupToggle.getAttribute("aria-expanded") !== "true");
            }
            const closed = folded();
            const code = group.dataset.shellGroup;
            if (groupToggle.getAttribute("aria-expanded") === "true") closed.delete(code); else closed.add(code);
            local()?.setItem(foldedKey, JSON.stringify([...closed]));
            return;
        }
        // Picking a screen closes the overlay
        if (target.closest("[data-shell-nav] a")) setOpen(false);
    });

    // The overlay closes on Esc and keeps focus inside the menu
    document.addEventListener("keydown", (e) => {
        if (!shell()?.hasAttribute("data-shell-open")) return;
        if (e.key === "Escape") { e.preventDefault(); close(); return; }
        if (e.key !== "Tab") return;
        const items = [...(nav()?.querySelectorAll("a, button") ?? [])].filter((el) => el.offsetParent !== null);
        if (items.length === 0) return;
        const first = items[0];
        const last = items[items.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
        else if (!nav().contains(document.activeElement)) { e.preventDefault(); first.focus(); }
    });

    document.addEventListener("change", (e) => {
        if (e.target.matches?.("[data-shell-language]")) e.target.form.requestSubmit();
    });

    narrow.addEventListener?.("change", () => setOpen(false));

    document.addEventListener("DOMContentLoaded", () => {
        init();
        window.Blazor?.addEventListener?.("enhancedload", init);
    });
})();

// Entry frame shortcuts (entry-frame.md 2): F3 search, Ctrl+S save, Ctrl+Insert add row, Ctrl+Delete mark delete.
// One entry frame is on a page; it registers while mounted and disposes the returned reference when it goes.
window.quroEntryShortcuts = (frame) => {
    const commands = (e) => {
        if (e.key === "F3" && !e.ctrlKey && !e.altKey) return "search";
        if (!e.ctrlKey || e.altKey || e.shiftKey) return null;
        if (e.key === "s" || e.key === "S") return "save";
        if (e.key === "Insert") return "add-row";
        if (e.key === "Delete") return "mark-delete";
        return null;
    };
    const onKeyDown = (e) => {
        const command = commands(e);
        if (!command || document.querySelector(".quro-dialog-backdrop")) return;
        e.preventDefault();
        // An edited input commits its value on change, so let it fire before the command runs
        const input = document.activeElement;
        if (input?.matches?.("input:not([type=checkbox]):not([type=radio]), textarea")) {
            input.dispatchEvent(new Event("change", { bubbles: true }));
        }
        frame.invokeMethodAsync("OnShortcut", command);
    };
    document.addEventListener("keydown", onKeyDown);
    return DotNet.createJSObjectReference({ dispose: () => document.removeEventListener("keydown", onKeyDown) });
};
