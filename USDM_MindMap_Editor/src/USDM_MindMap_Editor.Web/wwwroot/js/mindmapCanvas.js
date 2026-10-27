const canvases = new WeakMap();

export function attachCanvas(root, dotnet, initialZoom = 1, initialPanX = 0, initialPanY = 0, minZoom = .25, maxZoom = 2, zoomStep = .1, hitHeight = 4, dragTolerance = 4) {
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
    let additionGesture;
    let additionPending = false;
    let additionDialogOpen = false;
    let additionRequestVersion = 0;
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
        world.style.setProperty('--child-add-height', `${hitHeight / zoom}px`);
        if (status) status.textContent = `${Math.round(zoom * 100)}%`;
    };

    const inAdditionBand = (node, x, y) => {
        const rect = node.getBoundingClientRect();
        return node.isConnected !== false && x >= rect.left && x <= rect.right
            && y >= rect.bottom - hitHeight / 2 && y <= rect.bottom + hitHeight / 2;
    };

    const clearAdditionGesture = () => {
        const pointerId = additionGesture?.pointerId;
        additionGesture = undefined;
        if (pointerId !== undefined && root.hasPointerCapture(pointerId)) root.releasePointerCapture(pointerId);
    };

    const requestChildAddition = async key => {
        if (disposed || additionPending || additionDialogOpen) return;
        additionPending = true;
        const version = ++additionRequestVersion;
        try {
            const opened = await dotnet.invokeMethodAsync('RequestChildAddition', key);
            if (disposed || version !== additionRequestVersion) return;
            additionPending = false;
            if (opened === true) additionDialogOpen = true;
        } catch {
            if (!disposed && version === additionRequestVersion) additionPending = false;
        }
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
        const region = event.target.closest?.('.child-add-region');
        if (region) {
            event.stopPropagation();
            if (event.button !== 0 || event.isPrimary === false) return;
            event.preventDefault();
            dragging = false;
            if (additionGesture || additionPending || additionDialogOpen || region.disabled) return;
            const node = region.closest('.node');
            if (!node || !inAdditionBand(node, event.clientX, event.clientY)) return;
            region.focus({ preventScroll: true });
            additionGesture = {
                pointerId: event.pointerId, node, key: region.dataset.parentKey,
                startX: event.clientX, startY: event.clientY, canceled: false
            };
            root.setPointerCapture(event.pointerId);
            return;
        }
        if (additionGesture || additionPending || additionDialogOpen) return;
        if (event.button !== 0 || event.target.closest('.node')) return;
        event.preventDefault();
        dragging = true;
        lastX = event.clientX;
        lastY = event.clientY;
        root.setPointerCapture(event.pointerId);
    }, { signal: events.signal });
    root.addEventListener('pointermove', event => {
        if (additionGesture?.pointerId === event.pointerId) {
            event.stopPropagation();
            event.preventDefault();
            if (Math.hypot(event.clientX - additionGesture.startX, event.clientY - additionGesture.startY) > dragTolerance)
                additionGesture.canceled = true;
            return;
        }
        if (!dragging) return;
        panX += event.clientX - lastX;
        panY += event.clientY - lastY;
        lastX = event.clientX;
        lastY = event.clientY;
        apply();
        scheduleSync();
    }, { signal: events.signal });
    root.addEventListener('pointerup', event => {
        if (additionGesture?.pointerId === event.pointerId) {
            event.stopPropagation();
            event.preventDefault();
            const gesture = additionGesture;
            clearAdditionGesture();
            if (event.button === 0 && !gesture.canceled
                && Math.hypot(event.clientX - gesture.startX, event.clientY - gesture.startY) <= dragTolerance
                && inAdditionBand(gesture.node, event.clientX, event.clientY)) void requestChildAddition(gesture.key);
            return;
        }
        dragging = false;
    }, { signal: events.signal });
    root.addEventListener('pointercancel', event => {
        if (additionGesture?.pointerId === event.pointerId) clearAdditionGesture();
        dragging = false;
    }, { signal: events.signal });
    root.addEventListener('lostpointercapture', event => {
        if (additionGesture?.pointerId === event.pointerId) additionGesture = undefined;
        dragging = false;
    }, { signal: events.signal });
    root.addEventListener('click', event => {
        const region = event.target.closest?.('.child-add-region');
        if (!region) return;
        event.stopPropagation();
        event.preventDefault();
        // Native button Enter/Space activation has detail=0. Pointer activation was
        // already handled at pointerup, including drag/cancel and border hit checks.
        if (event.detail === 0 && !region.disabled) void requestChildAddition(region.dataset.parentKey);
    }, { signal: events.signal });

    // toggle does not bubble; capture survives Blazor updates to details elements.
    root.addEventListener('toggle', event => {
        if (!(event.target instanceof HTMLDetailsElement)) return;
        const node = event.target.closest('.node');
        if (node) invoke('ReasonChanged', node.id.substring(5), event.target.open);
    }, { capture: true, signal: events.signal });

    canvases.set(root, {
        setAdditionDialogOpen(open) {
            additionDialogOpen = open;
            additionPending = false;
            dragging = false;
            clearAdditionGesture();
            // Closing also invalidates a late interop response from the prior dialog.
            if (!open) additionRequestVersion++;
        },
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
            additionRequestVersion++;
            clearAdditionGesture();
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

export function setAdditionDialogOpen(root, open) {
    canvases.get(root)?.setAdditionDialogOpen(open);
}
