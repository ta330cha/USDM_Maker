import assert from 'node:assert/strict';
import test from 'node:test';
import { attachCanvas, detachCanvas, resetCanvas, setAdditionDialogOpen } from '../../src/USDM_MindMap_Editor.Web/wwwroot/js/mindmapCanvas.js';

// Small DOM/observer doubles exercise the public JS interop boundary without browser dependencies.
function harness() {
    let resize;
    let mutation;
    let nextFrame = 1;
    const frames = new Map();
    const calls = [];
    const capturedPointers = new Set();
    let fullNodeQueries = 0;
    let nodeRect = { left: 100, right: 380, top: 140, bottom: 250, width: 280, height: 110 };
    let additionResponse = () => Promise.resolve(true);
    const classes = () => {
        const values = new Set();
        return {
            add: (...names) => names.forEach(name => values.add(name)),
            remove: (...names) => names.forEach(name => values.delete(name)),
            contains: name => values.has(name),
            toggle(name, force) {
                const include = force ?? !values.has(name);
                if (include) values.add(name); else values.delete(name);
                return include;
            }
        };
    };
    const node = {
        id: 'node-requirement:001', offsetWidth: 280, offsetHeight: 999, isConnected: true,
        getBoundingClientRect: () => nodeRect, closest: selector => selector === '.node' ? node : null,
        querySelector: selector => selector === '.child-add-region' ? region : selector === 'details' ? details : null,
        classList: classes()
    };
    const region = {
        dataset: { parentKey: 'requirement:001' }, disabled: false,
        title: '下位要求を追加',
        closest: selector => selector === '.node' ? node : selector === '.child-add-region' ? region : null,
        focus: () => {}, classList: classes(),
        getBoundingClientRect: () => ({
            left: nodeRect.left + nodeRect.width / 4, right: nodeRect.right - nodeRect.width / 4,
            top: nodeRect.bottom - 5, bottom: nodeRect.bottom + 5,
            width: nodeRect.width / 2, height: 10
        })
    };
    const details = { open: false, closest: selector => selector === '.node' ? node : selector === 'details' ? details : null };
    const summary = {
        closest: selector => selector === '.node' ? node : selector === 'summary' ? summary : selector === 'details' ? details : null
    };
    const style = { setProperty(name, value) { this[name] = value; } };
    const world = { style, querySelectorAll: () => { fullNodeQueries++; return [node]; }, closest: () => null };
    const status = { textContent: '' };
    const root = new EventTarget();
    Object.assign(root, {
        classList: classes(), title: '',
        querySelector: selector => selector === '.mindmap-world' ? world : null,
        closest: selector => selector === '.editor-shell' ? { querySelector: () => status } : null,
        getBoundingClientRect: () => ({ left: 0, top: 0 }),
        setPointerCapture: id => capturedPointers.add(id),
        hasPointerCapture: id => capturedPointers.has(id),
        releasePointerCapture: id => capturedPointers.delete(id)
    });
    globalThis.requestAnimationFrame = callback => { const id = nextFrame++; frames.set(id, callback); return id; };
    globalThis.cancelAnimationFrame = id => frames.delete(id);
    globalThis.ResizeObserver = class {
        constructor(callback) { this.callback = callback; resize = this; }
        observe() {}
        unobserve() {}
        disconnect() { this.disconnected = true; }
    };
    globalThis.MutationObserver = class {
        constructor(callback) { this.callback = callback; mutation = this; }
        observe() {}
        disconnect() { this.disconnected = true; }
    };
    globalThis.HTMLDetailsElement = class { closest() { return node; } };
    globalThis.document = {
        elementFromPoint: (x, y) => x >= nodeRect.left && x <= nodeRect.right && y >= nodeRect.top && y <= nodeRect.bottom ? node : null
    };

    return {
        root, world, status, calls, node, region, summary, details, capturedPointers,
        attach: (...settings) => attachCanvas(root, {
            invokeMethodAsync: (method, ...args) => {
                calls.push({ method, args });
                return method === 'RequestChildAddition' ? additionResponse(...args) : Promise.resolve();
            }
        }, ...settings),
        setNodeRect: rect => nodeRect = rect,
        setAdditionResponse: response => additionResponse = response,
        fullNodeQueries: () => fullNodeQueries,
        additionCalls: () => calls.filter(call => call.method === 'RequestChildAddition'),
        pointer(type, options = {}) {
            const event = new Event(type, { cancelable: true });
            const { target = region, x = nodeRect.left + nodeRect.width / 2, y = nodeRect.bottom, ...properties } = options;
            Object.assign(event, { clientX: x, clientY: y, pointerId: 1, button: 0, isPrimary: true, ...properties });
            Object.defineProperty(event, 'target', { value: target });
            root.dispatchEvent(event);
            return event;
        },
        reasonClick(options = {}) {
            const event = this.pointer('click', { target: summary, detail: 1, ...options });
            if (!event.defaultPrevented) details.open = !details.open;
            return event;
        },
        resize: height => resize.callback([{ target: node, borderBoxSize: [{ inlineSize: 280, blockSize: height }] }]),
        flush() { const callbacks = [...frames.values()]; frames.clear(); callbacks.forEach(callback => callback()); },
        wheel(deltaY = -1) {
            const event = new Event('wheel', { cancelable: true });
            Object.assign(event, { clientX: 100, clientY: 120, deltaY });
            root.dispatchEvent(event);
        },
        observers: () => [resize, mutation]
    };
}

test('size notifications are batched, use CSS border pixels, and include collapse shrinkage', () => {
    const h = harness();
    h.attach();
    try {
        h.resize(160.25);
        h.resize(180.5);
        assert.equal(h.calls.length, 0);
        h.flush();
        assert.deepEqual(h.calls, [{ method: 'NodeSizesChanged', args: [[{ key: 'requirement:001', width: 280, height: 180.5 }]] }]);
        h.resize(180.5);
        h.flush();
        assert.equal(h.calls.length, 1, 'unchanged sizes must not cause a rendering loop');
        h.resize(110);
        h.flush();
        assert.equal(h.calls[1].args[0][0].height, 110);
        h.wheel();
        assert.equal(h.calls.filter(call => call.method === 'NodeSizesChanged').length, 2, 'zoom must not remeasure nodes');
    } finally { detachCanvas(h.root); }
});

test('reset changes the same zoom and pan state used by the next wheel operation', () => {
    const h = harness();
    h.attach(1.6, 25, -15);
    try {
        resetCanvas(h.root, 1);
        assert.equal(h.world.style.transform, 'translate(0px, 0px) scale(1)');
        h.wheel();
        assert.equal(h.status.textContent, '110%');
        assert.match(h.world.style.transform, /scale\(1\.1\)$/);
        assert.deepEqual(h.calls[0], { method: 'CanvasChanged', args: [1, 0, 0] });
    } finally { detachCanvas(h.root); }
});

test('delegated reason toggles keep working with replacement details elements', () => {
    const h = harness();
    h.attach();
    try {
        for (const open of [true, false, true]) {
            const details = new HTMLDetailsElement();
            details.open = open;
            const event = new Event('toggle');
            Object.defineProperty(event, 'target', { value: details });
            h.root.dispatchEvent(event);
        }
        assert.deepEqual(h.calls.map(call => call.args), [
            ['requirement:001', true], ['requirement:001', false], ['requirement:001', true]
        ]);
    } finally { detachCanvas(h.root); }
});

test('detach releases observers, events, and a pending measurement batch', () => {
    const h = harness();
    h.attach();
    h.resize(160);
    const transform = h.world.style.transform;
    detachCanvas(h.root);
    h.flush();
    h.wheel();
    assert.equal(h.calls.length, 0);
    assert.equal(h.world.style.transform, transform);
    assert.ok(h.observers().every(observer => observer.disconnected));
});

test('the addition ellipse is ten screen pixels high and half the displayed node width at 25%, 100%, and 200%', async () => {
    for (const zoom of [.25, 1, 2]) {
        const h = harness();
        h.attach(zoom);
        try {
            assert.equal(h.world.style['--child-add-height'], `${10 / zoom}px`);
            const left = 51.5;
            const right = left + 280 * zoom;
            const bottom = 202.25;
            h.setNodeRect({ left, right, bottom, top: bottom - 110 * zoom, width: 280 * zoom, height: 110 * zoom });
            const centerX = (left + right) / 2;
            const radiusX = (right - left) / 4;
            const accepted = [
                [centerX, bottom],
                [centerX - radiusX, bottom], [centerX + radiusX, bottom],
                [centerX, bottom - 5], [centerX, bottom + 5],
                [centerX + radiusX * .6, bottom + 4],
                [centerX - radiusX * .6, bottom - 4]
            ];
            for (const [x, y] of accepted) {
                const count = h.additionCalls().length;
                h.pointer('pointerdown', { x, y });
                h.pointer('pointerup', { x, y });
                h.pointer('click', { x, y, detail: 1 });
                assert.equal(h.additionCalls().length, count + 1, 'pointerup plus click must create one request');
                assert.deepEqual(h.additionCalls().at(-1).args, ['requirement:001']);
                await Promise.resolve();
                setAdditionDialogOpen(h.root, false);
            }
            for (const [x, y] of [
                [centerX - radiusX - .1, bottom], [centerX + radiusX + .1, bottom],
                [centerX, bottom - 5.1], [centerX, bottom + 5.1],
                [centerX - radiusX, bottom - 5], [centerX + radiusX, bottom - 5],
                [centerX - radiusX, bottom + 5], [centerX + radiusX, bottom + 5],
                [centerX + radiusX * .6, bottom + 4.01],
                [left, bottom], [right, bottom]
            ]) {
                h.pointer('pointerdown', { x, y });
                h.pointer('pointerup', { x, y });
                h.pointer('click', { x, y, detail: 1 });
            }
            assert.equal(h.additionCalls().length, accepted.length, 'ellipse corners and points just beyond its boundary must not add children');
        } finally { detachCanvas(h.root); }
    }
});

test('node body outside the ellipse, right button, and secondary pointer do not open child addition', () => {
    const h = harness();
    h.attach();
    try {
        const initialTransform = h.world.style.transform;
        for (const options of [{ target: h.node, y: 200 }, { button: 2 }, { isPrimary: false }]) {
            h.pointer('pointerdown', options);
            h.pointer('pointermove', { ...options, x: 180, y: 300 });
            h.pointer('pointerup', options);
            h.pointer('click', { ...options, detail: 1 });
        }
        assert.equal(h.additionCalls().length, 0);
        assert.equal(h.world.style.transform, initialTransform, 'node operations must not start a canvas pan');
    } finally { detachCanvas(h.root); }
});

test('ellipse coordinates take precedence when the pointer target is the node or a reason summary', async () => {
    for (const targetName of ['node', 'summary']) {
        const h = harness();
        h.attach();
        try {
            const down = h.pointer('pointerdown', { target: h[targetName] });
            const up = h.pointer('pointerup', { target: h[targetName] });
            const click = targetName === 'summary'
                ? h.reasonClick()
                : h.pointer('click', { target: h.node, detail: 1 });
            assert.equal(h.additionCalls().length, 1);
            assert.equal(down.defaultPrevented, true, 'reason default action must be stopped before it happens');
            assert.equal(up.defaultPrevented, true);
            assert.equal(click.defaultPrevented, true);
            assert.equal(h.details.open, false, 'reason must never open during a child-addition click');
            await Promise.resolve();
        } finally { detachCanvas(h.root); }
    }
});

test('the ellipse bounding-box corners and outer node quarters leave reason clicks untouched', () => {
    const h = harness();
    h.attach();
    try {
        for (const [x, y] of [[170, 245], [310, 245], [170, 255], [310, 255], [120, 250], [360, 250], [240, 244.9]]) {
            const originalOpen = h.details.open;
            const down = h.pointer('pointerdown', { target: h.summary, x, y });
            h.pointer('pointerup', { target: h.summary, x, y });
            const click = h.reasonClick({ x, y });
            assert.equal(down.defaultPrevented, false, `reason pointerdown must remain available at (${x}, ${y})`);
            assert.equal(click.defaultPrevented, false);
            assert.equal(h.details.open, !originalOpen);
        }
        assert.equal(h.additionCalls().length, 0);
    } finally { detachCanvas(h.root); }
});

test('the outer ellipse boundary can locate its parent locally without scanning all nodes', () => {
    const h = harness();
    h.attach();
    try {
        const initialQueries = h.fullNodeQueries();
        h.pointer('pointermove', { target: h.world, x: 240, y: 255 });
        h.pointer('pointerdown', { target: h.world, x: 240, y: 255 });
        h.pointer('pointerup', { target: h.world, x: 240, y: 255 });
        h.pointer('click', { target: h.world, x: 240, y: 255, detail: 1 });
        assert.equal(h.additionCalls().length, 1);
        assert.equal(h.fullNodeQueries(), initialQueries, 'pointer hit testing must not traverse every node');
    } finally { detachCanvas(h.root); }
});

test('native keyboard reason activation is not captured by the ellipse at the same coordinates', () => {
    const h = harness();
    h.attach();
    try {
        const reasonClick = h.reasonClick({ detail: 0 });
        assert.equal(reasonClick.defaultPrevented, false);
        assert.equal(h.details.open, true);
        assert.equal(h.additionCalls().length, 0);
        const regionClick = h.pointer('click', { detail: 0 });
        assert.equal(regionClick.defaultPrevented, true);
        assert.equal(h.additionCalls().length, 1);
        assert.equal(h.details.open, true);
    } finally { detachCanvas(h.root); }
});

test('a node without a child-add region cannot create a child from its bottom ellipse', () => {
    const h = harness();
    h.node.querySelector = () => null;
    h.attach();
    try {
        h.pointer('pointerdown', { target: h.node });
        h.pointer('pointerup', { target: h.node });
        h.pointer('click', { target: h.node, detail: 1 });
        assert.equal(h.additionCalls().length, 0);
    } finally { detachCanvas(h.root); }
});

test('ellipse dimensions follow the configured height and width ratio', async () => {
    const h = harness();
    h.attach(2, 0, 0, .25, 2, .1, 12, 4, .75);
    try {
        assert.equal(h.world.style['--child-add-height'], '6px');
        h.pointer('pointerdown', { x: 135, y: 250 });
        h.pointer('pointerup', { x: 135, y: 250 });
        assert.equal(h.additionCalls().length, 1, 'configured ellipse must include its wider left boundary');
        await Promise.resolve();
        setAdditionDialogOpen(h.root, false);
        h.pointer('pointerdown', { x: 240, y: 256 });
        h.pointer('pointerup', { x: 240, y: 256 });
        assert.equal(h.additionCalls().length, 2, 'configured screen height must include the bottom boundary');
    } finally { detachCanvas(h.root); }
});

test('drag, pointer cancellation, lost capture, or release outside the latest ellipse cancels addition', () => {
    const h = harness();
    h.attach();
    try {
        const initialTransform = h.world.style.transform;
        h.pointer('pointerdown');
        h.pointer('pointermove', { x: 245 });
        h.pointer('pointermove');
        h.pointer('pointerup');
        h.pointer('click', { detail: 1 });
        h.pointer('pointerdown');
        h.pointer('pointerup', { x: 245 });
        for (const cancel of ['pointercancel', 'lostpointercapture']) {
            h.pointer('pointerdown');
            h.pointer(cancel);
            h.pointer('pointerup');
            h.pointer('click', { detail: 1 });
        }
        h.pointer('pointerdown');
        h.setNodeRect({ left: 100, right: 380, top: 140, bottom: 300, width: 280, height: 160 });
        h.pointer('pointerup', { y: 250 });
        assert.equal(h.additionCalls().length, 0);
        assert.equal(h.world.style.transform, initialTransform, 'a canceled ellipse drag must not pan');
        h.pointer('pointerdown');
        h.pointer('pointermove', { x: 244 });
        h.pointer('pointerup', { x: 244 });
        assert.equal(h.additionCalls().length, 1, 'movement at the configured tolerance remains a click');
    } finally { detachCanvas(h.root); }
});

test('drag and canceled ellipse gestures never fall back to reason or canvas pan', () => {
    for (const cancel of ['drag', 'pointercancel', 'lostpointercapture']) {
        const h = harness();
        h.attach();
        try {
            const originalTransform = h.world.style.transform;
            h.pointer('pointerdown', { target: h.summary });
            if (cancel === 'drag') {
                h.pointer('pointermove', { target: h.summary, x: 245 });
                h.pointer('pointermove', { target: h.summary });
            } else h.pointer(cancel, { target: h.summary });
            h.pointer('pointerup', { target: h.summary });
            const click = h.reasonClick();
            assert.equal(h.additionCalls().length, 0);
            assert.equal(click.defaultPrevented, true);
            assert.equal(h.details.open, false);
            assert.equal(h.world.style.transform, originalTransform);
            h.pointer('pointerdown', { target: h.summary, y: 200 });
            h.pointer('pointerup', { target: h.summary, y: 200 });
            const ordinaryClick = h.reasonClick({ y: 200 });
            assert.equal(ordinaryClick.defaultPrevented, false, 'suppression must not leak into the next ordinary reason gesture');
            assert.equal(h.details.open, true);
        } finally { detachCanvas(h.root); }
    }
});

test('release rechecks the latest node width, height, position, and connected state', () => {
    for (const change of ['width', 'height', 'pan', 'disconnected']) {
        const h = harness();
        h.attach();
        try {
            h.pointer('pointerdown', { x: 300 });
            if (change === 'width') h.setNodeRect({ left: 100, right: 300, top: 140, bottom: 250, width: 200, height: 110 });
            if (change === 'height') h.setNodeRect({ left: 100, right: 380, top: 140, bottom: 350, width: 280, height: 210 });
            if (change === 'pan') h.setNodeRect({ left: 300, right: 580, top: 340, bottom: 450, width: 280, height: 110 });
            if (change === 'disconnected') h.node.isConnected = false;
            h.pointer('pointerup', { x: 300, y: 250 });
            assert.equal(h.additionCalls().length, 0, `stale geometry must not activate after ${change}`);
        } finally { detachCanvas(h.root); }
    }
});

test('detaching an active ellipse gesture releases capture and removes click interception', () => {
    const h = harness();
    h.attach();
    h.pointer('pointerdown', { target: h.summary });
    assert.equal(h.capturedPointers.has(1), true);
    detachCanvas(h.root);
    assert.equal(h.capturedPointers.has(1), false);
    h.pointer('pointerup', { target: h.summary });
    const click = h.reasonClick();
    assert.equal(click.defaultPrevented, false);
    assert.equal(h.details.open, true);
    assert.equal(h.additionCalls().length, 0);
});

test('hover feedback follows the actual ellipse even when a reason summary is the event target', () => {
    const h = harness();
    h.attach();
    try {
        h.pointer('pointermove', { target: h.summary });
        assert.equal(h.region.classList.contains('is-hovered'), true);
        assert.equal(h.root.classList.contains('child-add-hover'), true);
        assert.equal(h.root.title, '下位要求を追加');
        h.pointer('pointermove', { target: h.summary, x: 170, y: 245 });
        assert.equal(h.region.classList.contains('is-hovered'), false, 'bounding-box corner must not highlight as an ellipse hit');
        assert.equal(h.root.classList.contains('child-add-hover'), false);
        h.pointer('pointermove', { target: h.summary });
        h.pointer('pointerleave', { target: h.summary });
        assert.equal(h.region.classList.contains('is-hovered'), false);
        assert.equal(h.root.classList.contains('child-add-hover'), false);
        for (const invalidate of [() => h.wheel(), () => h.resize(160), () => h.observers()[1].callback([])]) {
            h.pointer('pointermove', { target: h.summary });
            invalidate();
            assert.equal(h.region.classList.contains('is-hovered'), false, 'geometry updates must clear stale supplemental hover');
            assert.equal(h.root.classList.contains('child-add-hover'), false);
            assert.equal(h.root.title, '');
        }
        for (const end of ['pointercancel', 'lostpointercapture']) {
            h.pointer('pointermove', { target: h.summary });
            h.pointer('pointerdown', { target: h.summary });
            h.pointer('pointermove', { target: h.summary, x: 340 });
            assert.equal(h.region.classList.contains('is-hovered'), false, 'dragging out must remove hover');
            h.pointer('pointermove', { target: h.summary });
            assert.equal(h.region.classList.contains('is-hovered'), true);
            h.pointer(end, { target: h.summary });
            assert.equal(h.region.classList.contains('is-hovered'), false, 'cancellation must remove hover');
        }
    } finally { detachCanvas(h.root); }
});

test('rapid clicks and native keyboard clicks share the in-flight and open-dialog lock', async () => {
    const h = harness();
    let resolveRequest;
    h.setAdditionResponse(() => new Promise(resolve => resolveRequest = resolve));
    h.attach();
    try {
        h.pointer('pointerdown');
        h.pointer('pointerup');
        h.pointer('pointerdown');
        h.pointer('pointerup');
        h.pointer('click', { detail: 0 });
        assert.equal(h.additionCalls().length, 1, 'requests must be locked before server interop completes');
        resolveRequest(true);
        await Promise.resolve();
        h.pointer('click', { detail: 0 });
        assert.equal(h.additionCalls().length, 1, 'an open dialog keeps the lock');
        setAdditionDialogOpen(h.root, false);
        h.pointer('click', { detail: 0 });
        assert.equal(h.additionCalls().length, 2, 'native Enter/Space activation opens the same dialog after close');
        setAdditionDialogOpen(h.root, false);
        resolveRequest(true);
        await Promise.resolve();
        h.pointer('click', { detail: 0 });
        assert.equal(h.additionCalls().length, 3, 'a late reply from a closed dialog must not relock the canvas');
    } finally { detachCanvas(h.root); }
});

test('rejected or failed addition requests release the lock; explicit dialog state also locks', async () => {
    const h = harness();
    h.setAdditionResponse(() => Promise.resolve(false));
    h.attach();
    try {
        h.pointer('click', { detail: 0 });
        await Promise.resolve();
        h.pointer('click', { detail: 0 });
        assert.equal(h.additionCalls().length, 2);
        await Promise.resolve();
        h.setAdditionResponse(() => Promise.reject(new Error('disconnected')));
        h.pointer('click', { detail: 0 });
        await Promise.resolve();
        h.pointer('click', { detail: 0 });
        assert.equal(h.additionCalls().length, 4);
        await Promise.resolve();
        setAdditionDialogOpen(h.root, true);
        h.pointer('click', { detail: 0 });
        assert.equal(h.additionCalls().length, 4);
        setAdditionDialogOpen(h.root, false);
        h.pointer('click', { detail: 0 });
        assert.equal(h.additionCalls().length, 5);
    } finally { detachCanvas(h.root); }
});
