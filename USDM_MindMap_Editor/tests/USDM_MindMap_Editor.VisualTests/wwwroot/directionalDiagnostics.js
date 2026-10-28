// Confirmation-host-only DOM reader. It never invokes editor APIs, synthesizes
// input, changes focus, modifies model state, or changes production elements.
export function attach(panel, fixture) {
    const scope = panel.parentElement;
    const events = new AbortController();
    let baseline;
    let baselineRevision;
    let baselineTimer;
    let refreshQueued = false;
    let stopped = false;

    const read = () => {
        const canvas = scope.querySelector('.mindmap-canvas');
        const positions = [...scope.querySelectorAll('.tree-nodes')].map(group => ({
            id: group.dataset.backgroundId,
            x: Number(group.dataset.positionX), y: Number(group.dataset.positionY),
            preview: group.style.transform || 'なし'
        }));
        return {
            positions, revision: canvas?.dataset.layoutRevision ?? '-', dirty: canvas?.dataset.unsaved ?? '-',
            zoom: scope.querySelector('.zoom-status')?.textContent?.trim() ?? '-',
            transform: scope.querySelector('.mindmap-world')?.style.transform ?? '-',
            status: scope.querySelector('.editor-status [role="status"]')?.textContent?.trim() ?? '-',
            dialog: Boolean(scope.querySelector('dialog[open]'))
        };
    };
    const number = value => Number.isFinite(value) ? Number(value.toFixed(2)).toString() : '不正値';
    const signed = value => value > 0 ? `+${number(value)}` : number(value);
    const captureBaseline = () => {
        if (baseline || stopped) return;
        const sample = read();
        if (sample.positions.length === 0) return;
        baseline = new Map(sample.positions.map(position => [position.id, position]));
        baselineRevision = sample.revision;
        refresh();
    };
    const refresh = () => {
        if (stopped) return;
        const sample = read();
        let movedRight = false;
        const rows = sample.positions.map(position => {
            const before = baseline?.get(position.id);
            const dx = before ? position.x - before.x : 0;
            const dy = before ? position.y - before.y : 0;
            if (dx > 0) movedRight = true;
            const delta = before ? `ΔX=${signed(dx)} ΔY=${signed(dy)}` : '基準待ち';
            return `${position.id}: X=${number(position.x)} Y=${number(position.y)} ${delta}`
                + (position.preview === 'なし' ? '' : `\n  未確定preview=${position.preview}`);
        });
        const warning = movedRight && /下(?:へ|方)/.test(sample.status)
            ? '\n注意: X方向移動あり。通知が「下方のみ」と読めないか確認。' : '';
        const text = `方向別診断（2026-10-28／読取専用）\n${fixture}\n`
            + `zoom=${sample.zoom} rev=${sample.revision} dirty=${sample.dirty} dialog=${sample.dialog}\n`
            + `${sample.transform}\n基準rev=${baselineRevision ?? '初期描画の安定待ち'}\n`
            + `${rows.join('\n')}\n通知: ${sample.status}${warning}\n`
            + `visibility=${document.visibilityState} active=${document.hasFocus()}\n`
            + '初期データのやり直しはページ再読み込み。実操作の合格判定は別途。';
        if (panel.textContent !== text) panel.textContent = text;
    };
    const schedule = () => {
        // Safari can report this confirmation tab as hidden while accessibility
        // operations still update its DOM. Reading must not depend on paint/rAF.
        if (!refreshQueued) {
            refreshQueued = true;
            queueMicrotask(() => { refreshQueued = false; refresh(); });
        }
        if (!baseline) {
            clearTimeout(baselineTimer);
            baselineTimer = setTimeout(captureBaseline, 400);
        }
    };
    // Capture the settled initial state, or the state immediately before the first
    // real user input when the operator acts before the settling timer completes.
    for (const type of ['pointerdown', 'keydown']) scope.addEventListener(type, captureBaseline,
        { capture: true, signal: events.signal });
    const observer = new MutationObserver(records => {
        if (records.some(record => !panel.contains(record.target))) schedule();
    });
    observer.observe(scope, { subtree: true, childList: true, characterData: true, attributes: true,
        attributeFilter: ['style', 'open', 'data-position-x', 'data-position-y', 'data-layout-revision', 'data-unsaved'] });
    document.addEventListener('visibilitychange', schedule, { signal: events.signal });
    window.addEventListener('focus', schedule, { signal: events.signal });
    window.addEventListener('blur', schedule, { signal: events.signal });
    schedule();
    return {
        dispose() {
            stopped = true;
            events.abort();
            observer.disconnect();
            clearTimeout(baselineTimer);
        }
    };
}
