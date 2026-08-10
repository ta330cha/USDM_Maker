export function attachCanvas(root, dotnet, initialZoom = 1, initialPanX = 0, initialPanY = 0, minZoom = .25, maxZoom = 2, zoomStep = .1) {
    const world = root.querySelector('.mindmap-world');
    if (!world) return;

    let zoom = initialZoom;
    let panX = initialPanX;
    let panY = initialPanY;
    let dragging = false;
    let lastX = 0;
    let lastY = 0;
    let syncTimer;

    const scheduleSync = () => {
        clearTimeout(syncTimer);
        syncTimer = setTimeout(() => dotnet.invokeMethodAsync('CanvasChanged', zoom, panX, panY).catch(() => {}), 100);
    };

    const apply = () => {
        world.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;
        const status = root.querySelector('.zoom-status');
        if (status) status.textContent = `${Math.round(zoom * 100)}%`;
    };

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
    }, { passive: false });

    root.addEventListener('pointerdown', event => {
        if (event.target.closest('.node')) return;
        dragging = true;
        lastX = event.clientX;
        lastY = event.clientY;
        root.setPointerCapture(event.pointerId);
    });
    root.addEventListener('pointermove', event => {
        if (!dragging) return;
        panX += event.clientX - lastX;
        panY += event.clientY - lastY;
        lastX = event.clientX;
        lastY = event.clientY;
        apply();
        scheduleSync();
    });
    root.addEventListener('pointerup', () => dragging = false);
    root.addEventListener('pointercancel', () => dragging = false);

    root.querySelectorAll('details').forEach(details => details.addEventListener('toggle', () => {
        const node = details.closest('.node');
        if (node) dotnet.invokeMethodAsync('ReasonChanged', node.id.substring(5), details.open).catch(() => {});
    }));

    dotnet.invokeMethodAsync('CanvasReady').catch(() => {});
    apply();
}

export function resetCanvas(root) {
    const world = root.querySelector('.mindmap-world');
    if (!world) return;
    world.style.transform = 'translate(0px, 0px) scale(1)';
    const status = root.querySelector('.zoom-status');
    if (status) status.textContent = '100%';
}

export function measureNodes(root) {
    return [...root.querySelectorAll('.node')].map(node => ({
        key: node.id.substring(5),
        width: node.offsetWidth,
        height: node.offsetHeight
    }));
}
