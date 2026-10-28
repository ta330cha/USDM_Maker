// Confirmation host only: read DOM and write this diagnostic panel, never editor
// elements, focus, events, model data, or application/interop APIs.
export function attach(panel, fixture) {
    const scope = panel.parentElement;
    const events = new AbortController();
    const initialCanvas = scope.querySelector('.mindmap-canvas');
    let stopped = false;
    let queued = false;
    let initial;
    let initialTimer;
    let transitionTimer;
    let pendingInput;
    let lastSample;
    let transition;
    let transitionCount = 0;

    const rect = element => {
        if (!element) return null;
        const value = element.getBoundingClientRect();
        return { x: value.x, y: value.y, width: value.width, height: value.height, right: value.right, bottom: value.bottom };
    };
    const displayed = element => {
        if (!element || element.getClientRects().length === 0) return false;
        const style = getComputedStyle(element);
        return style.display !== 'none' && style.visibility !== 'hidden' && style.visibility !== 'collapse';
    };
    const describe = element => !element ? null : {
        tag: element.tagName, id: element.id || null, classes: element.className?.baseVal ?? element.className ?? '',
        label: ['BODY', 'HTML'].includes(element.tagName) ? ''
            : (element.getAttribute('aria-label') ?? element.textContent ?? '').replace(/\s+/g, ' ').trim().slice(0, 80),
        focusVisible: element.matches?.(':focus-visible') ?? false
    };
    const inViewport = value => Boolean(value && value.x >= -1 && value.y >= -1
        && value.right <= innerWidth + 1 && value.bottom <= innerHeight + 1);
    const intersects = (a, b) => Boolean(a && b && a.width > 0 && b.width > 0 && a.height > 0 && b.height > 0
        && a.x < b.right && a.right > b.x && a.y < b.bottom && a.bottom > b.y);
    const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
    const read = () => {
        const shell = scope.querySelector('.editor-shell');
        const canvas = scope.querySelector('.mindmap-canvas');
        const world = scope.querySelector('.mindmap-world');
        const toggle = scope.querySelector('.toolbar-toggle');
        const header = scope.querySelector('.editor-toolbar');
        const status = scope.querySelector('.editor-status');
        const controlsId = toggle?.getAttribute('aria-controls');
        const toggleStyle = toggle ? getComputedStyle(toggle) : null;
        const headerStyle = header ? getComputedStyle(header) : null;
        const headerControls = [...(header?.querySelectorAll('button,input,select,textarea,a[href],[tabindex]') ?? [])];
        const visibleControls = headerControls.filter(displayed);
        const rows = new Set(visibleControls.map(control => Math.round(control.getBoundingClientRect().top)));
        const transform = world ? getComputedStyle(world).transform : 'none';
        const matrix = new DOMMatrixReadOnly(transform === 'none' ? undefined : transform);
        const nodes = [...scope.querySelectorAll('.tree-nodes .node')].map(node => ({
            key: node.id, backgroundId: node.closest('.tree-nodes')?.dataset.backgroundId,
            x: node.style.left, y: node.style.top, width: node.style.width, minHeight: node.style.minHeight,
            measuredWidth: getComputedStyle(node).width, measuredHeight: getComputedStyle(node).height,
            text: node.textContent, reasonExpanded: node.querySelector('details')?.open ?? null
        }));
        const paths = [...scope.querySelectorAll('.tree-connectors path')].map(path => ({
            backgroundId: path.closest('.tree-connectors')?.dataset.backgroundId,
            parent: path.dataset.parent, child: path.dataset.child, d: path.getAttribute('d')
        }));
        const positions = [...scope.querySelectorAll('.tree-nodes')].map(group => ({
            id: group.dataset.backgroundId, x: group.dataset.positionX, y: group.dataset.positionY,
            preview: group.style.transform
        }));
        return {
            fixture, toolbarVisible: shell?.dataset.toolbarVisible ?? String(!header?.hidden),
            canvasSameAsInitial: canvas === initialCanvas,
            revision: canvas?.dataset.layoutRevision ?? null, dirty: canvas?.dataset.unsaved ?? null,
            positions, nodes, paths, transform, worldInlineTransform: world?.style.transform,
            zoomPan: { zoom: Math.hypot(matrix.a, matrix.b), panX: matrix.e, panY: matrix.f },
            selected: [...scope.querySelectorAll('[aria-selected="true"],.node.is-selected')].map(node => node.id),
            toggle: {
                exists: Boolean(toggle), expanded: toggle?.getAttribute('aria-expanded'), controls: controlsId,
                controlsMatch: Boolean(header && controlsId && document.getElementById(controlsId) === header),
                label: toggle?.getAttribute('aria-label'), title: toggle?.getAttribute('title'),
                disabled: toggle?.disabled ?? null, rect: rect(toggle), visible: displayed(toggle),
                withinViewport: inViewport(rect(toggle)), insideWorld: Boolean(toggle?.closest('.mindmap-world')),
                focused: document.activeElement === toggle, focusVisible: toggle?.matches(':focus-visible') ?? false,
                outline: toggleStyle ? `${toggleStyle.outlineStyle} ${toggleStyle.outlineWidth} ${toggleStyle.outlineColor}` : null,
                shadow: toggleStyle?.boxShadow,
                svgIcons: [...(toggle?.querySelectorAll('svg') ?? [])].map(icon => ({
                    classes: icon.getAttribute('class'), transform: getComputedStyle(icon).transform, rect: rect(icon)
                })),
                svgPaths: [...(toggle?.querySelectorAll('svg path,svg polyline') ?? [])]
                    .map(icon => icon.getAttribute('d') ?? icon.getAttribute('points'))
            },
            header: {
                id: header?.id, hidden: header?.hidden ?? null, ariaHidden: header?.getAttribute('aria-hidden'),
                display: headerStyle?.display, rect: rect(header), renderedControlCount: visibleControls.length,
                controlLabels: headerControls.map(control => control.textContent.trim()),
                titleTextPresent: header?.textContent.includes('USDM MindMap Editor') ?? false,
                pageTitle: document.title, firstControlRect: rect(visibleControls[0]),
                rowLefts: [...rows].map(row => Math.min(...visibleControls
                    .filter(control => Math.round(control.getBoundingClientRect().top) === row)
                    .map(control => control.getBoundingClientRect().left))),
                potentialTabStops: visibleControls.filter(control => !control.disabled && control.tabIndex >= 0 && !control.closest('[inert]')).length,
                controlRows: rows.size, horizontalOverflow: Boolean(header && header.scrollWidth > header.clientWidth + 1),
                overlapsToggle: [...(header?.querySelectorAll('strong,button,input,select,a') ?? [])]
                    .some(control => control !== toggle && displayed(control) && intersects(rect(control), rect(toggle)))
            },
            canvas: rect(canvas), status: { rect: rect(status), visible: displayed(status), withinViewport: inViewport(rect(status)) },
            shell: rect(shell), viewport: { width: innerWidth, height: innerHeight }, activeElement: describe(document.activeElement),
            hoverCount: scope.querySelectorAll('.child-add-region.is-hovered').length,
            tooltipCount: scope.querySelectorAll('.child-add-region[title]').length,
            dialog: Boolean(scope.querySelector('dialog[open]')), documentVisibility: document.visibilityState
        };
    };
    const compare = (before, after) => ({
        canvasSame: before.canvasSameAsInitial && after.canvasSameAsInitial,
        revision: before.revision === after.revision, dirty: before.dirty === after.dirty,
        positions: same(before.positions, after.positions), nodes: same(before.nodes, after.nodes), paths: same(before.paths, after.paths),
        zoomPan: same(before.zoomPan, after.zoomPan), selection: same(before.selected, after.selected),
        toggleRect: same(before.toggle.rect, after.toggle.rect),
        canvasHeightDelta: after.canvas && before.canvas ? after.canvas.height - before.canvas.height : null,
        canvasOriginDelta: after.canvas && before.canvas ? { x: after.canvas.x - before.canvas.x, y: after.canvas.y - before.canvas.y } : null
    });
    const n = value => typeof value === 'number' ? Number(value.toFixed(2)).toString() : '-';
    const rectText = value => value ? `(${n(value.x)},${n(value.y)}) ${n(value.width)}×${n(value.height)}` : 'なし';
    const refresh = () => {
        if (stopped) return;
        const sample = read();
        if (lastSample && sample.toolbarVisible !== lastSample.toolbarVisible) {
            transition = { number: ++transitionCount, before: pendingInput ?? lastSample, after: sample, settled: false };
            pendingInput = undefined;
        }
        lastSample = sample;
        if (transition && !transition.settled) transition.after = sample;
        const comparison = transition ? compare(transition.before, transition.after) : null;
        const unchanged = comparison ? Object.entries(comparison).filter(([key]) => !key.startsWith('canvasHeight') && !key.startsWith('canvasOrigin'))
            .map(([key, value]) => `${key}:${value ? '同じ' : '変更'}`).join(' ') : '切替待ち';
        const header = sample.header;
        const text = `上部メニュー診断（読取専用） ${fixture}\n`
            + `表示=${sample.toolbarVisible} aria-expanded=${sample.toggle.expanded} controls一致=${sample.toggle.controlsMatch}\n`
            + `label=${sample.toggle.label} title=${sample.toggle.title} disabled=${sample.toggle.disabled}\n`
            + `toggle=${rectText(sample.toggle.rect)} 固定変換外=${!sample.toggle.insideWorld} focus=${sample.toggle.focused} fv=${sample.toggle.focusVisible}\n`
            + `icon=${sample.toggle.svgIcons.map(icon => `${icon.classes ?? ''} ${icon.transform} ${rectText(icon.rect)}`).join(' / ')}\n`
            + `header hidden=${header.hidden} display=${header.display} Tab候補=${header.potentialTabStops} rows=${header.controlRows}\n`
            + `メニューDOM順=${header.controlLabels.join(' → ')}\n`
            + `メニューtitle=${header.titleTextPresent} PageTitle=${header.pageTitle}\n`
            + `新規=${rectText(header.firstControlRect)} 各行左端=${header.rowLefts.map(n).join(',')}\n`
            + `header=${rectText(header.rect)} 横overflow=${header.horizontalOverflow} toggle重複=${header.overlapsToggle}\n`
            + `canvas=${rectText(sample.canvas)} status=${rectText(sample.status.rect)} 画面内=${sample.status.withinViewport}\n`
            + `zoom=${n(sample.zoomPan.zoom)} pan=(${n(sample.zoomPan.panX)},${n(sample.zoomPan.panY)}) rev=${sample.revision} dirty=${sample.dirty}\n`
            + `canvas同一DOM=${sample.canvasSameAsInitial} nodes=${sample.nodes.length} paths=${sample.paths.length} hover=${sample.hoverCount} tooltip=${sample.tooltipCount}\n`
            + `focus=${sample.activeElement?.tag ?? '-'} ${sample.activeElement?.label ?? '-'} dialog=${sample.dialog}\n`
            + `初期基準rev=${initial?.revision ?? '安定待ち'} 切替回数=${transitionCount} ${transition?.settled ? '切替後採取済み' : ''}\n`
            + `${unchanged}\n`
            + (comparison ? `切替Δcanvas高=${n(comparison.canvasHeightDelta)} 原点=${JSON.stringify(comparison.canvasOriginDelta)}\n` : '')
            + sample.positions.map(position => `${position.id}=(${position.x},${position.y})`).join(' ') + '\n'
            + `visibility=${sample.documentVisibility} 詳細=data-diagnostic-snapshot（診断panelのみ）`;
        // Native Safari accessibility snapshots can truncate one large text node.
        // Keep each row/chunk independently readable without changing the editor.
        const rows = text.split('\n').flatMap(line => {
            const characters = Array.from(line);
            const chunks = [];
            for (let start = 0; start < Math.max(1, characters.length); start += 350) {
                const row = document.createElement('div');
                row.textContent = characters.slice(start, start + 350).join('');
                chunks.push(row);
            }
            return chunks;
        });
        panel.replaceChildren(...rows);
        panel.dataset.diagnosticSnapshot = JSON.stringify({ current: sample, initial, transition, comparison });
    };
    const schedule = () => {
        if (stopped) return;
        if (!queued) {
            queued = true;
            // Safari accessibility inspection can keep the page "hidden". Never
            // depend on requestAnimationFrame to update this read-only evidence.
            queueMicrotask(() => { queued = false; refresh(); });
        }
        if (!initial) {
            clearTimeout(initialTimer);
            initialTimer = setTimeout(() => { if (!stopped) { initial = read(); refresh(); } }, 400);
        }
        clearTimeout(transitionTimer);
        transitionTimer = setTimeout(() => {
            if (stopped || !transition || transition.settled) return;
            transition.after = read();
            transition.settled = true;
            refresh();
        }, 180);
    };
    const beforeInput = event => {
        if (!event.target?.closest?.('.toolbar-toggle')) return;
        if (event.type === 'keydown' && !['Enter', ' '].includes(event.key)) return;
        pendingInput = read();
        initial ??= pendingInput;
        schedule();
    };
    for (const type of ['pointerdown', 'keydown', 'click']) scope.addEventListener(type, beforeInput, { capture: true, signal: events.signal });
    for (const type of ['focusin', 'focusout']) scope.addEventListener(type, schedule, { signal: events.signal });
    const mutations = new MutationObserver(records => {
        if (records.some(record => !panel.contains(record.target))) schedule();
    });
    mutations.observe(scope, { subtree: true, childList: true, characterData: true, attributes: true });
    const sizes = new ResizeObserver(schedule);
    for (const element of scope.querySelectorAll('.editor-shell,.editor-toolbar,.toolbar-toggle,.mindmap-canvas,.editor-status')) sizes.observe(element);
    document.addEventListener('visibilitychange', schedule, { signal: events.signal });
    window.addEventListener('resize', schedule, { signal: events.signal });
    window.addEventListener('focus', schedule, { signal: events.signal });
    window.addEventListener('blur', schedule, { signal: events.signal });
    schedule();
    return {
        // Serializes a fresh DOM read; useful to read-only confirmation tooling.
        read: () => ({ current: read(), initial, transition, comparison: transition ? compare(transition.before, transition.after) : null }),
        dispose() {
            stopped = true;
            events.abort();
            mutations.disconnect();
            sizes.disconnect();
            clearTimeout(initialTimer);
            clearTimeout(transitionTimer);
        }
    };
}
