// Read-only instrumentation for /feedback. Never shipped by the production host.
(() => {
    const panel = document.getElementById('feedback-diagnostics');
    if (!panel) return;
    let pointer = null;
    let previousOpen = false;
    let closeSnapshot = '';
    let closeCaptureUntil = 0;
    const inputEvents = [];
    for (const type of ['pointerdown', 'pointerup', 'click', 'keydown']) {
        document.addEventListener(type, event => {
            closeCaptureUntil = 0;
            const target = event.target.closest?.('.child-add-region, dialog, button');
            if (!target) return;
            inputEvents.push(`${type}: ${target.dataset.parentKey || target.getAttribute('aria-label') || target.tagName} detail=${event.detail ?? '-'} key=${event.key || '-'} pointer=${event.pointerType || '-'}`);
            if (inputEvents.length > 6) inputEvents.shift();
        }, true);
    }
    document.addEventListener('pointermove', event => {
        closeCaptureUntil = 0;
        pointer = { x: event.clientX, y: event.clientY };
    }, true);
    const sample = () => {
        const canvas = document.querySelector('.mindmap-canvas');
        const open = !!document.querySelector('dialog[open]');
        const regions = [...document.querySelectorAll('.child-add-region')].map(region => {
            const style = getComputedStyle(region);
            const rect = region.closest('.node').getBoundingClientRect();
            const inside = pointer && ((pointer.x - rect.left - rect.width / 2) / (rect.width / 4)) ** 2
                + ((pointer.y - rect.bottom) / 5) ** 2 <= 1;
            return `${region.dataset.parentKey}: focus=${document.activeElement === region} hover=${region.matches(':hover')} fv=${region.matches(':focus-visible')} inside=${!!inside}\n`
                + `  class=${region.className}\n  fill=${style.backgroundColor} ring=${style.boxShadow} title=${JSON.stringify(region.title)}`;
        });
        return `dialog=${open} visibility=${document.visibilityState} active=${document.hasFocus()}\n`
            + `rootTitle=${JSON.stringify(canvas?.title)} zoom=${document.querySelector('.zoom-status')?.textContent}\n` + regions.join('\n');
    };
    const refresh = () => {
        const open = !!document.querySelector('dialog[open]');
        if (previousOpen && !open) closeCaptureUntil = performance.now() + 500;
        previousOpen = open;
        // Include the following focus restoration and asynchronous close notification,
        // but stop capturing before the next user input can re-enable hover.
        if (closeCaptureUntil && performance.now() <= closeCaptureUntil) closeSnapshot = sample();
        const text = `強調診断（読取専用）\n${sample()}\n\n直近の終了直後:\n${closeSnapshot || '未終了'}\n\n入力イベント:\n${inputEvents.join('\n')}\n${navigator.userAgent}`;
        if (panel.textContent !== text) panel.textContent = text;
    };
    const observer = new MutationObserver(records => {
        if (records.some(record => !panel.contains(record.target))) refresh();
    });
    observer.observe(document.body, { subtree: true, childList: true, attributes: true,
        attributeFilter: ['class', 'style', 'open', 'title'] });
    for (const type of ['focusin', 'focusout', 'visibilitychange']) document.addEventListener(type, refresh);
    const timer = setInterval(refresh, 100);
    window.addEventListener('pagehide', () => { clearInterval(timer); observer.disconnect(); }, { once: true });
})();
