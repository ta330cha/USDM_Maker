// Run against the VisualTests host. PLAYWRIGHT_MODULE can point to a bundled package.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const baseUrl = process.env.CONNECTOR_TEST_URL || 'http://127.0.0.1:5189';
const artifactDir = path.resolve(__dirname, '../../artifacts/indented-layout');
const scenarios = ['single', 'multiple', 'deep-first-subtree', 'four-level', 'multiple-backgrounds', 'long-text', 'long-expanded'];

// Compare SVG endpoints with real HTML borders in screen coordinates, including
// the common zoom/pan transform. Derive the layout tree from actual rendered paths
// and verify downward indentation, subtree spacing, and curve/node separation.
function inspectConnections() {
    const errors = [];
    const nodes = [...document.querySelectorAll('.node')];
    const svg = document.querySelector('.connectors');
    if (!svg || nodes.length === 0) return { errors: ['No rendered nodes'], nodes: 0 };
    const bounds = nodes.map(node => ({ key: node.id.slice(5), rect: node.getBoundingClientRect() }));
    const paths = [...svg.querySelectorAll('path')];
    const worldMatrix = svg.getScreenCTM();
    const expectedZoom = parseFloat(document.querySelector('.zoom-status').textContent) / 100;
    if (Math.abs(worldMatrix.a - expectedZoom) > .001) errors.push('Zoom transform was lost during layout');
    const near = (actual, expected) => Math.abs(actual - expected) < .3;
    const localNear = (actual, expected) => Math.abs(actual - expected) < .1;
    const layoutBounds = new Map(bounds.map(({ key, rect }) => [key, {
        left: (rect.left - worldMatrix.e) / worldMatrix.a,
        top: (rect.top - worldMatrix.f) / worldMatrix.d,
        right: (rect.right - worldMatrix.e) / worldMatrix.a,
        bottom: (rect.bottom - worldMatrix.f) / worldMatrix.d,
        width: rect.width / worldMatrix.a
    }]));
    const children = new Map();
    const hasParent = new Set();
    for (const curve of paths) {
        const parentKey = curve.dataset.parent;
        const childKey = curve.dataset.child;
        if (!layoutBounds.has(parentKey) || !layoutBounds.has(childKey)) {
            errors.push('Connection references a missing node');
            continue;
        }
        if (hasParent.has(childKey)) errors.push('Node has multiple parent connections: ' + childKey);
        hasParent.add(childKey);
        if (!children.has(parentKey)) children.set(parentKey, []);
        children.get(parentKey).push(childKey);
    }
    const compareIds = (left, right) => {
        const a = left.split(':')[1].split('-').map(Number);
        const b = right.split(':')[1].split('-').map(Number);
        for (let i = 0; i < Math.min(a.length, b.length); i++) if (a[i] !== b[i]) return a[i] - b[i];
        return a.length - b.length;
    };
    const subtreeKeys = key => {
        const keys = new Set([key]);
        for (const child of children.get(key) || []) for (const descendant of subtreeKeys(child)) keys.add(descendant);
        return keys;
    };
    const subtreeBottom = key => {
        const keys = subtreeKeys(key);
        let bottom = Math.max(...[...keys].map(k => layoutBounds.get(k).bottom));
        // Include the incoming curve as well as all internal descendant curves.
        for (const curve of paths) {
            if (!keys.has(curve.dataset.child)) continue;
            const box = curve.getBBox();
            bottom = Math.max(bottom, box.y + box.height);
        }
        return bottom;
    };
    for (const [parentKey, childKeys] of children) {
        const parent = layoutBounds.get(parentKey);
        childKeys.sort(compareIds);
        for (const [index, childKey] of childKeys.entries()) {
            const child = layoutBounds.get(childKey);
            if (!localNear(child.left, parent.left + parent.width * .75)) errors.push('Child indentation: ' + childKey);
            if (index === 0 && child.top < parent.bottom + 96 - .1) errors.push('First child vertical gap: ' + childKey);
            if (index > 0 && child.top < subtreeBottom(childKeys[index - 1]) + 32 - .1) errors.push('Sibling overlaps preceding subtree: ' + childKey);
        }
    }
    const roots = bounds.map(n => n.key).filter(key => !hasParent.has(key)).sort(compareIds);
    for (const [index, key] of roots.entries()) {
        const root = layoutBounds.get(key);
        if (!localNear(root.left, 96)) errors.push('Background left alignment: ' + key);
        if (index === 0 && !localNear(root.top, 96)) errors.push('First background top margin');
        if (index > 0 && root.top < subtreeBottom(roots[index - 1]) + 32 - .1) errors.push('Background overlaps preceding subtree: ' + key);
    }
    const rectangles = [...layoutBounds.entries()];
    for (let i = 0; i < rectangles.length; i++) {
        for (let j = i + 1; j < rectangles.length; j++) {
            const [aKey, a] = rectangles[i];
            const [bKey, b] = rectangles[j];
            if (Math.min(a.right, b.right) > Math.max(a.left, b.left) + .1
                && Math.min(a.bottom, b.bottom) > Math.max(a.top, b.top) + .1) errors.push('Nodes overlap: ' + aKey + ', ' + bKey);
        }
    }
    for (const curve of paths) {
        const parent = bounds.find(n => n.key === curve.dataset.parent)?.rect;
        const child = bounds.find(n => n.key === curve.dataset.child)?.rect;
        if (!parent || !child) continue;
        const matrix = curve.getScreenCTM();
        const length = curve.getTotalLength();
        const start = curve.getPointAtLength(0).matrixTransform(matrix);
        const end = curve.getPointAtLength(length).matrixTransform(matrix);
        if (!near(start.x, parent.x + parent.width / 2) || !near(start.y, parent.bottom)) errors.push('Parent anchor: ' + curve.dataset.parent);
        if (!near(end.x, child.left) || !near(end.y, child.y + child.height / 2)) errors.push('Child anchor: ' + curve.dataset.child);
        const coordinates = curve.getAttribute('d').match(/[-+]?(?:\d*\.)?\d+(?:e[-+]?\d+)?/gi)?.map(Number);
        if (!coordinates || coordinates.length !== 8) errors.push('Expected a single cubic curve');
        else {
            const [sx, sy, c1x, c1y, c2x, c2y, ex, ey] = coordinates;
            if (!localNear(c1x, sx) || c1y <= sy || c1y > ey + .1) errors.push('Downward outgoing control point');
            if (!localNear(c2y, ey) || c2x >= ex || c2x < sx - .1) errors.push('Rightward incoming control point');
            if (ex <= sx || ey <= sy) errors.push('Connection must progress down and right');
        }
        const box = curve.getBBox();
        if (box.x < -0.1 || box.y < -0.1 || box.x + box.width + 1 > svg.width.baseVal.value
            || box.y + box.height + 1 > svg.height.baseVal.value) errors.push('Curve clipped by SVG bounds');
        if (getComputedStyle(curve).fill !== 'none') errors.push('Missing curve styles');
        for (let index = 1; index < 600; index++) {
            const point = curve.getPointAtLength(length * index / 600).matrixTransform(matrix);
            for (const node of bounds) {
                const r = node.rect;
                if (point.x > r.left + .1 && point.x < r.right - .1 && point.y > r.top + .1 && point.y < r.bottom - .1) {
                    errors.push('Curve crosses ' + node.key);
                    break;
                }
            }
        }
    }
    return { errors: [...new Set(errors)], nodes: nodes.length, curves: paths.length };
}

async function verify(page, label) {
    // Wait for the first measurement round / Blazor diff to settle. Recheck twice
    // so a transient predicted-layout frame cannot be mistaken for a pass.
    await page.waitForFunction(() => typeof Blazor !== 'undefined');
    for (let attempt = 0; attempt < 40; attempt++) {
        const result = await page.evaluate(inspectConnections);
        if (result.errors.length === 0) {
            await page.waitForTimeout(150);
            const settled = await page.evaluate(inspectConnections);
            if (settled.errors.length === 0) return { label, ...settled };
        }
        await page.waitForTimeout(100);
    }
    assert.deepEqual((await page.evaluate(inspectConnections)).errors, [], label);
}

(async () => {
    await fs.mkdir(artifactDir, { recursive: true });
    const browser = await chromium.launch({ headless: true });
    const results = [];
    try {
        const page = await browser.newPage({ viewport: { width: 1700, height: 1100 } });
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('response', response => { if (response.status() >= 400) errors.push(response.status() + ' ' + response.url()); });
        for (const scenario of scenarios) {
            await page.goto(baseUrl + '/scenarios/' + scenario);
            await page.locator('.node').first().waitFor();
            results.push(await verify(page, scenario + ': initial'));

            // Real wheel events exercise both 25% and 200%, without changing the SVG paths.
            const originalPaths = await page.locator('.connectors path').evaluateAll(paths => paths.map(p => p.getAttribute('d')));
            const canvas = await page.locator('.mindmap-canvas').boundingBox();
            await page.mouse.move(canvas.x + 8, canvas.y + 8);
            for (let index = 0; index < 12; index++) await page.mouse.wheel(0, -100);
            await page.waitForFunction(() => document.querySelector('.zoom-status').textContent === '200%');
            results.push(await verify(page, scenario + ': zoom 200%'));
            for (let index = 0; index < 20; index++) await page.mouse.wheel(0, 100);
            await page.waitForFunction(() => document.querySelector('.zoom-status').textContent === '25%');
            results.push(await verify(page, scenario + ': zoom 25%'));
            assert.deepEqual(await page.locator('.connectors path').evaluateAll(paths => paths.map(p => p.getAttribute('d'))), originalPaths, 'Zoom must not recalculate paths');
            await page.screenshot({ path: path.join(artifactDir, scenario + '.png') });

            // Move the bottom-most curve into the viewport with an actual canvas drag.
            const lastCurve = await page.locator('.connectors path').evaluateAll(paths => paths.map(p => {
                const b = p.getBBox(); const m = p.getScreenCTM();
                return { x: (b.x + b.width / 2) * m.a + m.e, y: (b.y + b.height) * m.d + m.f };
            }).sort((a, b) => b.y - a.y)[0]);
            const dragStart = { x: canvas.x + canvas.width - 20, y: canvas.y + 20 };
            await page.mouse.move(dragStart.x, dragStart.y);
            await page.mouse.down();
            await page.mouse.move(dragStart.x + canvas.x + canvas.width / 2 - lastCurve.x,
                dragStart.y + canvas.y + canvas.height / 2 - lastCurve.y, { steps: 12 });
            await page.mouse.up();
            results.push(await verify(page, scenario + ': pan'));
            assert.deepEqual(await page.locator('.connectors path').evaluateAll(paths => paths.map(p => p.getAttribute('d'))), originalPaths, 'Pan must not recalculate paths');
            if (scenario === 'multiple-backgrounds') await page.screenshot({ path: path.join(artifactDir, 'multiple-backgrounds-bottom.png') });

            await page.getByRole('button', { name: 'ズームリセット' }).click();
            await page.waitForFunction(() => document.querySelector('.zoom-status').textContent === '100%');
            results.push(await verify(page, scenario + ': reset'));
            if (scenario === 'single' || scenario === 'four-level') await page.screenshot({ path: path.join(artifactDir, scenario + '-100.png') });
        }

        // Repeated reason toggles must shrink again, including incoming/outgoing anchors.
        await page.goto(baseUrl + '/scenarios/long-text');
        results.push(await verify(page, 'long reason: initial'));
        const requirement = page.locator('[id="node-requirement:001"]');
        const originalHeight = await requirement.evaluate(n => n.getBoundingClientRect().height);
        for (let i = 0; i < 3; i++) {
            // Zoom out so the long node's summary is reachable without artificial DOM changes.
            await page.mouse.move(8, 80);
            for (let step = 0; step < 9; step++) await page.mouse.wheel(0, 100);
            await page.waitForFunction(() => document.querySelector('.zoom-status').textContent === '25%');
            await requirement.locator('summary').click();
            await page.waitForFunction(() => document.querySelector('[id="node-requirement:001"] details').open);
            results.push(await verify(page, 'long reason: expanded ' + i));
            const expandedHeight = await requirement.evaluate(n => n.getBoundingClientRect().height * 4);
            assert.ok(expandedHeight > originalHeight + 100);
            if (i === 0) await page.screenshot({ path: path.join(artifactDir, 'reason-expanded.png') });
            await requirement.locator('summary').click();
            await page.waitForFunction(() => !document.querySelector('[id="node-requirement:001"] details').open);
            results.push(await verify(page, 'long reason: collapsed ' + i));
            assert.ok(Math.abs(await requirement.evaluate(n => n.getBoundingClientRect().height * 4) - originalHeight) < .1);
        }
        assert.deepEqual(errors, [], 'Browser errors');
        const report = { layout: 'downward-indented', browser: browser.version(), viewport: '1700x1100', checks: results, errors };
        await fs.writeFile(path.join(artifactDir, 'results.json'), JSON.stringify(report, null, 2) + '\n');
        console.log(JSON.stringify({ browser: report.browser, checks: results.length, scenarios: scenarios.length, errors, artifacts: artifactDir }, null, 2));
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
