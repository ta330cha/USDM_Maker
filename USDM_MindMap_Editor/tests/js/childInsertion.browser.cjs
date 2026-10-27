// Run against the VisualTests host. PLAYWRIGHT_MODULE can point to a bundled package.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const baseUrl = process.env.CONNECTOR_TEST_URL || 'http://127.0.0.1:5189';
const artifactDir = path.resolve(__dirname, '../../artifacts/elliptical-child-insertion');

function inspectLayoutAndRegions() {
    const errors = [];
    const nodes = [...document.querySelectorAll('.node')];
    const svg = document.querySelector('.connectors');
    const rectangles = new Map(nodes.map(node => [node.id.slice(5), node.getBoundingClientRect()]));
    const near = (a, b, tolerance = .35) => Math.abs(a - b) <= tolerance;
    const zoom = Number.parseFloat(document.querySelector('.zoom-status').textContent) / 100;
    const matrix = svg?.getScreenCTM();
    if (!matrix || !near(matrix.a, zoom, .001)) errors.push('Canvas zoom mismatch');
    const regionDetails = [];
    for (const node of nodes) {
        const key = node.id.slice(5);
        const rect = rectangles.get(key);
        const regions = node.querySelectorAll('button.child-add-region');
        if (key.startsWith('specification:')) {
            if (regions.length !== 0) errors.push('Specification must not have a child region');
            continue;
        }
        if (regions.length !== 1) { errors.push('Expected exactly one child region: ' + key); continue; }
        const region = regions[0];
        const hit = region.getBoundingClientRect();
        const name = key.startsWith('background:') ? '上位要求を追加'
            : key.split(':')[1].includes('-') ? '仕様を追加' : '下位要求を追加';
        if (region.dataset.parentKey !== key) errors.push('Wrong region parent: ' + key);
        if (region.getAttribute('aria-label') !== name || region.dataset.tooltip !== name) errors.push('Missing accessible operation name: ' + key);
        if (region.tabIndex < 0) errors.push('Child region is not keyboard focusable: ' + key);
        if (!near(hit.height, 10, .12)) errors.push('Child region must remain 10 CSS px: ' + key);
        if (!near(hit.left, rect.left + rect.width / 4) || !near(hit.right, rect.left + rect.width * .75)
            || !near(hit.top, rect.bottom - 5) || !near(hit.bottom, rect.bottom + 5)) errors.push('Child ellipse does not follow bottom midpoint: ' + key);
        if (!getComputedStyle(region).clipPath.startsWith('ellipse(')) errors.push('Region must clip its rectangular corners: ' + key);
        regionDetails.push({ key, width: hit.width, height: hit.height, top: hit.top, nodeBottom: rect.bottom });
    }
    const paths = [...svg.querySelectorAll('path')];
    for (const curve of paths) {
        const parent = rectangles.get(curve.dataset.parent);
        const child = rectangles.get(curve.dataset.child);
        if (!parent || !child) { errors.push('Orphan connection'); continue; }
        const transform = curve.getScreenCTM();
        const length = curve.getTotalLength();
        const start = curve.getPointAtLength(0).matrixTransform(transform);
        const end = curve.getPointAtLength(length).matrixTransform(transform);
        if (!near(start.x, parent.left + parent.width / 2) || !near(start.y, parent.bottom)) errors.push('Parent anchor mismatch');
        if (!near(end.x, child.left) || !near(end.y, child.top + child.height / 2)) errors.push('Child anchor mismatch');
        if (!near(child.left, parent.left + parent.width * .75) || child.top < parent.bottom + 96 * zoom - .35) errors.push('Downward indentation mismatch');
        const box = curve.getBBox();
        if (box.x < 0 || box.y < 0 || box.x + box.width + 1 > svg.width.baseVal.value
            || box.y + box.height + 1 > svg.height.baseVal.value) errors.push('SVG curve clipping');
        for (let sample = 1; sample < 200; sample++) {
            const point = curve.getPointAtLength(length * sample / 200).matrixTransform(transform);
            for (const rect of rectangles.values()) {
                if (point.x > rect.left + .1 && point.x < rect.right - .1
                    && point.y > rect.top + .1 && point.y < rect.bottom - .1) errors.push('Curve crosses a node');
            }
        }
    }
    const all = [...rectangles.values()];
    for (let i = 0; i < all.length; i++) for (let j = i + 1; j < all.length; j++) {
        if (Math.min(all[i].right, all[j].right) > Math.max(all[i].left, all[j].left) + .1
            && Math.min(all[i].bottom, all[j].bottom) > Math.max(all[i].top, all[j].top) + .1) errors.push('Nodes overlap');
    }
    return { errors: [...new Set(errors)], nodes: nodes.length, curves: paths.length, zoom, regions: regionDetails };
}

const nodeFor = (page, key) => page.locator('[id="node-' + key + '"]');
const regionFor = (page, key) => nodeFor(page, key).locator('button.child-add-region');
const dialogFor = page => page.locator('dialog.node-input-dialog[open]');

async function verify(page, label) {
    for (let attempt = 0; attempt < 50; attempt++) {
        const result = await page.evaluate(inspectLayoutAndRegions);
        if (result.errors.length === 0) {
            await page.waitForTimeout(120);
            const settled = await page.evaluate(inspectLayoutAndRegions);
            if (settled.errors.length === 0) return { label, ...settled };
        }
        await page.waitForTimeout(100);
    }
    assert.deepEqual((await page.evaluate(inspectLayoutAndRegions)).errors, [], label);
}

async function load(page, scenario) {
    await page.goto(baseUrl + '/scenarios/' + scenario);
    await page.locator('.child-add-region').first().waitFor();
    await page.waitForFunction(() => typeof Blazor !== 'undefined');
    await verify(page, scenario + ': ready');
}

async function noDialog(page, expectedCount, label) {
    await page.waitForTimeout(220);
    assert.equal(await dialogFor(page).count(), 0, label);
    assert.equal(await page.locator('.node').count(), expectedCount, label + ': node count');
}

async function cancel(page) {
    await dialogFor(page).getByRole('button', { name: 'キャンセル', exact: true }).click();
    await dialogFor(page).waitFor({ state: 'hidden' });
}

async function expectParentFocus(page, key, label) {
    await page.waitForFunction(parentKey => document.activeElement?.matches('button.child-add-region')
        && document.activeElement.dataset.parentKey === parentKey, key, { timeout: 5000 });
    assert.equal(await regionFor(page, key).evaluate(region => document.activeElement === region), true, label);
}

async function verifyModalTabLoop(page) {
    const dialog = dialogFor(page);
    await dialog.locator('textarea').first().focus();
    const controls = await dialog.locator('textarea:not([disabled]), input:not([disabled]), button:not([disabled])').count();
    for (const direction of ['Tab', 'Shift+Tab']) {
        // Cross the final/first boundary twice, not just the adjacent controls.
        for (let index = 0; index < controls * 2 + 1; index++) {
            await page.keyboard.press(direction);
            const focused = await dialog.evaluate(element => ({
                inside: element.contains(document.activeElement),
                tag: document.activeElement?.tagName,
                id: document.activeElement?.id
            }));
            assert.equal(focused.inside, true,
                direction + ' step ' + (index + 1) + ' must keep focus inside the modal: ' + JSON.stringify(focused));
        }
    }
}

async function panBy(page, dx, dy) {
    const start = await page.locator('.mindmap-canvas').evaluate(canvas => {
        const r = canvas.getBoundingClientRect();
        const candidates = [[r.right - 15, r.top + 15], [r.right - 15, r.bottom - 15], [r.left + 15, r.top + 15]];
        const point = candidates.find(([x, y]) => !document.elementFromPoint(x, y)?.closest('.node'));
        if (!point) throw new Error('No blank canvas point for a real drag');
        return { x: point[0], y: point[1] };
    });
    await page.mouse.move(start.x, start.y);
    await page.mouse.down();
    await page.mouse.move(start.x + dx, start.y + dy, { steps: 12 });
    await page.mouse.up();
}

async function reveal(page, locator) {
    const target = await locator.boundingBox();
    const canvas = await page.locator('.mindmap-canvas').boundingBox();
    const x = target.x + target.width / 2;
    const y = target.y + target.height / 2;
    if (x < canvas.x + 30 || x > canvas.x + canvas.width - 30 || y < canvas.y + 30 || y > canvas.y + canvas.height - 30)
        await panBy(page, canvas.x + canvas.width / 2 - x, canvas.y + canvas.height / 2 - y);
}

async function openChild(page, key, name, offset = 0) {
    await reveal(page, regionFor(page, key));
    const rect = await nodeFor(page, key).boundingBox();
    await page.mouse.click(rect.x + rect.width / 2, rect.y + rect.height + offset);
    await dialogFor(page).waitFor();
    assert.equal(await dialogFor(page).count(), 1);
    assert.equal(await dialogFor(page).getAttribute('aria-labelledby'), 'node-dialog-title');
    assert.equal((await page.locator('#node-dialog-title').textContent()).trim(), name);
}

async function ellipsePoint(page, key, nx, ny) {
    const rect = await nodeFor(page, key).boundingBox();
    return { x: rect.x + rect.width / 2 + rect.width / 4 * nx, y: rect.y + rect.height + 5 * ny };
}

async function watchReason(page, key) {
    await nodeFor(page, key).locator('details').evaluate(details => {
        window.reasonChanges = [];
        window.reasonObserver?.disconnect();
        window.reasonObserver = new MutationObserver(records => {
            for (const record of records) window.reasonChanges.push({ old: record.oldValue, open: details.open });
        });
        window.reasonObserver.observe(details, { attributes: true, attributeFilter: ['open'], attributeOldValue: true });
    });
}

async function assertReasonUnchanged(page, key, expectedOpen, label) {
    assert.equal(await nodeFor(page, key).locator('details').evaluate(details => details.open), expectedOpen, label);
    assert.deepEqual(await page.evaluate(() => window.reasonChanges), [], label + ': no temporary reason state change');
}

async function setZoom(page, percentage) {
    await page.getByRole('button', { name: 'ズームリセット', exact: true }).click();
    await page.waitForFunction(() => document.querySelector('.zoom-status').textContent === '100%');
    const canvas = await page.locator('.mindmap-canvas').boundingBox();
    await page.mouse.move(canvas.x + 8, canvas.y + 8);
    for (let i = 0; i < (percentage === 25 ? 8 : percentage === 200 ? 10 : 0); i++)
        await page.mouse.wheel(0, percentage < 100 ? 100 : -100);
    await page.waitForFunction(expected => document.querySelector('.zoom-status').textContent === expected + '%', percentage);
}

(async () => {
    await fs.mkdir(artifactDir, { recursive: true });
    const browser = await chromium.launch({ headless: true });
    const results = [];
    const errors = [];
    try {
        const page = await browser.newPage({ viewport: { width: 1700, height: 1100 } });
        page.on('pageerror', error => errors.push(error.message));
        page.on('response', response => { if (response.status() >= 400) errors.push(response.status() + ' ' + response.url()); });

        await load(page, 'four-level');
        results.push(await verify(page, 'initial: three parent types; no specification region'));
        const initialCount = await page.locator('.node').count();
        const background = 'background:001';

        // Repeated clicks can open only one modal and cannot create nodes before save.
        const backgroundBox = await nodeFor(page, background).boundingBox();
        await page.mouse.dblclick(backgroundBox.x + backgroundBox.width / 2, backgroundBox.y + backgroundBox.height, { delay: 25 });
        await dialogFor(page).waitFor();
        assert.equal(await dialogFor(page).count(), 1);
        assert.equal(await page.locator('.node').count(), initialCount);
        await cancel(page);
        await noDialog(page, initialCount, 'Repeated click and cancel');
        await expectParentFocus(page, background, 'Cancel must return focus to the originating parent');
        results.push({ label: 'Repeated click: one dialog; cancel: no mutation and parent focus restored' });

        await openChild(page, background, '上位要求を追加');
        await dialogFor(page).getByLabel(/^要求/).fill('   ');
        await dialogFor(page).getByRole('button', { name: '保存', exact: true }).click();
        await dialogFor(page).getByRole('alert').waitFor();
        assert.match(await dialogFor(page).getByRole('alert').textContent(), /要求を入力してください/);
        assert.equal(await page.locator('.node').count(), initialCount);
        await cancel(page);
        await noDialog(page, initialCount, 'Invalid input and cancel');
        await expectParentFocus(page, background, 'Cancel after invalid input must restore parent focus');
        results.push({ label: 'Whitespace requirement: error without mutation or ID consumption' });

        await openChild(page, background, '上位要求を追加');
        await verifyModalTabLoop(page);
        await page.keyboard.press('Escape');
        await dialogFor(page).waitFor({ state: 'hidden' });
        await noDialog(page, initialCount, 'Escape must cancel without mutation');
        await expectParentFocus(page, background, 'Escape must return focus to the originating parent');
        results.push({ label: 'Modal Tab/Shift+Tab stay contained; Escape cancels and restores parent focus' });

        const additions = [
            { parent: background, title: '上位要求を追加', field: '要求', text: '追加した上位要求', key: 'requirement:002' },
            { parent: 'requirement:001', title: '下位要求を追加', field: '要求', text: '追加した下位要求', key: 'requirement:001-002' },
            { parent: 'requirement:001-001', title: '仕様を追加', field: '仕様', text: '追加した仕様', key: 'specification:001-001-002' }
        ];
        for (const addition of additions) {
            await openChild(page, addition.parent, addition.title);
            if (addition.field === '仕様') {
                const beforeValidation = await page.locator('.node').count();
                await dialogFor(page).getByLabel(/^仕様/).fill(' ');
                await dialogFor(page).getByRole('button', { name: '保存', exact: true }).click();
                await dialogFor(page).getByRole('alert').waitFor();
                assert.match(await dialogFor(page).getByRole('alert').textContent(), /仕様を入力してください/);
                assert.equal(await page.locator('.node').count(), beforeValidation);
            }
            await dialogFor(page).getByLabel(new RegExp('^' + addition.field)).fill(addition.text);
            if (addition.field === '要求') await dialogFor(page).getByLabel(/^理由/).fill('');
            else {
                const related = dialogFor(page).getByRole('checkbox', { name: /001-002/ });
                assert.equal(await related.count(), 1, 'Other lower requirement is selectable');
                await related.check();
                const owner = dialogFor(page).getByRole('checkbox', { name: /001-001/ });
                if (await owner.count()) assert.equal(await owner.isEnabled(), false, 'Owning lower requirement must not be selectable');
            }
            await page.screenshot({ path: path.join(artifactDir, addition.field === '仕様' ? 'specification-dialog.png' : addition.key === 'requirement:002' ? 'requirement-dialog.png' : 'lower-requirement-dialog.png') });
            await dialogFor(page).getByRole('button', { name: '保存', exact: true }).click();
            await dialogFor(page).waitFor({ state: 'hidden' });
            await expectParentFocus(page, addition.parent, addition.title + ': save must restore parent focus');
            await nodeFor(page, addition.key).waitFor();
            assert.match(await nodeFor(page, addition.key).textContent(), new RegExp(addition.text));
            assert.equal(await page.locator('.connectors path[data-parent="' + addition.parent + '"][data-child="' + addition.key + '"]').count(), 1);
            results.push(await verify(page, addition.title + ': ID, parent, layout, anchors, restored focus'));
        }

        // Keyboard access shares the same modal, validation and ID allocation path.
        await reveal(page, regionFor(page, background));
        await regionFor(page, background).focus();
        await page.keyboard.press('Enter');
        await dialogFor(page).waitFor();
        await cancel(page);
        await expectParentFocus(page, background, 'Keyboard-opened dialog cancellation must restore parent focus');
        await regionFor(page, background).focus();
        await page.keyboard.press('Space');
        await dialogFor(page).waitFor();
        await dialogFor(page).getByLabel(/^要求/).fill('キーボードで追加');
        await dialogFor(page).getByRole('button', { name: '保存', exact: true }).click();
        await dialogFor(page).waitFor({ state: 'hidden' });
        await expectParentFocus(page, background, 'Keyboard-opened dialog save must restore parent focus');
        await nodeFor(page, 'requirement:003').waitFor();
        results.push(await verify(page, 'Enter and Space: shared addition flow'));

        await page.getByRole('button', { name: '背景追加', exact: true }).click();
        await dialogFor(page).waitFor();
        assert.equal((await page.locator('#node-dialog-title').textContent()).trim(), '背景を追加');
        await dialogFor(page).getByLabel(/^背景/).fill(' ');
        await dialogFor(page).getByRole('button', { name: '保存', exact: true }).click();
        await dialogFor(page).getByRole('alert').waitFor();
        await dialogFor(page).getByLabel(/^背景/).fill('ツールバーで追加した背景');
        await dialogFor(page).getByRole('button', { name: '保存', exact: true }).click();
        await dialogFor(page).waitFor({ state: 'hidden' });
        assert.equal(await page.getByRole('button', { name: '背景追加', exact: true }).evaluate(button => document.activeElement === button), true,
            'Background save must restore focus to the toolbar opener');
        await nodeFor(page, 'background:002').waitFor();
        results.push(await verify(page, 'Toolbar background: validation, ID, layout'));
        await setZoom(page, 25);
        await page.screenshot({ path: path.join(artifactDir, 'added-nodes.png') });

        // Non-addition gestures must neither open the dialog nor pan the canvas.
        await load(page, 'four-level');
        const count = await page.locator('.node').count();
        const bottom = await nodeFor(page, background).boundingBox();
        const bx = bottom.x + bottom.width / 2;
        const by = bottom.y + bottom.height;
        await page.mouse.click(bx, by, { button: 'right' });
        await page.keyboard.press('Escape');
        await noDialog(page, count, 'Right click');
        await nodeFor(page, background).locator('p').first().click();
        await noDialog(page, count, 'Node body click');
        await nodeFor(page, 'requirement:001').locator('summary').click();
        await noDialog(page, count, 'Reason summary click');
        await nodeFor(page, 'requirement:001').locator('details p').click();
        await noDialog(page, count, 'Reason body click');
        results.push(await verify(page, 'Body, reason, right click: no unintended addition'));

        const curvePoint = await page.locator('.connectors path').first().evaluate(curve => {
            const point = curve.getPointAtLength(curve.getTotalLength() / 2).matrixTransform(curve.getScreenCTM());
            return { x: point.x, y: point.y };
        });
        await page.mouse.click(curvePoint.x, curvePoint.y);
        await noDialog(page, count, 'Connection click');
        const transformBefore = await page.locator('.mindmap-world').getAttribute('style');
        await page.mouse.move(bx, by);
        await page.mouse.down();
        await page.mouse.move(bx + 40, by + 30, { steps: 8 });
        await page.mouse.up();
        await noDialog(page, count, 'Drag from addition region');
        assert.equal(await page.locator('.mindmap-world').getAttribute('style'), transformBefore, 'Region drag must not pan');
        await page.mouse.move(bx, by);
        await page.mouse.down();
        // Browsers do not expose a hardware pointer-cancel action through Playwright.
        // Dispatch only this cancellation; the surrounding down/up are real inputs.
        await regionFor(page, background).dispatchEvent('pointercancel', { pointerId: 1, pointerType: 'mouse', isPrimary: true, bubbles: true });
        await page.mouse.up();
        await noDialog(page, count, 'Canceled pointer');
        assert.equal(await page.locator('.mindmap-world').getAttribute('style'), transformBefore);
        results.push({ label: 'Connection click, region drag, pointercancel: no addition or unintended pan' });

        for (const zoom of [25, 100, 200]) {
            await load(page, 'single');
            await setZoom(page, zoom);
            results.push(await verify(page, zoom + '%: screen-space 10px ellipses, half parent width'));
            const currentCount = await page.locator('.node').count();
            // Exact boundary points are included; test diagonals as well as all four axes.
            for (const [nx, ny] of [[0, 0], [-1, 0], [1, 0], [0, -1], [0, 1], [.6, .8], [-.6, -.8]]) {
                await reveal(page, regionFor(page, background));
                const point = await ellipsePoint(page, background, nx, ny);
                await page.mouse.click(point.x, point.y);
                try { await dialogFor(page).waitFor({ timeout: 5000 }); }
                catch (cause) { throw new Error(`${zoom}% ellipse boundary (${nx}, ${ny}) at ${JSON.stringify(point)}`, { cause }); }
                await cancel(page);
            }
            for (const [nx, ny] of [[-1.01, 0], [1.01, 0], [0, -1.02], [0, 1.02], [-.99, -.99], [.99, -.99], [-.99, .99], [.99, .99]]) {
                const point = await ellipsePoint(page, background, nx, ny);
                await page.mouse.click(point.x, point.y);
                await noDialog(page, currentCount, zoom + '% outside ellipse ' + nx + ',' + ny);
            }
            const beforePan = await nodeFor(page, background).boundingBox();
            await panBy(page, 100, 60);
            const afterPan = await nodeFor(page, background).boundingBox();
            assert.ok(Math.abs(afterPan.x - beforePan.x - 100) < .1);
            assert.ok(Math.abs(afterPan.y - beforePan.y - 60) < .1);
            results.push(await verify(page, zoom + '%: pan, ellipse boundary and rectangular corner exclusion'));
            await openChild(page, background, '上位要求を追加', 4);
            await cancel(page);
            const r = await nodeFor(page, background).boundingBox();
            await page.mouse.click(r.x + r.width / 2, r.y + r.height + 5.1);
            await noDialog(page, currentCount, zoom + '% outside region after pan');
            await page.mouse.move(r.x + r.width / 2, r.y + r.height);
            await page.screenshot({ path: path.join(artifactDir, 'region-' + zoom + '.png') });
        }

        await load(page, 'single');
        const hoverPoint = await ellipsePoint(page, background, 0, .9);
        await page.mouse.move(hoverPoint.x, hoverPoint.y);
        await page.waitForFunction(() => document.querySelector('.child-add-region').classList.contains('is-hovered'));
        for (let index = 0; index < 3; index++) await page.mouse.wheel(0, -100);
        await page.waitForFunction(() => document.querySelector('.zoom-status').textContent === '130%');
        assert.equal(await regionFor(page, background).evaluate(region => region.classList.contains('is-hovered')), false,
            'Zoom must not leave a stale supplemental hover outside the fixed-height ellipse');
        await setZoom(page, 100);
        const dragPoint = await ellipsePoint(page, background, 0, 0);
        await page.mouse.move(dragPoint.x, dragPoint.y);
        await page.mouse.down();
        await page.mouse.move(dragPoint.x + 100, dragPoint.y, { steps: 6 });
        await page.mouse.up();
        await noDialog(page, 2, 'Hover after canceled drag outside ellipse');
        assert.equal(await regionFor(page, background).evaluate(region => region.classList.contains('is-hovered')), false,
            'Canceled drag must not leave stale hover at its starting point');
        results.push(await verify(page, 'Hover clears after zoom or dragging outside the ellipse'));

        // At 25%, the upper half of the screen-space ellipse overlaps the real
        // summary. Observe the open attribute, not just the eventual state.
        await load(page, 'four-level');
        await setZoom(page, 25);
        const reasonParent = 'requirement:001';
        const reasonNode = nodeFor(page, reasonParent);
        const reasonSummary = reasonNode.locator('summary');
        const reasonCount = await page.locator('.node').count();
        await watchReason(page, reasonParent);
        await openChild(page, reasonParent, '下位要求を追加', -4.5);
        await assertReasonUnchanged(page, reasonParent, false, 'Ellipse overlapping summary');
        await cancel(page);
        results.push(await verify(page, 'Overlap: child addition wins without changing the reason even transiently'));

        // Remove only the overlay's pointer hit target for this stress case, so
        // real input reaches summary text and still exercises root capture priority.
        await regionFor(page, reasonParent).evaluate(region => region.style.pointerEvents = 'none');
        const summaryPoint = await ellipsePoint(page, reasonParent, 0, -.9);
        assert.equal(await page.evaluate(({ x, y }) => !!document.elementFromPoint(x, y)?.closest('summary'), summaryPoint), true,
            'Stress input must actually target the reason summary');
        await page.mouse.click(summaryPoint.x, summaryPoint.y);
        await dialogFor(page).waitFor();
        await assertReasonUnchanged(page, reasonParent, false, 'Direct summary target inside ellipse');
        await cancel(page);
        const beforeReasonDrag = await page.locator('.mindmap-world').getAttribute('style');
        await page.mouse.move(summaryPoint.x, summaryPoint.y);
        await page.mouse.down();
        await page.mouse.move(summaryPoint.x + 8, summaryPoint.y, { steps: 3 });
        await page.mouse.move(summaryPoint.x, summaryPoint.y, { steps: 3 });
        await page.mouse.up();
        await noDialog(page, reasonCount, 'Summary ellipse drag returns inside');
        await assertReasonUnchanged(page, reasonParent, false, 'Dragged summary must not toggle reason');
        await page.mouse.down();
        await reasonSummary.dispatchEvent('pointercancel', { pointerId: 1, pointerType: 'mouse', isPrimary: true, bubbles: true });
        await page.mouse.up();
        await noDialog(page, reasonCount, 'Canceled summary gesture');
        await assertReasonUnchanged(page, reasonParent, false, 'Canceled summary must not toggle reason');
        assert.equal(await page.locator('.mindmap-world').getAttribute('style'), beforeReasonDrag, 'No fallback to panning');
        await regionFor(page, reasonParent).evaluate(region => region.style.removeProperty('pointer-events'));
        results.push({ label: 'Direct summary target, drag-return, cancellation: no reason state mutation or pan' });

        // The top rectangular corner lies over summary but outside the ellipse:
        // it must reach the summary normally, not become a rectangular shield.
        const corner = await ellipsePoint(page, reasonParent, .99, -.99);
        assert.equal(await page.evaluate(({ x, y }) => !!document.elementFromPoint(x, y)?.closest('summary'), corner), true);
        await page.mouse.click(corner.x, corner.y);
        await page.waitForFunction(() => document.querySelector('[id="node-requirement:001"] details').open);
        await noDialog(page, reasonCount, 'Outside ellipse corner toggles reason');
        await verify(page, 'Reason corner changed layout');
        await reasonSummary.focus();
        await page.keyboard.press('Enter');
        await page.waitForFunction(() => !document.querySelector('[id="node-requirement:001"] details').open);
        await page.keyboard.press('Space');
        await page.waitForFunction(() => document.querySelector('[id="node-requirement:001"] details').open);
        await noDialog(page, reasonCount, 'Reason keyboard remains independent');
        await verify(page, 'Expanded reason settled');
        await watchReason(page, reasonParent);
        await openChild(page, reasonParent, '下位要求を追加', -4);
        await assertReasonUnchanged(page, reasonParent, true, 'Expanded reason stays expanded');
        await cancel(page);
        results.push(await verify(page, 'Outside corner and reason keyboard preserved; expanded reason unchanged by addition'));
        await page.evaluate(() => window.reasonObserver.disconnect());

        await load(page, 'long-text');
        await setZoom(page, 25);
        const longParent = 'requirement:001';
        const longNode = nodeFor(page, longParent);
        const initialHeight = (await longNode.boundingBox()).height;
        for (const expanded of [true, false]) {
            await reveal(page, longNode.locator('summary'));
            await longNode.locator('summary').click();
            await page.waitForFunction(expected => document.querySelector('[id="node-requirement:001"] details').open === expected, expanded);
            results.push(await verify(page, 'Long reason ' + (expanded ? 'expanded' : 'collapsed') + ': region and anchors follow'));
            const height = (await longNode.boundingBox()).height;
            assert.ok(expanded ? height > initialHeight + 25 : Math.abs(height - initialHeight) < .1);
            await openChild(page, longParent, '下位要求を追加');
            await cancel(page);
            await reveal(page, regionFor(page, longParent));
            await page.screenshot({ path: path.join(artifactDir, expanded ? 'long-reason-expanded.png' : 'long-reason-collapsed.png') });
        }
        await openChild(page, longParent, '下位要求を追加');
        await dialogFor(page).getByLabel(/^要求/).fill('長文の親へ追加した下位要求');
        await dialogFor(page).getByRole('button', { name: '保存', exact: true }).click();
        await nodeFor(page, 'requirement:001-002').waitFor();
        results.push(await verify(page, 'Long parent: insertion after reason toggles preserves layout'));

        assert.deepEqual(errors, [], 'Browser errors');
        const report = { feature: 'elliptical-child-insertion', browser: browser.version(), viewport: '1700x1100', checks: results, errors };
        await fs.writeFile(path.join(artifactDir, 'results.json'), JSON.stringify(report, null, 2) + '\n');
        console.log(JSON.stringify({ browser: report.browser, checks: results.length, errors, artifacts: artifactDir }, null, 2));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
