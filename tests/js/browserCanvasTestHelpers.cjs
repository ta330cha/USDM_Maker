const assert = require('node:assert/strict');

// Test-only setup / reset verification. This is not a user-facing command and
// does not test a toolbar button or imply a replacement reset shortcut exists.
async function resetCanvasForTest(page) {
    const accepted = await page.evaluate(async () => {
        const root = document.querySelector('.mindmap-canvas');
        if (!root) throw new Error('No canvas available for test-only reset');
        // Match Home's module URL so this uses its attached canvas controller.
        const canvasModule = await import(new URL('js/mindmapCanvas.js', document.baseURI).href);
        return canvasModule.resetCanvas(root, 1);
    });
    assert.equal(accepted, true, 'Test-only reset requires an attached, idle canvas');
    await page.waitForFunction(() => {
        const world = document.querySelector('.mindmap-world');
        const status = document.querySelector('.zoom-status');
        if (!world || status?.textContent !== '100%') return false;
        const matrix = new DOMMatrixReadOnly(getComputedStyle(world).transform);
        return matrix.a === 1 && matrix.d === 1 && matrix.e === 0 && matrix.f === 0;
    });
}

module.exports = { resetCanvasForTest };
