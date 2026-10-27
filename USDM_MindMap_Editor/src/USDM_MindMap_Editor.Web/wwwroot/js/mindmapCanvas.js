const canvases = new WeakMap();

export function attachCanvas(root, dotnet, initialZoom = 1, initialPanX = 0, initialPanY = 0, minZoom = .25, maxZoom = 2, zoomStep = .1) {
    detachCanvas(root);
    const world = root.querySelector('.mindmap-world');
    if (!world) return;

    let zoom = initialZoom;
    let panX = initialPanX;
    let panY = initialPanY;
    let dragging = false;
    let lastX = 0;
    let lastY = 0;
    let syncTimer;
    let measurementFrame;
    let disposed = false;
    const events = new AbortController();
    const pendingMeasurements = new Map();
    const reportedSizes = new Map();
    const observedNodes = new Set();
    const status = root.closest('.editor-shell')?.querySelector('.zoom-status');

    const invoke = (method, ...args) => {
        if (!disposed) dotnet.invokeMethodAsync(method, ...args).catch(() => {});
    };

    const scheduleSync = () => {
        clearTimeout(syncTimer);
        syncTimer = setTimeout(() => invoke('CanvasChanged', zoom, panX, panY), 100);
    };

    const apply = () => {
        world.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;
        if (status) status.textContent = `${Math.round(zoom * 100)}%`;
    };

    // borderBoxSize uses untransformed CSS pixels, including fractional border sizes.
    // Canvas transforms therefore do not invalidate node measurements.
    const resizeObserver = new ResizeObserver(entries => {
        for (const entry of entries) {
            const border = entry.borderBoxSize?.[0];
            const key = entry.target.id.substring(5);
            const size = {
                key,
                width: border?.inlineSize ?? entry.target.offsetWidth,
                height: border?.blockSize ?? entry.target.offsetHeight
            };
            const previous = reportedSizes.get(key);
            if (previous?.width === size.width && previous?.height === size.height) continue;
            reportedSizes.set(key, size);
            pendingMeasurements.set(key, size);
        }
        if (pendingMeasurements.size === 0 || measurementFrame) return;
        measurementFrame = requestAnimationFrame(() => {
            measurementFrame = undefined;
            const measurements = [...pendingMeasurements.values()];
            pendingMeasurements.clear();
            invoke('NodeSizesChanged', measurements);
        });
    });

    const observeNode = node => {
        if (observedNodes.has(node)) return;
        observedNodes.add(node);
        resizeObserver.observe(node, { box: 'border-box' });
    };
    world.querySelectorAll('.node').forEach(observeNode);

    // Inserted nodes are observed without measuring the whole logical document again.
    const mutations = new MutationObserver(records => {
        for (const record of records) {
            for (const added of record.addedNodes) {
                if (!(added instanceof Element)) continue;
                if (added.matches('.node')) observeNode(added);
                added.querySelectorAll('.node').forEach(observeNode);
            }
            for (const removed of record.removedNodes) {
                if (!(removed instanceof Element)) continue;
                const nodes = removed.matches('.node') ? [removed] : removed.querySelectorAll('.node');
                for (const node of nodes) {
                    resizeObserver.unobserve(node);
                    observedNodes.delete(node);
                    reportedSizes.delete(node.id.substring(5));
                    pendingMeasurements.delete(node.id.substring(5));
                }
            }
        }
    });
    mutations.observe(world, { childList: true, subtree: true });

    root.addEventListener('wheel', event => {
        event.preventDefault();
        const bounds = root.getBoundingClientRect();
        const pointerX = event.clientX - bounds.left;
        const pointerY = event.clientY - bounds.top;
        const nextZoom = Math.min(maxZoom, Math.max(minZoom, zoom + (event.deltaY < 0 ? zoomStep : -zoomStep)));
        const canvasX = (pointerX - panX) / zoom;
        const canvasY = (pointerY - panY) / zoom;
        zoom = nextZoom;
        panX = pointerX - canvasX * zoom;
        panY = pointerY - canvasY * zoom;
        apply();
        scheduleSync();
    }, { passive: false, signal: events.signal });

    root.addEventListener('pointerdown', event => {
        if (event.button !== 0 || event.target.closest('.node')) return;
        event.preventDefault();
        dragging = true;
        lastX = event.clientX;
        lastY = event.clientY;
        root.setPointerCapture(event.pointerId);
    }, { signal: events.signal });
    root.addEventListener('pointermove', event => {
        if (!dragging) return;
        panX += event.clientX - lastX;
        panY += event.clientY - lastY;
        lastX = event.clientX;
        lastY = event.clientY;
        apply();
        scheduleSync();
    }, { signal: events.signal });
    root.addEventListener('pointerup', () => dragging = false, { signal: events.signal });
    root.addEventListener('pointercancel', () => dragging = false, { signal: events.signal });

    // toggle does not bubble; capture survives Blazor updates to details elements.
    root.addEventListener('toggle', event => {
        if (!(event.target instanceof HTMLDetailsElement)) return;
        const node = event.target.closest('.node');
        if (node) invoke('ReasonChanged', node.id.substring(5), event.target.open);
    }, { capture: true, signal: events.signal });

    canvases.set(root, {
        reset(defaultZoom) {
            clearTimeout(syncTimer);
            zoom = defaultZoom;
            panX = 0;
            panY = 0;
            apply();
            invoke('CanvasChanged', zoom, panX, panY);
        },
        dispose() {
            disposed = true;
            clearTimeout(syncTimer);
            if (measurementFrame) cancelAnimationFrame(measurementFrame);
            events.abort();
            mutations.disconnect();
            resizeObserver.disconnect();
            pendingMeasurements.clear();
            observedNodes.clear();
            reportedSizes.clear();
        }
    });
    apply();
}

export function resetCanvas(root, defaultZoom = 1) {
    canvases.get(root)?.reset(defaultZoom);
}

export function detachCanvas(root) {
    canvases.get(root)?.dispose();
    canvases.delete(root);
}
