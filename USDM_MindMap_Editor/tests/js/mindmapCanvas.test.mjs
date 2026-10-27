import assert from 'node:assert/strict';
import test from 'node:test';
import { attachCanvas, detachCanvas, resetCanvas } from '../../src/USDM_MindMap_Editor.Web/wwwroot/js/mindmapCanvas.js';

// Small DOM/observer doubles exercise the public JS interop boundary without browser dependencies.
function harness() {
    let resize;
    let mutation;
    let nextFrame = 1;
    const frames = new Map();
    const calls = [];
    const node = { id: 'node-requirement:001', offsetWidth: 280, offsetHeight: 999 };
    const world = { style: {}, querySelectorAll: () => [node] };
    const status = { textContent: '' };
    const root = new EventTarget();
    Object.assign(root, {
        querySelector: selector => selector === '.mindmap-world' ? world : null,
        closest: selector => selector === '.editor-shell' ? { querySelector: () => status } : null,
        getBoundingClientRect: () => ({ left: 0, top: 0 }),
        setPointerCapture: () => {}
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
        root, world, status, calls,
        attach: (...settings) => attachCanvas(root, {
            invokeMethodAsync: (method, ...args) => { calls.push({ method, args }); return Promise.resolve(); }
        }, ...settings),
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
