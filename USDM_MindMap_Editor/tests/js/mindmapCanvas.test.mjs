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
    let nodeRect = { left: 100, right: 380, top: 140, bottom: 250, width: 280, height: 110 };
    let additionResponse = () => Promise.resolve(true);
    const node = {
        id: 'node-requirement:001', offsetWidth: 280, offsetHeight: 999, isConnected: true,
        getBoundingClientRect: () => nodeRect, closest: selector => selector === '.node' ? node : null
    };
    const region = {
        dataset: { parentKey: 'requirement:001' }, disabled: false,
        closest: selector => selector === '.node' ? node : selector === '.child-add-region' ? region : null,
        focus: () => {}
    };
    const style = { setProperty(name, value) { this[name] = value; } };
    const world = { style, querySelectorAll: () => [node] };
    const status = { textContent: '' };
    const root = new EventTarget();
    Object.assign(root, {
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

    return {
        root, world, status, calls, node, region,
        attach: (...settings) => attachCanvas(root, {
            invokeMethodAsync: (method, ...args) => {
                calls.push({ method, args });
                return method === 'RequestChildAddition' ? additionResponse(...args) : Promise.resolve();
            }
        }, ...settings),
        setNodeRect: rect => nodeRect = rect,
        setAdditionResponse: response => additionResponse = response,
        additionCalls: () => calls.filter(call => call.method === 'RequestChildAddition'),
        pointer(type, options = {}) {
            const event = new Event(type, { cancelable: true });
            const { target = region, x = nodeRect.left + 20, y = nodeRect.bottom, ...properties } = options;
            Object.assign(event, { clientX: x, clientY: y, pointerId: 1, button: 0, isPrimary: true, ...properties });
            Object.defineProperty(event, 'target', { value: target });
            root.dispatchEvent(event);
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

test('the child addition band stays four screen CSS pixels at 25%, 100%, and 200%', async () => {
    for (const zoom of [.25, 1, 2]) {
        const h = harness();
        h.attach(zoom);
        try {
            assert.equal(h.world.style['--child-add-height'], `${4 / zoom}px`);
            const left = 51.5;
            const right = left + 280 * zoom;
            const bottom = 202.25;
            h.setNodeRect({ left, right, bottom, top: bottom - 110 * zoom, width: 280 * zoom, height: 110 * zoom });
            for (const [x, y] of [[left, bottom - 2], [right, bottom + 2], [(left + right) / 2, bottom]]) {
                const count = h.additionCalls().length;
                h.pointer('pointerdown', { x, y });
                h.pointer('pointerup', { x, y });
                h.pointer('click', { x, y, detail: 1 });
                assert.equal(h.additionCalls().length, count + 1, 'pointerup plus click must create one request');
                assert.deepEqual(h.additionCalls().at(-1).args, ['requirement:001']);
                await Promise.resolve();
                setAdditionDialogOpen(h.root, false);
            }
            for (const [x, y] of [[left - .1, bottom], [right + .1, bottom], [left, bottom - 2.1], [left, bottom + 2.1]]) {
                h.pointer('pointerdown', { x, y });
                h.pointer('pointerup', { x, y });
                h.pointer('click', { x, y, detail: 1 });
            }
            assert.equal(h.additionCalls().length, 3, 'points outside the node border band must do nothing');
        } finally { detachCanvas(h.root); }
    }
});

test('node body, right button, and secondary pointer do not open child addition', () => {
    const h = harness();
    h.attach();
    try {
        const initialTransform = h.world.style.transform;
        for (const options of [{ target: h.node }, { button: 2 }, { isPrimary: false }]) {
            h.pointer('pointerdown', options);
            h.pointer('pointermove', { ...options, x: 180, y: 300 });
            h.pointer('pointerup', options);
            h.pointer('click', { ...options, detail: 1 });
        }
        assert.equal(h.additionCalls().length, 0);
        assert.equal(h.world.style.transform, initialTransform, 'node operations must not start a canvas pan');
    } finally { detachCanvas(h.root); }
});

test('drag, pointer cancellation, lost capture, or release outside the latest border cancels addition', () => {
    const h = harness();
    h.attach();
    try {
        const initialTransform = h.world.style.transform;
        h.pointer('pointerdown');
        h.pointer('pointermove', { x: 125 });
        h.pointer('pointermove');
        h.pointer('pointerup');
        h.pointer('click', { detail: 1 });
        h.pointer('pointerdown');
        h.pointer('pointerup', { x: 125 });
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
        assert.equal(h.world.style.transform, initialTransform, 'a canceled border drag must not pan');
        h.pointer('pointerdown');
        h.pointer('pointermove', { x: 124 });
        h.pointer('pointerup', { x: 124 });
        assert.equal(h.additionCalls().length, 1, 'movement at the configured tolerance remains a click');
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
