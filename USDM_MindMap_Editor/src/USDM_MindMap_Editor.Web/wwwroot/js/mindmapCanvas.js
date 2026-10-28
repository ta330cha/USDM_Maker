const canvases = new WeakMap();

export function attachCanvas(root, dotnet, initialZoom = 1, initialPanX = 0, initialPanY = 0, minZoom = .25, maxZoom = 2, zoomStep = .1, hitHeight = 10, dragTolerance = 4, widthRatio = .5, treeDragTolerance = 4) {
    detachCanvas(root);
    const world = root.querySelector('.mindmap-world');
    if (!world) return;

    let zoom = initialZoom;
    let panX = initialPanX;
    let panY = initialPanY;
    let panGesture;
    let blankClickCount = 0;
    let syncTimer;
    let measurementFrame;
    let disposed = false;
    let additionGesture;
    let suppressedPointerClick;
    let hoveredAdditionRegion;
    let keyboardFocusedRegion;
    let inputModality = 'keyboard';
    let additionOrigin;
    let windowActive = true;
    let treeLayoutRevision = 0;
    let geometryPending = false;
    let geometryRequiredRevision = 0;
    let geometryRequestFailed = false;
    let measurementsInFlight = 0;
    let trees = new Map();
    let treeGesture;
    let treeFrame;
    let committingTree;
    let moveCommitPending = false;
    let moveRequestVersion = 0;
    let additionPending = false;
    let additionDialogOpen = false;
    let additionRequestVersion = 0;
    let toolbarToggleInProgress = false;
    let suppressedToolbarClick = false;
    const events = new AbortController();
    const pendingMeasurements = new Map();
    const reportedSizes = new Map();
    const observedNodes = new Set();
    const status = root.closest('.editor-shell')?.querySelector('.zoom-status');
    const editorShell = root.closest('.editor-shell');
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
        world.style.setProperty('--child-add-focus-width', `${2 / zoom}px`);
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

    const presentationActive = () => windowActive && globalThis.document?.hidden !== true
        && globalThis.document?.visibilityState !== 'hidden';
    const treeInteractionBusy = () => Boolean(treeGesture || moveCommitPending);
    const toolbarToggleBusy = () => toolbarToggleInProgress || additionPending || additionDialogOpen
        || Boolean(additionGesture || panGesture) || treeInteractionBusy();
    const isToolbarToggle = target => Boolean(target?.closest?.('.toolbar-toggle'));
    const geometryBusy = () => geometryPending || Number(root.dataset?.layoutRevision) > treeLayoutRevision;
    const insideEditor = target => editorShell?.contains?.(target) ?? root.contains(target);
    const releaseGeometryBarrier = () => {
        if (measurementsInFlight === 0 && pendingMeasurements.size === 0 && measurementFrame === undefined
            && !geometryRequestFailed && treeLayoutRevision >= geometryRequiredRevision) geometryPending = false;
    };

    const setAdditionHover = region => {
        if (toolbarToggleInProgress || additionPending || additionDialogOpen || treeInteractionBusy() || !presentationActive()) region = null;
        if (hoveredAdditionRegion === region) return;
        if (hoveredAdditionRegion) {
            hoveredAdditionRegion.classList.remove('is-hovered');
            hoveredAdditionRegion.removeAttribute('title');
        }
        hoveredAdditionRegion = region;
        if (region) {
            region.classList.add('is-hovered');
            region.title = region.dataset.tooltip ?? region.getAttribute('aria-label') ?? '';
        }
        root.classList.toggle('child-add-hover', Boolean(region));
        root.title = region?.title ?? canvasTitle;
    };

    const setKeyboardFocus = region => {
        if (additionPending || additionDialogOpen || treeInteractionBusy() || !presentationActive()) region = null;
        if (keyboardFocusedRegion === region) return;
        keyboardFocusedRegion?.classList.remove('is-keyboard-focused');
        keyboardFocusedRegion = region;
        region?.classList.add('is-keyboard-focused');
    };

    const restoreKeyboardFocus = () => {
        const active = globalThis.document?.activeElement;
        const region = active?.closest?.('.child-add-region');
        setKeyboardFocus(inputModality === 'keyboard' && region && root.contains(region) ? region : null);
    };

    const finishAdditionPresentation = () => {
        // Dialog typing never changes this frozen origin. Home restores DOM focus
        // before sending the close notification, so restore only its visual mode.
        const origin = additionOrigin;
        additionOrigin = undefined;
        setAdditionHover(null);
        if (origin) {
            inputModality = origin.modality;
            setKeyboardFocus(origin.modality === 'keyboard' && globalThis.document?.activeElement === origin.region
                ? origin.region : null);
        } else restoreKeyboardFocus();
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

    const requestChildAddition = async (key, region, modality) => {
        if (disposed || toolbarToggleInProgress || additionPending || additionDialogOpen || treeInteractionBusy()) return;
        additionOrigin = { region, modality };
        additionPending = true;
        setAdditionHover(null);
        setKeyboardFocus(null);
        const version = ++additionRequestVersion;
        try {
            const opened = await dotnet.invokeMethodAsync('RequestChildAddition', key);
            if (disposed || version !== additionRequestVersion) return;
            additionPending = false;
            if (opened === true) additionDialogOpen = true;
            else finishAdditionPresentation();
        } catch {
            if (!disposed && version === additionRequestVersion) {
                additionPending = false;
                finishAdditionPresentation();
            }
        }
    };

    const requestBackgroundAddition = async (x, y) => {
        if (disposed || toolbarToggleInProgress || additionPending || additionDialogOpen || treeInteractionBusy() || geometryBusy()) return;
        root.focus({ preventScroll: true });
        additionOrigin = { region: root, modality: 'pointer' };
        additionPending = true;
        setAdditionHover(null);
        setKeyboardFocus(null);
        const version = ++additionRequestVersion;
        try {
            const opened = await dotnet.invokeMethodAsync('RequestBackgroundAddition', x, y);
            if (disposed || version !== additionRequestVersion) return;
            additionPending = false;
            if (opened === true) additionDialogOpen = true;
            else finishAdditionPresentation();
        } catch {
            if (!disposed && version === additionRequestVersion) {
                additionPending = false;
                finishAdditionPresentation();
            }
        }
    };

    const isOperationControl = target => Boolean(target?.closest?.(
        'button, input, textarea, select, summary, a, [contenteditable], [role="button"], dialog'));
    const isBlankCanvas = event => !nodeFromTarget(event.target) && !isOperationControl(event.target)
        && !event.target?.closest?.('.connectors path') && !additionAt(event);
    const screenToCanvas = (x, y) => {
        const rect = root.getBoundingClientRect();
        return { x: (x - rect.left - panX) / zoom, y: (y - rect.top - panY) / zoom };
    };
    const intersects = (a, b) => a.x < b.x + b.width && a.x + a.width > b.x
        && a.y < b.y + b.height && a.y + a.height > b.y;

    const clearTreeFrame = () => {
        if (treeFrame !== undefined) cancelAnimationFrame(treeFrame);
        treeFrame = undefined;
    };
    const restoreTreePreview = gesture => {
        if (!gesture || gesture.previewCleared) return;
        gesture.nodes.style.transform = gesture.nodeTransform;
        gesture.connectors.style.transform = gesture.edgeTransform;
        gesture.nodes.classList.remove('is-dragging', 'is-invalid-drop');
        gesture.connectors.classList.remove('is-dragging', 'is-invalid-drop');
        gesture.previewCleared = true;
        root.classList.remove('is-tree-dragging', 'is-invalid-drop');
    };
    const releaseTreeCapture = gesture => {
        if (gesture && root.hasPointerCapture(gesture.pointerId)) root.releasePointerCapture(gesture.pointerId);
    };
    const cancelTreeDrag = () => {
        const gesture = treeGesture;
        treeGesture = undefined;
        clearTreeFrame();
        restoreTreePreview(gesture);
        releaseTreeCapture(gesture);
        if (gesture?.moved) suppressedPointerClick = { pointerId: gesture.pointerId };
        setAdditionHover(null);
    };
    const updateTreeCandidate = (gesture, x, y) => {
        const screenX = x - gesture.startX;
        const screenY = y - gesture.startY;
        if (!gesture.moved && Math.hypot(screenX, screenY) <= treeDragTolerance) return;
        gesture.moved = true;
        gesture.dx = screenX / gesture.zoom;
        gesture.dy = screenY / gesture.zoom;
        const candidate = { ...gesture.bounds, x: gesture.bounds.x + gesture.dx, y: gesture.bounds.y + gesture.dy };
        gesture.valid = true;
        for (const tree of trees.values()) {
            if (tree.backgroundId !== gesture.backgroundId && intersects(candidate, tree.bounds)) {
                gesture.valid = false;
                break;
            }
        }
    };
    const drawTreeCandidate = gesture => {
        if (!gesture?.moved) return;
        const transform = `translate(${gesture.dx}px, ${gesture.dy}px)`;
        gesture.nodes.style.transform = transform;
        gesture.connectors.style.transform = transform;
        gesture.nodes.classList.add('is-dragging');
        gesture.connectors.classList.add('is-dragging');
        gesture.nodes.classList.toggle('is-invalid-drop', !gesture.valid);
        gesture.connectors.classList.toggle('is-invalid-drop', !gesture.valid);
        root.classList.add('is-tree-dragging');
        root.classList.toggle('is-invalid-drop', !gesture.valid);
    };
    const scheduleTreePreview = () => {
        if (treeFrame !== undefined) return;
        treeFrame = requestAnimationFrame(() => {
            treeFrame = undefined;
            if (treeGesture) updateTreeCandidate(treeGesture, treeGesture.latestX, treeGesture.latestY);
            drawTreeCandidate(treeGesture);
        });
    };
    const finishTreeCommit = () => {
        restoreTreePreview(committingTree);
        committingTree = undefined;
        moveCommitPending = false;
        setAdditionHover(null);
    };
    const commitTreeMove = async gesture => {
        committingTree = gesture;
        moveCommitPending = true;
        const version = ++moveRequestVersion;
        try {
            const accepted = await dotnet.invokeMethodAsync('CommitBackgroundMove', gesture.backgroundId,
                gesture.position.x + gesture.dx, gesture.position.y + gesture.dy, gesture.revision);
            if (disposed || version !== moveRequestVersion) return;
            // On success the preview remains until the authoritative DOM/layout
            // revision arrives. This avoids applying its translation twice.
            if (accepted !== true) finishTreeCommit();
        } catch {
            if (!disposed && version === moveRequestVersion) finishTreeCommit();
        }
    };
    const replaceTreeLayout = (revision, layout) => {
        if (revision < treeLayoutRevision) return;
        if (revision === treeLayoutRevision && trees.size > 0) return;
        cancelTreeDrag();
        if (moveCommitPending) {
            moveRequestVersion++;
            finishTreeCommit();
        }
        const nodeGroups = new Map([...(root.querySelectorAll?.('.tree-nodes') ?? [])]
            .map(element => [element.dataset.backgroundId, element]));
        const edgeGroups = new Map([...(root.querySelectorAll?.('.tree-connectors') ?? [])]
            .map(element => [element.dataset.backgroundId, element]));
        trees = new Map(layout.map(tree => [tree.backgroundId, {
            ...tree, nodes: nodeGroups.get(tree.backgroundId), connectors: edgeGroups.get(tree.backgroundId)
        }]));
        treeLayoutRevision = revision;
        geometryRequestFailed = false;
        releaseGeometryBarrier();
    };

    const invalidateTreeGeometry = () => {
        cancelTreeDrag();
        // A Blazor render has already installed the committed positions. Remove
        // the transient group translation before paint; keep the lock until its
        // full logical snapshot arrives through updateTreeLayout.
        if (moveCommitPending) restoreTreePreview(committingTree);
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
            invalidateTreeGeometry();
            geometryPending = true;
            reportedSizes.set(key, size);
            pendingMeasurements.set(key, size);
        }
        if (pendingMeasurements.size === 0 || measurementFrame) return;
        measurementFrame = requestAnimationFrame(() => {
            measurementFrame = undefined;
            const measurements = [...pendingMeasurements.values()];
            pendingMeasurements.clear();
            measurementsInFlight++;
            // A size correction may change collision bounds. Do not start a new
            // background action until both its acknowledgement and snapshot agree.
            dotnet.invokeMethodAsync('NodeSizesChanged', measurements).then(revision => {
                if (disposed) return;
                if (Number.isFinite(revision)) geometryRequiredRevision = Math.max(geometryRequiredRevision, revision);
                geometryRequestFailed = false;
            }).catch(() => { if (!disposed) geometryRequestFailed = true; }).finally(() => {
                measurementsInFlight--;
                if (!disposed) releaseGeometryBarrier();
            });
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
        if (records.some(record => record.type === 'childList'
            || (record.type === 'attributes' && record.target?.matches?.('.node, .mindmap-content'))))
            invalidateTreeGeometry();
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
    mutations.observe(world, { childList: true, subtree: true, attributes: true, attributeFilter: ['style'] });

    // Track keyboard navigation outside the canvas too, so Tab from the toolbar
    // can reveal the focus ring. Dialog input is deliberately ignored.
    globalThis.document?.addEventListener('keydown', event => {
        if (insideEditor(event.target) && (toolbarToggleInProgress
            || (isToolbarToggle(event.target) && ['Enter', ' '].includes(event.key) && toolbarToggleBusy()))) {
            consumeAdditionEvent(event);
            return;
        }
        if (isToolbarToggle(event.target) && ['Enter', ' '].includes(event.key)) suppressedToolbarClick = false;
        if (event.key === 'Escape' && treeGesture) {
            consumeAdditionEvent(event);
            cancelTreeDrag();
            return;
        }
        if (treeInteractionBusy()) {
            if (insideEditor(event.target)) consumeAdditionEvent(event);
            return;
        }
        if (additionPending || additionDialogOpen || event.altKey || event.ctrlKey || event.metaKey
            || !['Tab', 'Enter', ' '].includes(event.key)) return;
        inputModality = 'keyboard';
        setAdditionHover(null);
        restoreKeyboardFocus();
    }, { capture: true, signal: events.signal });
    globalThis.document?.addEventListener('click', event => {
        if (isToolbarToggle(event.target) && ((suppressedToolbarClick && event.detail !== 0) || toolbarToggleBusy())) {
            suppressedToolbarClick = false;
            consumeAdditionEvent(event);
            return;
        }
        if ((toolbarToggleInProgress || treeInteractionBusy()) && insideEditor(event.target)) consumeAdditionEvent(event);
    }, { capture: true, signal: events.signal });
    globalThis.document?.addEventListener('pointerdown', event => {
        if (isToolbarToggle(event.target)) {
            suppressedToolbarClick = toolbarToggleBusy();
            if (suppressedToolbarClick) { consumeAdditionEvent(event); return; }
        }
        if (toolbarToggleInProgress && insideEditor(event.target)) { consumeAdditionEvent(event); return; }
        if (additionPending || additionDialogOpen) return;
        inputModality = 'pointer';
        setKeyboardFocus(null);
        setAdditionHover(null);
    }, { capture: true, signal: events.signal });
    root.addEventListener('focusin', restoreKeyboardFocus, { signal: events.signal });
    root.addEventListener('focusout', () => setKeyboardFocus(null), { signal: events.signal });

    const deactivatePresentation = () => {
        setAdditionHover(null);
        setKeyboardFocus(null);
        clearAdditionGesture();
        cancelTreeDrag();
        if (panGesture && root.hasPointerCapture(panGesture.pointerId)) root.releasePointerCapture(panGesture.pointerId);
        panGesture = undefined;
        blankClickCount = 0;
    };
    globalThis.window?.addEventListener('blur', () => {
        windowActive = false;
        deactivatePresentation();
    }, { signal: events.signal });
    globalThis.window?.addEventListener('focus', () => {
        windowActive = true;
        restoreKeyboardFocus();
    }, { signal: events.signal });
    globalThis.document?.addEventListener('visibilitychange', () => {
        if (!presentationActive()) deactivatePresentation();
        else restoreKeyboardFocus();
    }, { signal: events.signal });

    root.addEventListener('wheel', event => {
        event.preventDefault();
        if (toolbarToggleInProgress || treeInteractionBusy()) return;
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
        if (toolbarToggleInProgress || treeInteractionBusy()) {
            consumeAdditionEvent(event);
            return;
        }
        if (!additionGesture) suppressedPointerClick = undefined;
        const addition = additionAt(event);
        if (addition) {
            if (event.button !== 0 || event.isPrimary === false) return;
            // Capture priority also covers summary/text targets underneath the
            // overlay. Its native details activation must never run first.
            consumeAdditionEvent(event);
            blankClickCount = 0;
            suppressedPointerClick = { pointerId: event.pointerId };
            const { node, region } = addition;
            if (additionGesture || additionPending || additionDialogOpen || region.disabled) return;
            inputModality = 'pointer';
            setKeyboardFocus(null);
            region.focus({ preventScroll: true });
            additionGesture = {
                pointerId: event.pointerId, node, region, key: region.dataset.parentKey,
                startX: event.clientX, startY: event.clientY, canceled: false
            };
            root.setPointerCapture(event.pointerId);
            return;
        }
        if (additionGesture || additionPending || additionDialogOpen) return;
        if (event.button !== 0 || event.isPrimary === false) return;
        const node = nodeFromTarget(event.target);
        if (node) {
            blankClickCount = 0;
            if (!node.matches?.('.background-node') || isOperationControl(event.target)) return;
            if (geometryBusy()) { consumeAdditionEvent(event); return; }
            const id = node.closest('.tree-nodes')?.dataset.backgroundId;
            const tree = trees.get(id);
            if (!tree?.nodes || !tree.connectors) return;
            consumeAdditionEvent(event);
            setAdditionHover(null);
            setKeyboardFocus(null);
            treeGesture = {
                ...tree, revision: treeLayoutRevision, pointerId: event.pointerId,
                startX: event.clientX, startY: event.clientY, latestX: event.clientX, latestY: event.clientY,
                zoom, dx: 0, dy: 0, moved: false, valid: true, previewCleared: false,
                nodeTransform: tree.nodes.style.transform ?? '', edgeTransform: tree.connectors.style.transform ?? ''
            };
            root.setPointerCapture(event.pointerId);
            return;
        }
        if (!isBlankCanvas(event)) { blankClickCount = 0; return; }
        event.preventDefault();
        panGesture = {
            pointerId: event.pointerId, startX: event.clientX, startY: event.clientY,
            panX, panY, moved: false
        };
        root.setPointerCapture(event.pointerId);
    }, { capture: true, signal: events.signal });
    root.addEventListener('pointermove', event => {
        if (toolbarToggleInProgress) { consumeAdditionEvent(event); return; }
        if (treeGesture?.pointerId === event.pointerId) {
            consumeAdditionEvent(event);
            treeGesture.latestX = event.clientX;
            treeGesture.latestY = event.clientY;
            if (Math.hypot(event.clientX - treeGesture.startX, event.clientY - treeGesture.startY) > treeDragTolerance)
                treeGesture.moved = true;
            if (treeGesture.moved) scheduleTreePreview();
            return;
        }
        if (moveCommitPending) { consumeAdditionEvent(event); return; }
        if (additionGesture?.pointerId === event.pointerId) {
            setAdditionHover(inAdditionEllipse(additionGesture.node, event.clientX, event.clientY)
                ? additionGesture.region : null);
            consumeAdditionEvent(event);
            if (Math.hypot(event.clientX - additionGesture.startX, event.clientY - additionGesture.startY) > dragTolerance)
                additionGesture.canceled = true;
            return;
        }
        setAdditionHover(additionPending || additionDialogOpen ? null : additionAt(event)?.region ?? null);
        if (panGesture?.pointerId !== event.pointerId) return;
        if (!panGesture.moved && Math.hypot(event.clientX - panGesture.startX, event.clientY - panGesture.startY) <= treeDragTolerance) return;
        panGesture.moved = true;
        panX = panGesture.panX + event.clientX - panGesture.startX;
        panY = panGesture.panY + event.clientY - panGesture.startY;
        apply();
        scheduleSync();
    }, { capture: true, signal: events.signal });
    root.addEventListener('pointerleave', () => setAdditionHover(null), { signal: events.signal });
    root.addEventListener('pointerup', event => {
        if (toolbarToggleInProgress) { consumeAdditionEvent(event); return; }
        if (treeGesture?.pointerId === event.pointerId) {
            consumeAdditionEvent(event);
            const gesture = treeGesture;
            updateTreeCandidate(gesture, event.clientX, event.clientY);
            clearTreeFrame();
            treeGesture = undefined;
            releaseTreeCapture(gesture);
            if (gesture.moved) {
                suppressedPointerClick = { pointerId: gesture.pointerId };
                blankClickCount = 0;
            }
            if (!gesture.moved || (gesture.dx === 0 && gesture.dy === 0)) {
                restoreTreePreview(gesture);
            } else if (!gesture.valid || event.button !== 0) {
                restoreTreePreview(gesture);
                if (!gesture.valid) invoke('NotifyBackgroundMoveRejected', '他の背景ツリーと重なるため、移動を取り消しました。');
            } else {
                drawTreeCandidate(gesture);
                void commitTreeMove(gesture);
            }
            setAdditionHover(null);
            return;
        }
        if (moveCommitPending) { consumeAdditionEvent(event); return; }
        if (additionGesture?.pointerId === event.pointerId) {
            consumeAdditionEvent(event);
            const gesture = additionGesture;
            const inside = inAdditionEllipse(gesture.node, event.clientX, event.clientY);
            // Only a fresh pointermove may enable hover after an operation.
            setAdditionHover(null);
            clearAdditionGesture();
            if (event.button === 0 && !gesture.canceled
                && Math.hypot(event.clientX - gesture.startX, event.clientY - gesture.startY) <= dragTolerance
                && inside) void requestChildAddition(gesture.key, gesture.region, 'pointer');
            return;
        }
        if (panGesture?.pointerId === event.pointerId) {
            const gesture = panGesture;
            panGesture = undefined;
            if (root.hasPointerCapture(event.pointerId)) root.releasePointerCapture(event.pointerId);
            if (Math.hypot(event.clientX - gesture.startX, event.clientY - gesture.startY) > treeDragTolerance)
                gesture.moved = true;
            if (gesture.moved) {
                panX = gesture.panX + event.clientX - gesture.startX;
                panY = gesture.panY + event.clientY - gesture.startY;
                apply();
                scheduleSync();
                suppressedPointerClick = { pointerId: event.pointerId };
                blankClickCount = 0;
                consumeAdditionEvent(event);
            }
        }
    }, { capture: true, signal: events.signal });
    root.addEventListener('pointercancel', event => {
        setAdditionHover(null);
        if (treeGesture?.pointerId === event.pointerId) {
            consumeAdditionEvent(event);
            cancelTreeDrag();
        }
        if (additionGesture?.pointerId === event.pointerId) {
            consumeAdditionEvent(event);
            clearAdditionGesture();
        }
        if (panGesture?.pointerId === event.pointerId) {
            panGesture = undefined;
            blankClickCount = 0;
        }
    }, { capture: true, signal: events.signal });
    root.addEventListener('lostpointercapture', event => {
        setAdditionHover(null);
        if (treeGesture?.pointerId === event.pointerId) {
            consumeAdditionEvent(event);
            cancelTreeDrag();
        }
        if (additionGesture?.pointerId === event.pointerId) {
            consumeAdditionEvent(event);
            additionGesture = undefined;
        }
        if (panGesture?.pointerId === event.pointerId) {
            panGesture = undefined;
            blankClickCount = 0;
        }
    }, { capture: true, signal: events.signal });
    root.addEventListener('click', event => {
        if (toolbarToggleInProgress || treeInteractionBusy()) {
            consumeAdditionEvent(event);
            blankClickCount = 0;
            return;
        }
        if (event.detail === 0) {
            // Keyboard activation follows its focused control, not coordinates:
            // a focused summary must retain normal reason expansion behavior.
            const region = event.target.closest?.('.child-add-region');
            if (region) {
                consumeAdditionEvent(event);
                if (!region.disabled) void requestChildAddition(region.dataset.parentKey, region, 'keyboard');
            }
            return;
        }
        if (suppressedPointerClick && (event.pointerId === undefined
            || event.pointerId === suppressedPointerClick.pointerId)) {
            // Suppress the compatibility click even after dragging out of the
            // ellipse, cancellation, or lost capture. Never fall back to summary.
            suppressedPointerClick = undefined;
            blankClickCount = 0;
            consumeAdditionEvent(event);
            return;
        }
        // A pointer click without a valid ellipse down/up cannot add, but must
        // still not activate the reason underneath this higher-priority region.
        if (additionAt(event)) { consumeAdditionEvent(event); blankClickCount = 0; return; }
        blankClickCount = isBlankCanvas(event) && (event.detail === 1 || (event.detail === 2 && blankClickCount === 1))
            ? event.detail : 0;
    }, { capture: true, signal: events.signal });

    root.addEventListener('dblclick', event => {
        const validSequence = blankClickCount === 2;
        blankClickCount = 0;
        if (toolbarToggleInProgress) { consumeAdditionEvent(event); return; }
        if (event.button !== 0 || !validSequence || !isBlankCanvas(event) || additionGesture
            || additionPending || additionDialogOpen || treeInteractionBusy()) return;
        if (geometryBusy()) return;
        consumeAdditionEvent(event);
        const position = screenToCanvas(event.clientX, event.clientY);
        void requestBackgroundAddition(position.x, position.y);
    }, { capture: true, signal: events.signal });

    root.addEventListener('keydown', event => {
        if (toolbarToggleInProgress || treeInteractionBusy()) consumeAdditionEvent(event);
    }, { capture: true, signal: events.signal });

    // toggle does not bubble; capture survives Blazor updates to details elements.
    root.addEventListener('toggle', event => {
        if (!(event.target instanceof HTMLDetailsElement)) return;
        const node = event.target.closest('.node');
        if (node) invoke('ReasonChanged', node.id.substring(5), event.target.open);
    }, { capture: true, signal: events.signal });

    canvases.set(root, {
        updateTreeLayout: replaceTreeLayout,
        tryBeginToolbarToggle() {
            if (disposed || toolbarToggleBusy()) return false;
            toolbarToggleInProgress = true;
            blankClickCount = 0;
            suppressedPointerClick = undefined;
            suppressedToolbarClick = false;
            setAdditionHover(null);
            return true;
        },
        completeToolbarToggle(button, expectedVisible) {
            if (!toolbarToggleInProgress) return true;
            // Blazor can render once before its async click handler has finished.
            // Keep the lock until the actual controlled DOM matches the new state.
            if (button && typeof expectedVisible === 'boolean') {
                const toolbar = globalThis.document?.getElementById(button.getAttribute('aria-controls'));
                if (!toolbar || toolbar.hidden !== !expectedVisible
                    || button.getAttribute('aria-expanded') !== String(expectedVisible)) return false;
            }
            blankClickCount = 0;
            suppressedPointerClick = undefined;
            suppressedToolbarClick = false;
            setAdditionHover(null);
            // Flush only the viewport's layout. Hit tests keep reading fresh rects;
            // toolbar changes must not measure nodes or report document changes.
            root.getBoundingClientRect();
            toolbarToggleInProgress = false;
            button?.focus({ preventScroll: true });
            return true;
        },
        restoreReasonState(key, expanded) {
            const node = globalThis.document?.getElementById(`node-${key}`);
            if (!node || !root.contains(node)) return;
            const details = node.querySelector('details');
            if (details && details.open !== expanded) {
                invalidateTreeGeometry();
                setAdditionHover(null);
                details.open = expanded;
            }
        },
        setAdditionDialogOpen(open) {
            cancelTreeDrag();
            if (panGesture && root.hasPointerCapture(panGesture.pointerId)) root.releasePointerCapture(panGesture.pointerId);
            panGesture = undefined;
            blankClickCount = 0;
            additionDialogOpen = open;
            additionPending = false;
            setAdditionHover(null);
            setKeyboardFocus(null);
            clearAdditionGesture();
            // Closing also invalidates a late interop response from the prior dialog.
            if (!open) {
                additionRequestVersion++;
                finishAdditionPresentation();
            }
        },
        reset(defaultZoom) {
            if (toolbarToggleInProgress || treeInteractionBusy()) return false;
            clearTimeout(syncTimer);
            zoom = defaultZoom;
            panX = 0;
            panY = 0;
            apply();
            invoke('CanvasChanged', zoom, panX, panY);
            return true;
        },
        dispose() {
            disposed = true;
            toolbarToggleInProgress = false;
            additionRequestVersion++;
            moveRequestVersion++;
            cancelTreeDrag();
            finishTreeCommit();
            if (panGesture && root.hasPointerCapture(panGesture.pointerId)) root.releasePointerCapture(panGesture.pointerId);
            panGesture = undefined;
            clearAdditionGesture();
            suppressedPointerClick = undefined;
            setAdditionHover(null);
            setKeyboardFocus(null);
            additionOrigin = undefined;
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
    return canvases.get(root)?.reset(defaultZoom) ?? false;
}

export function detachCanvas(root) {
    canvases.get(root)?.dispose();
    canvases.delete(root);
}

export function setAdditionDialogOpen(root, open) {
    canvases.get(root)?.setAdditionDialogOpen(open);
}

export function updateTreeLayout(root, revision, trees) {
    canvases.get(root)?.updateTreeLayout(revision, trees);
}

export function restoreReasonState(root, key, expanded) {
    canvases.get(root)?.restoreReasonState(key, expanded);
}

export function tryBeginToolbarToggle(root) {
    return canvases.get(root)?.tryBeginToolbarToggle() ?? false;
}

export function completeToolbarToggle(root, button, expectedVisible) {
    return canvases.get(root)?.completeToolbarToggle(button, expectedVisible) ?? true;
}
