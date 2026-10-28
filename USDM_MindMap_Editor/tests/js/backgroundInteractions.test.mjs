import assert from 'node:assert/strict';
import test from 'node:test';
import { attachCanvas, detachCanvas, resetCanvas, setAdditionDialogOpen, updateTreeLayout, restoreReasonState,
    tryBeginToolbarToggle, completeToolbarToggle }
    from '../../src/USDM_MindMap_Editor.Web/wwwroot/js/mindmapCanvas.js';

function harness({ zoom = 1, panX = 0, panY = 0 } = {}) {
    const calls = [], captures = new Set(), frames = new Map();
    let nextFrame = 1, resize, mutation, treeQueries = 0, nodeReads = 0, viewportReads = 0, nodeQueries = 0;
    let viewport = { left: 10, top: 20 };
    let commitResponse = () => Promise.resolve(true), additionResponse = () => Promise.resolve(true);
    let measurementResponse = () => Promise.resolve(1);
    const doc = new EventTarget(), win = new EventTarget();
    Object.assign(doc, { hidden: false, visibilityState: 'visible', activeElement: null });
    const classes = initial => {
        const set = new Set(initial);
        return {
            add: (...names) => names.forEach(name => set.add(name)),
            remove: (...names) => names.forEach(name => set.delete(name)),
            contains: name => set.has(name),
            toggle(name, value) { value ??= !set.has(name); if (value) set.add(name); else set.delete(name); return value; }
        };
    };
    const element = (names = [], parent = null) => ({
        parentElement: parent, classList: classes(names), dataset: {}, isConnected: true, title: '',
        style: { transform: '', setProperty(name, value) { this[name] = value; } },
        matches(selector) { return selector.split(',').some(part => part.trim().startsWith('.') && this.classList.contains(part.trim().slice(1))); },
        closest(selector) {
            if (selector === '.connectors path') return this.isPath ? this : null;
            if (selector.includes('button, input')) return this.isControl ? this : this.parentElement?.closest(selector) ?? null;
            return this.matches(selector) ? this : this.parentElement?.closest(selector) ?? null;
        },
        querySelector(selector) { return selector === '.child-add-region' ? this.region ?? null : selector === 'details' ? this.details ?? null : null; },
        querySelectorAll() { return []; },
        getAttribute(name) { return name === 'aria-label' ? this.dataset.tooltip : this[name] ?? null; },
        removeAttribute(name) { if (name === 'title') this.title = ''; },
        focus(options) {
            const previous = doc.activeElement;
            this.focusOptions = options;
            doc.activeElement = this;
            for (const [type, target, relatedTarget] of [['focusout', previous, this], ['focusin', this, previous]]) {
                if (!root.contains(target) || previous === this) continue;
                const event = new Event(type); Object.defineProperty(event, 'target', { value: target });
                Object.defineProperty(event, 'relatedTarget', { value: relatedTarget }); root.dispatchEvent(event);
            }
        }
    });
    const root = new EventTarget();
    Object.assign(root, element(['mindmap-canvas']), {
        getBoundingClientRect: () => { viewportReads++; return { ...viewport }; },
        setPointerCapture: id => captures.add(id), hasPointerCapture: id => captures.has(id), releasePointerCapture: id => captures.delete(id),
        contains: target => target === root || Boolean(target?.closest?.('.mindmap-canvas'))
    });
    const shell = element(['editor-shell']);
    shell.contains = target => target === shell || target?.closest?.('.editor-shell') === shell;
    root.parentElement = shell;
    const toolbar = element([], shell); toolbar.isControl = true;
    const toggle = element(['toolbar-toggle'], shell); toggle.isControl = true;
    toggle['aria-controls'] = 'editor-toolbar'; toggle['aria-expanded'] = 'true';
    const header = { id: 'editor-toolbar', hidden: false };
    const world = element(['mindmap-world'], root), svg = element(['connectors'], world);
    const layout = [
        { backgroundId: '001', position: { x: 100, y: 100 }, bounds: { x: 100, y: 100, width: 500, height: 600 } },
        { backgroundId: '002', position: { x: 900, y: 100 }, bounds: { x: 900, y: 100, width: 300, height: 300 } }
    ];
    const groups = layout.map(tree => {
        const nodes = element(['tree-nodes'], world), connectors = element(['tree-connectors'], svg);
        nodes.dataset.backgroundId = connectors.dataset.backgroundId = tree.backgroundId;
        const node = element(['node', 'background-node'], nodes);
        node.id = `node-background:${tree.backgroundId}`;
        node.getBoundingClientRect = () => { nodeReads++; return {
            left: viewport.left + panX + tree.position.x * zoom, top: viewport.top + panY + tree.position.y * zoom,
            right: viewport.left + panX + (tree.position.x + 280) * zoom, bottom: viewport.top + panY + (tree.position.y + 110) * zoom,
            width: 280 * zoom, height: 110 * zoom
        }; };
        const region = element(['child-add-region'], node);
        region.dataset.parentKey = `background:${tree.backgroundId}`;
        region.dataset.tooltip = '上位要求を追加'; region.isControl = true;
        node.region = region;
        const path = element([], connectors); path.isPath = true;
        return { nodes, connectors, node, region, path };
    });
    world.querySelectorAll = () => { nodeQueries++; return groups.map(group => group.node); };
    root.querySelector = selector => selector === '.mindmap-world' ? world : null;
    root.querySelectorAll = selector => { treeQueries++; return groups.map(group => selector === '.tree-nodes' ? group.nodes : group.connectors); };
    doc.elementFromPoint = (x, y) => groups.map(group => group.node).find(node => {
        const r = node.getBoundingClientRect(); return x >= r.left && x <= r.right && y >= r.top && y <= r.bottom;
    }) ?? null;
    doc.getElementById = id => id === header.id ? header : groups.map(group => group.node).find(node => node.id === id) ?? null;
    globalThis.document = doc; globalThis.window = win;
    globalThis.requestAnimationFrame = callback => { const id = nextFrame++; frames.set(id, callback); return id; };
    globalThis.cancelAnimationFrame = id => frames.delete(id);
    globalThis.ResizeObserver = class { constructor(callback) { resize = callback; } observe() {} unobserve() {} disconnect() {} };
    globalThis.MutationObserver = class { constructor(callback) { mutation = callback; } observe() {} disconnect() {} };
    globalThis.HTMLDetailsElement = class {};
    attachCanvas(root, { invokeMethodAsync(method, ...args) {
        calls.push({ method, args });
        return method === 'CommitBackgroundMove' ? commitResponse(...args)
            : ['RequestBackgroundAddition', 'RequestChildAddition'].includes(method) ? additionResponse(...args)
            : method === 'NodeSizesChanged' ? measurementResponse(...args) : Promise.resolve(true);
    } }, zoom, panX, panY);
    updateTreeLayout(root, 1, layout);
    const body = groups[0].node.getBoundingClientRect();
    const start = { x: body.left + 10, y: body.top + 10 };
    const h = {
        root, world, doc, win, toolbar, toggle, header, groups, layout, calls, captures, start,
        treeQueries: () => treeQueries,
        metrics: () => ({ nodeReads, viewportReads, nodeQueries, treeQueries }),
        setViewport: rect => viewport = { ...rect },
        renderToolbar(visible) { header.hidden = !visible; toggle['aria-expanded'] = String(visible); },
        setCommitResponse: fn => commitResponse = fn, setAdditionResponse: fn => additionResponse = fn,
        setMeasurementResponse: fn => measurementResponse = fn,
        event(type, { target = groups[0].node, x = start.x, y = start.y, ...extra } = {}) {
            const event = new Event(type, { cancelable: true });
            Object.assign(event, { clientX: x, clientY: y, pointerId: 1, button: 0, isPrimary: true, ...extra });
            Object.defineProperty(event, 'target', { value: target });
            if (['pointerdown', 'keydown', 'click'].includes(type)) doc.dispatchEvent(event);
            if (!event.defaultPrevented && root.contains(target)) root.dispatchEvent(event);
            return event;
        },
        flush() { const pending = [...frames.values()]; frames.clear(); pending.forEach(callback => callback()); },
        move(dx, dy = 0) { this.event('pointermove', { x: start.x + dx, y: start.y + dy }); this.flush(); },
        resize() { resize([{ target: groups[0].node, borderBoxSize: [{ inlineSize: 280, blockSize: 120 }] }]); },
        mutate(target = groups[0].node) { mutation([{ type: 'attributes', attributeName: 'style', target, addedNodes: [], removedNodes: [] }]); },
        doubleClick(options = {}) {
            for (const detail of [1, 2]) {
                this.event('pointerdown', options); this.event('pointerup', options); this.event('click', { ...options, detail });
            }
            return this.event('dblclick', { ...options, detail: 2 });
        },
        commits: () => calls.filter(call => call.method === 'CommitBackgroundMove'),
        additions: () => calls.filter(call => call.method === 'RequestBackgroundAddition')
    };
    return h;
}

test('blank double click captures logical position at each zoom and pans without adding on a single click', async () => {
    for (const zoom of [.25, 1, 2]) {
        const h = harness({ zoom, panX: 31, panY: -17 });
        try {
            const options = { target: h.world, x: 51, y: 1020 };
            h.event('click', { ...options, detail: 1 });
            assert.equal(h.additions().length, 0);
            h.doubleClick(options);
            assert.deepEqual(h.additions().map(call => call.args), [[(51 - 10 - 31) / zoom, (1020 - 20 + 17) / zoom]]);
            assert.equal(h.doc.activeElement, h.root);
            h.doubleClick(options);
            assert.equal(h.additions().length, 1, 'in-flight requests must share the dialog lock');
            await Promise.resolve();
            setAdditionDialogOpen(h.root, false);
        } finally { detachCanvas(h.root); }
    }
});

test('nodes, actual SVG strokes, controls and a pan followed by a second click cannot add a background', () => {
    const h = harness();
    try {
        const control = { closest: selector => selector.includes('button, input') ? control : null };
        for (const target of [h.groups[0].node, h.groups[0].path, control]) h.doubleClick({ target });
        assert.equal(h.additions().length, 0);
        const options = { target: h.world, x: 40, y: 1020 };
        h.event('pointerdown', options); h.event('pointermove', { ...options, x: 70 });
        h.event('pointerup', { ...options, x: 70 }); h.event('click', { ...options, x: 70, detail: 1 });
        h.event('pointerdown', options); h.event('pointerup', options); h.event('click', { ...options, detail: 2 });
        h.event('dblclick', { ...options, detail: 2 });
        assert.equal(h.additions().length, 0);
    } finally { detachCanvas(h.root); }
});

test('tree preview moves only two groups in canvas units and commits once, then awaits the authoritative revision', async () => {
    for (const zoom of [.25, 1, 2]) {
        const h = harness({ zoom });
        try {
            const queryCount = h.treeQueries();
            h.event('pointerdown');
            h.event('pointermove', { x: h.start.x + 20 * zoom });
            h.event('pointermove', { x: h.start.x + 40 * zoom });
            assert.equal(h.groups[0].nodes.style.transform, '', 'preview updates are frame-batched');
            h.flush();
            assert.equal(h.groups[0].nodes.style.transform, 'translate(40px, 0px)');
            assert.equal(h.groups[0].connectors.style.transform, 'translate(40px, 0px)');
            assert.equal(h.groups[1].nodes.style.transform, '');
            assert.equal(h.commits().length, 0);
            assert.equal(h.treeQueries(), queryCount, 'pointer moves must not query all DOM nodes/groups');
            h.event('pointerup', { x: h.start.x + 40 * zoom });
            assert.deepEqual(h.commits().map(call => call.args), [['001', 140, 100, 1]]);
            await Promise.resolve();
            assert.equal(resetCanvas(h.root), false, 'authoritative layout is still pending');
            h.mutate();
            assert.equal(h.groups[0].nodes.style.transform, '', 'committed DOM coordinates remove the transient translation before paint');
            updateTreeLayout(h.root, 2, h.layout);
            assert.equal(resetCanvas(h.root), true);
        } finally { detachCanvas(h.root); }
    }
});

test('collision includes entire cached tree bounds, rejects the final candidate and restores the start rather than the last valid point', () => {
    const h = harness();
    try {
        h.event('pointerdown'); h.move(100);
        assert.equal(h.groups[0].nodes.style.transform, 'translate(100px, 0px)');
        h.move(400);
        assert.equal(h.root.classList.contains('is-invalid-drop'), true);
        h.event('pointerup', { x: h.start.x + 400 });
        assert.equal(h.groups[0].nodes.style.transform, '');
        assert.equal(h.groups[0].connectors.style.transform, '');
        assert.equal(h.groups[1].nodes.style.transform, '');
        assert.equal(h.commits().length, 0);
        assert.equal(h.calls.filter(call => call.method === 'NotifyBackgroundMoveRejected').length, 1);
    } finally { detachCanvas(h.root); }
});

test('touching the other tree boundary is valid and unrendered trees still participate in collision', () => {
    const h = harness();
    try {
        h.event('pointerdown'); h.move(300);
        assert.equal(h.root.classList.contains('is-invalid-drop'), false);
        h.event('pointerup', { x: h.start.x + 300 });
        assert.equal(h.commits().length, 1);
        updateTreeLayout(h.root, 2, [...h.layout, { backgroundId: '003', position: { x: -500, y: 100 }, bounds: { x: -500, y: 100, width: 500, height: 600 } }]);
        h.event('pointerdown'); h.move(-200);
        assert.equal(h.root.classList.contains('is-invalid-drop'), true);
        h.event('pointercancel');
    } finally { detachCanvas(h.root); }
});

test('Escape, pointer cancellation, lost capture, blur, hidden state and geometry changes all restore a tree without committing', () => {
    for (const cancel of [
        h => h.event('keydown', { key: 'Escape' }), h => h.event('pointercancel'), h => h.event('lostpointercapture'),
        h => h.win.dispatchEvent(new Event('blur')),
        h => { h.doc.hidden = true; h.doc.dispatchEvent(new Event('visibilitychange')); },
        h => h.resize(), h => h.mutate(), h => updateTreeLayout(h.root, 2, h.layout)
    ]) {
        const h = harness();
        try {
            h.event('pointerdown'); h.move(80); cancel(h);
            assert.equal(h.groups[0].nodes.style.transform, '');
            assert.equal(h.groups[0].connectors.style.transform, '');
            h.event('pointerup', { x: h.start.x + 80 });
            h.event('click', { detail: 1 });
            assert.equal(h.commits().length, 0);
            assert.equal(h.additions().length, 0);
            assert.equal(h.captures.size, 0);
        } finally { detachCanvas(h.root); }
    }
});

test('the child-add ellipse wins over background movement and background drag does not zoom or create a child', () => {
    const h = harness();
    try {
        const rect = h.groups[0].node.getBoundingClientRect();
        const ellipse = { target: h.groups[0].region, x: rect.left + rect.width / 2, y: rect.bottom };
        h.event('pointerdown', ellipse); h.event('pointermove', { ...ellipse, x: ellipse.x + 20 });
        h.event('pointerup', ellipse); h.event('click', { ...ellipse, detail: 1 });
        assert.equal(h.commits().length, 0);
        assert.equal(h.calls.filter(call => call.method === 'RequestChildAddition').length, 0);
        h.event('pointerdown'); h.move(20);
        const worldTransform = h.world.style.transform;
        h.event('wheel', { deltaY: -1 });
        assert.equal(h.world.style.transform, worldTransform);
        assert.equal(resetCanvas(h.root), false);
        h.event('pointercancel');
    } finally { detachCanvas(h.root); }
});

test('small moves and a drag returning exactly to its start do not commit; preview mutations do not cancel the drag', () => {
    const h = harness();
    try {
        h.event('pointerdown'); h.move(4); h.event('pointerup', { x: h.start.x + 4 });
        assert.equal(h.commits().length, 0);
        h.event('pointerdown'); h.move(20); h.mutate(h.groups[0].nodes);
        assert.equal(h.groups[0].nodes.style.transform, 'translate(20px, 0px)');
        h.move(0); h.event('pointerup');
        assert.equal(h.commits().length, 0);
        assert.equal(h.groups[0].nodes.style.transform, '');
    } finally { detachCanvas(h.root); }
});

test('failed commits restore the start and release the lock; detach also removes active previews and callbacks', async () => {
    for (const response of [() => Promise.resolve(false), () => Promise.reject(new Error('disconnected'))]) {
        const h = harness();
        try {
            h.setCommitResponse(response);
            h.event('pointerdown'); h.move(20); h.event('pointerup', { x: h.start.x + 20 });
            await Promise.resolve();
            assert.equal(h.groups[0].nodes.style.transform, '');
            assert.equal(resetCanvas(h.root), true);
            h.event('pointerdown'); h.move(20); detachCanvas(h.root);
            assert.equal(h.groups[0].nodes.style.transform, '');
            assert.equal(h.captures.size, 0);
        } finally { detachCanvas(h.root); }
    }
});

test('reason state rollback changes only the requested details and is a no-op for unknown keys', () => {
    const h = harness();
    try {
        h.groups[0].node.details = { open: true };
        restoreReasonState(h.root, 'background:001', false);
        assert.equal(h.groups[0].node.details.open, false);
        restoreReasonState(h.root, 'requirement:unknown', true);
        assert.equal(h.groups[0].node.details.open, false);
    } finally { detachCanvas(h.root); }
});

test('toolbar mouse and keyboard activation are blocked during both dragging and Core commit acknowledgement', async () => {
    const h = harness();
    try {
        const toolbarEvent = type => {
            const event = new Event(type, { cancelable: true });
            Object.assign(event, { key: 'Enter' });
            Object.defineProperty(event, 'target', { value: h.toolbar });
            h.doc.dispatchEvent(event); return event;
        };
        assert.equal(toolbarEvent('click').defaultPrevented, false);
        h.event('pointerdown'); h.move(50);
        for (const type of ['click', 'keydown']) assert.equal(toolbarEvent(type).defaultPrevented, true);
        h.event('pointerup', { x: h.start.x + 50 }); await Promise.resolve();
        for (const type of ['click', 'keydown']) assert.equal(toolbarEvent(type).defaultPrevented, true);
        updateTreeLayout(h.root, 2, h.layout);
        assert.equal(toolbarEvent('click').defaultPrevented, false);
    } finally { detachCanvas(h.root); }
});

test('geometry corrections block new tree and blank-background actions until acknowledgement and matching revision both arrive', async () => {
    for (const snapshotFirst of [true, false]) {
        const h = harness();
        try {
            let acknowledge;
            h.setMeasurementResponse(() => new Promise(resolve => acknowledge = resolve));
            h.resize(); h.flush();
            const checkBlocked = () => {
                h.event('pointerdown'); h.move(40); h.event('pointerup', { x: h.start.x + 40 });
                h.doubleClick({ target: h.world, x: 40, y: 1020 });
                assert.equal(h.commits().length, 0);
                assert.equal(h.additions().length, 0);
            };
            checkBlocked();
            if (snapshotFirst) { updateTreeLayout(h.root, 2, h.layout); checkBlocked(); }
            acknowledge(2);
            await Promise.resolve(); await Promise.resolve(); await Promise.resolve();
            if (!snapshotFirst) { checkBlocked(); updateTreeLayout(h.root, 2, h.layout); }
            h.event('pointerdown'); h.move(40); h.event('pointerup', { x: h.start.x + 40 });
            assert.deepEqual(h.commits()[0].args, ['001', 140, 100, 2]);
        } finally { detachCanvas(h.root); }
    }
});

test('unchanged valid size acknowledgements do not deadlock and DOM revisions cannot start a drag using stale bounds', async () => {
    const h = harness();
    try {
        h.resize(); h.flush();
        await Promise.resolve(); await Promise.resolve(); await Promise.resolve();
        h.event('pointerdown'); h.move(30); h.event('pointercancel');
        assert.equal(h.groups[0].nodes.style.transform, '');
        assert.equal(h.captures.size, 0);
        h.root.dataset.layoutRevision = '2';
        h.event('pointerdown'); h.move(30); h.event('pointerup', { x: h.start.x + 30 });
        assert.equal(h.commits().length, 0);
        updateTreeLayout(h.root, 2, h.layout);
        h.event('pointerdown'); h.move(30);
        assert.equal(h.groups[0].nodes.style.transform, 'translate(30px, 0px)');
        updateTreeLayout(h.root, 1, h.layout);
        assert.equal(h.groups[0].nodes.style.transform, 'translate(30px, 0px)', 'late stale snapshots must not cancel current gestures');
        h.event('pointerup', { x: h.start.x + 30 });
        assert.equal(h.commits().length, 1);
    } finally { detachCanvas(h.root); }
});

test('toolbar toggle waits for rendered state, preserves geometry and transform, and never measures nodes', () => {
    const h = harness({ zoom: .25, panX: 31, panY: -17 });
    try {
        const layout = JSON.stringify(h.layout), transform = h.world.style.transform, metrics = h.metrics();
        assert.equal(tryBeginToolbarToggle(h.root), true);
        assert.equal(tryBeginToolbarToggle(h.root), false, 'duplicate request is locked');
        assert.equal(completeToolbarToggle(h.root, h.toggle, false), false, 'pre-await render must not release the lock');
        h.header.hidden = true;
        assert.equal(completeToolbarToggle(h.root, h.toggle, false), false, 'ARIA state must also be current');
        h.renderToolbar(false); h.setViewport({ left: 10, top: 0 });
        assert.equal(completeToolbarToggle(h.root, h.toggle, false), true);
        h.flush();
        assert.equal(h.world.style.transform, transform);
        assert.equal(JSON.stringify(h.layout), layout);
        assert.deepEqual(h.calls, [], 'no CanvasChanged, node measurements, layout or document calls');
        assert.deepEqual(h.metrics(), { ...metrics, viewportReads: metrics.viewportReads + 1 });
        assert.equal(h.doc.activeElement, h.toggle);
        assert.deepEqual(h.toggle.focusOptions, { preventScroll: true });
        assert.equal(tryBeginToolbarToggle(h.root), true);
        h.renderToolbar(true);
        assert.equal(completeToolbarToggle(h.root, h.toggle, true), true);
    } finally { detachCanvas(h.root); }
});

test('toolbar rejects pending additions, open dialog, every gesture, and both stages of a tree commit without disturbing them', async () => {
    const starts = [
        h => { h.event('pointerdown', { target: h.world, x: 40, y: 1020 }); },
        h => { h.event('pointerdown', { target: h.world, x: 40, y: 1020 }); h.event('pointermove', { target: h.world, x: 60, y: 1020 }); },
        h => { const r = h.groups[0].node.getBoundingClientRect(); h.event('pointerdown', { target: h.groups[0].region, x: r.left + r.width / 2, y: r.bottom }); },
        h => { h.setAdditionResponse(() => new Promise(() => {})); h.event('click', { target: h.groups[0].region, detail: 0 }); },
        h => { h.setAdditionResponse(() => new Promise(() => {})); h.doubleClick({ target: h.world, x: 40, y: 1020 }); },
        h => setAdditionDialogOpen(h.root, true),
        h => h.event('pointerdown'),
        h => { h.event('pointerdown'); h.move(30); },
        h => { h.setCommitResponse(() => new Promise(() => {})); h.event('pointerdown'); h.move(30); h.event('pointerup', { x: h.start.x + 30 }); },
        async h => { h.event('pointerdown'); h.move(30); h.event('pointerup', { x: h.start.x + 30 }); await Promise.resolve(); }
    ];
    for (const start of starts) {
        const h = harness();
        try {
            await start(h);
            const transform = h.world.style.transform, preview = h.groups[0].nodes.style.transform;
            const captures = [...h.captures], calls = h.calls.length;
            assert.equal(tryBeginToolbarToggle(h.root), false);
            for (const key of ['Enter', ' '])
                assert.equal(h.event('keydown', { target: h.toggle, key }).defaultPrevented, true);
            assert.equal(h.event('pointerdown', { target: h.toggle }).defaultPrevented, true);
            assert.equal(h.event('click', { target: h.toggle, detail: 1 }).defaultPrevented, true);
            assert.equal(h.world.style.transform, transform);
            assert.equal(h.groups[0].nodes.style.transform, preview);
            assert.deepEqual([...h.captures], captures);
            assert.equal(h.calls.length, calls);
        } finally { detachCanvas(h.root); }
    }
});

test('short toolbar transition blocks canvas and toolbar actions until completion', () => {
    const h = harness();
    try {
        const transform = h.world.style.transform;
        assert.equal(tryBeginToolbarToggle(h.root), true);
        for (const type of ['pointerdown', 'pointermove', 'pointerup', 'click', 'dblclick', 'keydown'])
            assert.equal(h.event(type, { key: 'Enter', detail: 1 }).defaultPrevented, true, type);
        assert.equal(h.event('wheel', { deltaY: -1 }).defaultPrevented, true);
        for (const target of [h.toolbar, h.toggle]) {
            for (const type of ['pointerdown', 'click', 'keydown'])
                assert.equal(h.event(type, { target, key: ' ', detail: 0 }).defaultPrevented, true);
        }
        h.event('click', { target: h.groups[0].region, detail: 0 });
        h.doubleClick({ target: h.world, x: 40, y: 1020 });
        assert.equal(resetCanvas(h.root), false);
        assert.equal(h.world.style.transform, transform);
        assert.equal(h.captures.size, 0);
        assert.equal(h.calls.length, 0);
        assert.equal(completeToolbarToggle(h.root, null), true, 'abort always releases the lock');
        assert.equal(h.event('click', { target: h.toggle, detail: 0 }).defaultPrevented, false);
    } finally { detachCanvas(h.root); }
});

test('blank background coordinates use the new viewport at 25, 100 and 200 percent with saved pan', () => {
    for (const zoom of [.25, 1, 2]) {
        const h = harness({ zoom, panX: 31, panY: -17 });
        try {
            assert.equal(tryBeginToolbarToggle(h.root), true);
            h.setViewport({ left: 25, top: 0 }); h.renderToolbar(false);
            completeToolbarToggle(h.root, h.toggle, false);
            h.doubleClick({ target: h.world, x: 56, y: 1020 });
            assert.deepEqual(h.additions().map(call => call.args), [[0, 1037 / zoom]]);
        } finally { detachCanvas(h.root); }
    }
});

test('background dragging after toolbar toggle uses current node rectangles without changing logical origins', () => {
    for (const zoom of [.25, 1, 2]) {
        const h = harness({ zoom, panX: 31, panY: -17 });
        try {
            tryBeginToolbarToggle(h.root); h.setViewport({ left: 25, top: 0 });
            completeToolbarToggle(h.root, null);
            const r = h.groups[0].node.getBoundingClientRect(), x = r.left + 5, y = r.top + 5;
            h.event('pointerdown', { x, y });
            h.event('pointermove', { x: x + 40 * zoom, y }); h.flush();
            assert.equal(h.groups[0].nodes.style.transform, 'translate(40px, 0px)');
            h.event('pointerup', { x: x + 40 * zoom, y });
            assert.deepEqual(h.commits().map(call => call.args), [['001', 140, 100, 1]]);
        } finally { detachCanvas(h.root); }
    }
});

test('toolbar completion clears stale child hover but preserves keyboard focus behavior and fresh ellipse priority', () => {
    for (const zoom of [.25, 1, 2]) {
        const h = harness({ zoom, panX: 31, panY: -17 });
        try {
            const { node, region } = h.groups[0], old = node.getBoundingClientRect();
            h.event('pointermove', { target: region, x: old.left + old.width / 2, y: old.bottom });
            assert.equal(region.classList.contains('is-hovered'), true);
            region.focus(); h.event('keydown', { target: region, key: 'Tab' });
            assert.equal(region.classList.contains('is-keyboard-focused'), true);
            tryBeginToolbarToggle(h.root);
            assert.equal(region.classList.contains('is-hovered'), false);
            assert.equal(region.title, '');
            assert.equal(region.classList.contains('is-keyboard-focused'), true, 'begin must not disable keyboard presentation');
            h.setViewport({ left: 10, top: -40 }); completeToolbarToggle(h.root, h.toggle);
            assert.equal(h.doc.activeElement, h.toggle);
            assert.equal(region.classList.contains('is-keyboard-focused'), false, 'focus follows the toggle button');
            region.focus();
            assert.equal(region.classList.contains('is-keyboard-focused'), true, 'Tab back retains keyboard modality');
            assert.equal(region.classList.contains('is-hovered'), false);
            const summary = { closest: selector => selector.includes('button, input') ? summary : node.closest(selector) };
            assert.equal(h.event('click', { target: summary, x: old.left + old.width / 2, y: old.bottom, detail: 1 }).defaultPrevented, false);
            const now = node.getBoundingClientRect(), options = { target: summary, x: now.left + now.width / 2, y: now.bottom };
            h.event('pointermove', options);
            assert.equal(region.classList.contains('is-hovered'), true);
            assert.equal(h.event('pointerdown', options).defaultPrevented, true);
            assert.equal(h.event('pointerup', options).defaultPrevented, true);
            assert.equal(h.calls.filter(call => call.method === 'RequestChildAddition').length, 1);
        } finally { detachCanvas(h.root); }
    }
});

test('toolbar transition resets blank double click history and stale pointer-click suppression', () => {
    const h = harness();
    try {
        const blank = { target: h.world, x: 40, y: 1020 };
        h.event('click', { ...blank, detail: 1 });
        tryBeginToolbarToggle(h.root); completeToolbarToggle(h.root, null);
        h.event('click', { ...blank, detail: 2 }); h.event('dblclick', { ...blank, detail: 2 });
        assert.equal(h.additions().length, 0);
        h.event('pointerdown', blank); h.event('pointermove', { ...blank, x: 60 });
        h.event('pointerup', { ...blank, x: 60 });
        tryBeginToolbarToggle(h.root); completeToolbarToggle(h.root, null);
        assert.equal(h.event('click', { ...blank, detail: 1 }).defaultPrevented, false);
        h.event('click', { ...blank, detail: 2 }); h.event('dblclick', { ...blank, detail: 2 });
        assert.equal(h.additions().length, 1);
    } finally { detachCanvas(h.root); }
});

test('a canceled blocked pointer toggle cannot consume a later keyboard activation, and detach safely ends a toggle', () => {
    const h = harness();
    try {
        const blank = { target: h.world, x: 40, y: 1020 };
        h.event('pointerdown', blank);
        assert.equal(h.event('pointerdown', { target: h.toggle }).defaultPrevented, true);
        h.event('pointercancel', blank);
        assert.equal(h.event('click', { target: h.toggle, detail: 0 }).defaultPrevented, false);
        assert.equal(h.event('keydown', { target: h.toggle, key: 'Enter' }).defaultPrevented, false);
        assert.equal(h.event('click', { target: h.toggle, detail: 0 }).defaultPrevented, false);
        assert.equal(tryBeginToolbarToggle(h.root), true);
        detachCanvas(h.root);
        assert.equal(completeToolbarToggle(h.root, null), true);
        assert.equal(tryBeginToolbarToggle(h.root), false);
        assert.equal(h.event('click', { target: h.toggle, detail: 1 }).defaultPrevented, false);
    } finally { detachCanvas(h.root); }
});
