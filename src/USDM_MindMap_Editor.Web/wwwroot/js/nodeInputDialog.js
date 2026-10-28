const dialogs = new WeakMap();

export function showNodeDialog(dialog, dotnet) {
    if (dialogs.has(dialog)) return;
    const opener = document.activeElement;
    const events = new AbortController();
    dialog.addEventListener('cancel', event => {
        event.preventDefault();
        dotnet.invokeMethodAsync('CancelFromBrowser').catch(() => {});
    }, { signal: events.signal });
    dialog.addEventListener('keydown', event => {
        if (event.key !== 'Tab' || event.altKey || event.ctrlKey || event.metaKey) return;
        const controls = [...dialog.querySelectorAll('button:not(:disabled), textarea:not(:disabled), input:not(:disabled), select:not(:disabled), a[href], [tabindex]:not([tabindex="-1"])')]
            .filter(control => control.getClientRects().length > 0);
        const first = controls[0];
        const last = controls.at(-1);
        if (!first) { event.preventDefault(); return; }
        const active = document.activeElement;
        if (event.shiftKey && (active === first || !controls.includes(active))) {
            event.preventDefault();
            last.focus({ preventScroll: true });
        } else if (!event.shiftKey && (active === last || !controls.includes(active))) {
            event.preventDefault();
            first.focus({ preventScroll: true });
        }
    }, { signal: events.signal });
    dialogs.set(dialog, { events, opener });
    dialog.showModal();
    dialog.querySelector('textarea')?.focus({ preventScroll: true });
}

export function closeNodeDialog(dialog) {
    const state = dialogs.get(dialog);
    if (!state) return;
    state.events.abort();
    dialog.close();
    if (state.opener?.isConnected) state.opener.focus({ preventScroll: true });
    dialogs.delete(dialog);
}
