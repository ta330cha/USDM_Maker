export function attachCanvas(root, dotnet) {
    const world = root.querySelector('.mindmap-world');
    if (!world) return;

    let zoom = 1;
    let panX = 0;
    let panY = 0;
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
        const factor = event.deltaY < 0 ? 1.1 : 0.9;
        const nextZoom = Math.min(2, Math.max(.25, zoom * factor));
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
