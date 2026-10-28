import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

// Source-contract checks only: these do not render Razor, test Safari Tab
// navigation, or claim that unconnected file commands perform an operation.
const home = await readFile(new URL('../../src/USDM_MindMap_Editor.Web/Components/Pages/Home.razor', import.meta.url), 'utf8');
const markup = home.split('@code')[0].replace(/<!--[^]*?-->/g, '').replace(/@\*[^]*?\*@/g, '');

function toolbarButtons() {
    const actions = markup.match(/<div\b[^>]*class="toolbar-actions"[^>]*>([^]*?)<\/div>/);
    assert.ok(actions, 'Home must retain the toolbar action container');
    return [...actions[1].matchAll(/<button\b([^>]*?)>([^]*?)<\/button>/g)]
        .map(([, attributes, content]) => ({ attributes, label: content.replace(/<[^>]*>/g, '').trim() }));
}

test('source contract: toolbar has exactly five commands in the designed order, each in natural Tab order', () => {
    const buttons = toolbarButtons();
    assert.deepEqual(buttons.map(button => button.label), ['新規', '開く', '保存', 'エクスポート', '背景追加']);
    for (const { attributes, label } of buttons) {
        assert.match(attributes, /\btype="button"/, label);
        assert.match(attributes, /\btabindex="0"/, label);
    }
    assert.match(buttons[4].attributes, /@onclick="OpenBackgroundAsync"/, 'Existing background addition remains connected');
});

test('source contract: search and reset buttons are absent and toolbar visibility toggle remains separate', () => {
    const labels = [...markup.matchAll(/<button\b[^>]*>([^]*?)<\/button>/g)]
        .map(([, content]) => content.replace(/<[^>]*>/g, '').trim());
    assert.ok(!labels.includes('検索'));
    assert.ok(!labels.includes('ズームリセット'));
    assert.match(markup, /<button\b[^>]*class="toolbar-toggle"[^>]*@onclick="ToggleToolbarAsync"/);
    assert.ok(toolbarButtons().every(button => !/toolbar-toggle/.test(button.attributes)));
    assert.doesNotMatch(markup, /@onclick="ResetZoom"/, 'No replacement reset button is introduced');
});

test('source contract: Home retains its existing ResetZoom method and delegates to the canvas reset API', () => {
    const method = home.match(/^    private async Task ResetZoom\(\)\s*\{([^]*?)^    \}/m);
    assert.ok(method, 'Reset implementation remains available for a future UI entry point');
    assert.match(method[1], /InvokeVoidAsync\("resetCanvas",\s*_canvasElement,\s*_settings\.InitialZoom\)/);
});
