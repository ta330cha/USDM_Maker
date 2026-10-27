// Run against the VisualTests host. PLAYWRIGHT_MODULE can point to a bundled package.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const baseUrl = process.env.CONNECTOR_TEST_URL || 'http://127.0.0.1:5189';
const artifactDir = path.resolve(__dirname, '../../artifacts/connection-lines');
const scenarios = ['single', 'multiple', 'upward', 'four-level', 'multiple-backgrounds', 'long-text', 'long-expanded'];

// Compare SVG endpoints with real HTML borders in screen coordinates, including
// the common zoom/pan transform. Also sample the curve against every actual node.
function inspectConnections() {
    const errors = [];
    const nodes = [...document.querySelectorAll('.node')];
    const svg = document.querySelector('.connectors');
    if (!svg || nodes.length === 0) return { errors: ['No rendered nodes'], nodes: 0 };
    const bounds = nodes.map(node => ({ key: node.id.slice(5), rect: node.getBoundingClientRect() }));
    const paths = [...svg.querySelectorAll('path')];
    const expectedZoom = parseFloat(document.querySelector('.zoom-status').textContent) / 100;
    if (Math.abs(svg.getScreenCTM().a - expectedZoom) > .001) errors.push('Zoom transform was lost during layout');
    const near = (actual, expected) => Math.abs(actual - expected) < .3;
    for (const curve of paths) {
        const parent = bounds.find(n => n.key === curve.dataset.parent).rect;
        const child = bounds.find(n => n.key === curve.dataset.child).rect;
        const matrix = curve.getScreenCTM();
        const length = curve.getTotalLength();
        const start = curve.getPointAtLength(0).matrixTransform(matrix);
        const end = curve.getPointAtLength(length).matrixTransform(matrix);
        if (!near(start.x, parent.x + parent.width / 2) || !near(start.y, parent.bottom)) errors.push('Parent anchor: ' + curve.dataset.parent);
        if (!near(end.x, child.left) || !near(end.y, child.y + child.height / 2)) errors.push('Child anchor: ' + curve.dataset.child);
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
        const report = { browser: browser.version(), viewport: '1700x1100', checks: results, errors };
        await fs.writeFile(path.join(artifactDir, 'results.json'), JSON.stringify(report, null, 2) + '\n');
        console.log(JSON.stringify({ browser: report.browser, checks: results.length, scenarios: scenarios.length, errors, artifacts: artifactDir }, null, 2));
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
