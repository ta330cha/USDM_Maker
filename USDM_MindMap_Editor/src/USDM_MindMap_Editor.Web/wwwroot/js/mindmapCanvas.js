const canvases = new WeakMap();

export function attachCanvas(root, dotnet, initialZoom = 1, initialPanX = 0, initialPanY = 0, minZoom = .25, maxZoom = 2, zoomStep = .1, hitHeight = 10, dragTolerance = 4, widthRatio = .5) {
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
    let suppressedPointerClick;
    let hoveredAdditionRegion;
    let additionPending = false;
    let additionDialogOpen = false;
    let additionRequestVersion = 0;
    const events = new AbortController();
    const pendingMeasurements = new Map();
    const reportedSizes = new Map();
    const observedNodes = new Set();
    const status = root.closest('.editor-shell')?.querySelector('.zoom-status');
    const canvasTitle = root.title;

    const invoke = (method, ...args) => {
        if (!disposed) dotnet.invokeMethodAsync(method, ...args).catch(() => {});
    };

    const scheduleSync = () => {
        clearTimeout(syncTimer);
        syncTimer = setTimeout(() => invoke('CanvasChanged', zoom, panX, panY), 100);
    };

    const apply = () => {
        // The last pointer event may refer to the pre-transform position.
        setAdditionHover(null);
        world.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;
        world.style.setProperty('--child-add-height', `${hitHeight / zoom}px`);
        world.style.setProperty('--child-add-width-ratio', `${widthRatio}`);
        if (status) status.textContent = `${Math.round(zoom * 100)}%`;
    };

    const inAdditionEllipse = (node, x, y) => {
        const rect = node.getBoundingClientRect();
        if (node.isConnected === false || rect.width <= 0 || widthRatio <= 0 || hitHeight <= 0) return false;
        const dx = (x - (rect.left + rect.width / 2)) / (rect.width * widthRatio / 2);
        const dy = (y - rect.bottom) / (hitHeight / 2);
        // Only absorb floating-point roundoff at the mathematical boundary.
        return dx * dx + dy * dy <= 1 + 1e-10;
    };

    const nodeFromTarget = target => target?.closest?.('.node') ?? target?.parentElement?.closest('.node');

    const additionAt = event => {
        let node = nodeFromTarget(event.target);
        if (!node && Number.isFinite(event.clientX) && Number.isFinite(event.clientY)) {
            // A clipped ellipse's outer boundary can hit the canvas instead of the
            // button. Probe one nearby point inside the parent, never all nodes.
            node = nodeFromTarget(globalThis.document?.elementFromPoint?.(
                event.clientX, event.clientY - hitHeight / 2 - 1));
        }
        const region = node?.querySelector('.child-add-region');
        return region && inAdditionEllipse(node, event.clientX, event.clientY) ? { node, region } : null;
    };

    const setAdditionHover = region => {
        if (hoveredAdditionRegion === region) return;
        hoveredAdditionRegion?.classList.remove('is-hovered');
        hoveredAdditionRegion = region;
        region?.classList.add('is-hovered');
        root.classList.toggle('child-add-hover', Boolean(region));
        root.title = region?.title ?? canvasTitle;
    };

    const consumeAdditionEvent = event => {
        event.preventDefault();
        event.stopImmediatePropagation();
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
        setAdditionHover(null);
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
        setAdditionHover(null);
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
        if (!additionGesture) suppressedPointerClick = undefined;
        const addition = additionAt(event);
        if (addition) {
            if (event.button !== 0 || event.isPrimary === false) return;
            // Capture priority also covers summary/text targets underneath the
            // overlay. Its native details activation must never run first.
            consumeAdditionEvent(event);
            suppressedPointerClick = { pointerId: event.pointerId };
            dragging = false;
            const { node, region } = addition;
            if (additionGesture || additionPending || additionDialogOpen || region.disabled) return;
            region.focus({ preventScroll: true });
            additionGesture = {
                pointerId: event.pointerId, node, region, key: region.dataset.parentKey,
                startX: event.clientX, startY: event.clientY, canceled: false
            };
            root.setPointerCapture(event.pointerId);
            return;
        }
        if (additionGesture || additionPending || additionDialogOpen) return;
        if (event.button !== 0 || nodeFromTarget(event.target)) return;
        event.preventDefault();
        dragging = true;
        lastX = event.clientX;
        lastY = event.clientY;
        root.setPointerCapture(event.pointerId);
    }, { capture: true, signal: events.signal });
    root.addEventListener('pointermove', event => {
        if (additionGesture?.pointerId === event.pointerId) {
            setAdditionHover(inAdditionEllipse(additionGesture.node, event.clientX, event.clientY)
                ? additionGesture.region : null);
            consumeAdditionEvent(event);
            if (Math.hypot(event.clientX - additionGesture.startX, event.clientY - additionGesture.startY) > dragTolerance)
                additionGesture.canceled = true;
            return;
        }
        setAdditionHover(additionPending || additionDialogOpen ? null : additionAt(event)?.region ?? null);
        if (!dragging) return;
        panX += event.clientX - lastX;
        panY += event.clientY - lastY;
        lastX = event.clientX;
        lastY = event.clientY;
        apply();
        scheduleSync();
    }, { capture: true, signal: events.signal });
    root.addEventListener('pointerleave', () => setAdditionHover(null), { signal: events.signal });
    root.addEventListener('pointerup', event => {
        if (additionGesture?.pointerId === event.pointerId) {
            consumeAdditionEvent(event);
            const gesture = additionGesture;
            const inside = inAdditionEllipse(gesture.node, event.clientX, event.clientY);
            setAdditionHover(inside ? gesture.region : null);
            clearAdditionGesture();
            if (event.button === 0 && !gesture.canceled
                && Math.hypot(event.clientX - gesture.startX, event.clientY - gesture.startY) <= dragTolerance
                && inside) void requestChildAddition(gesture.key);
            return;
        }
        dragging = false;
    }, { capture: true, signal: events.signal });
    root.addEventListener('pointercancel', event => {
        if (additionGesture?.pointerId === event.pointerId) {
            consumeAdditionEvent(event);
            setAdditionHover(null);
            clearAdditionGesture();
        }
        dragging = false;
    }, { capture: true, signal: events.signal });
    root.addEventListener('lostpointercapture', event => {
        if (additionGesture?.pointerId === event.pointerId) {
            consumeAdditionEvent(event);
            setAdditionHover(null);
            additionGesture = undefined;
        }
        dragging = false;
    }, { capture: true, signal: events.signal });
    root.addEventListener('click', event => {
        if (event.detail === 0) {
            // Keyboard activation follows its focused control, not coordinates:
            // a focused summary must retain normal reason expansion behavior.
            const region = event.target.closest?.('.child-add-region');
            if (region) {
                consumeAdditionEvent(event);
                if (!region.disabled) void requestChildAddition(region.dataset.parentKey);
            }
            return;
        }
        if (suppressedPointerClick && (event.pointerId === undefined
            || event.pointerId === suppressedPointerClick.pointerId)) {
            // Suppress the compatibility click even after dragging out of the
            // ellipse, cancellation, or lost capture. Never fall back to summary.
            suppressedPointerClick = undefined;
            consumeAdditionEvent(event);
            return;
        }
        // A pointer click without a valid ellipse down/up cannot add, but must
        // still not activate the reason underneath this higher-priority region.
        if (additionAt(event)) consumeAdditionEvent(event);
    }, { capture: true, signal: events.signal });

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
            setAdditionHover(null);
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
            suppressedPointerClick = undefined;
            setAdditionHover(null);
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
