import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

// Source-contract checks only: these do not render Razor, test Safari Tab
// navigation, or claim that unconnected file commands perform an operation.
const home = await readFile(new URL('../../src/USDM_MindMap_Editor.Web/Components/Pages/Home.razor', import.meta.url), 'utf8');
const markup = home.split('@code')[0].replace(/<!--[^]*?-->/g, '').replace(/@\*[^]*?\*@/g, '');

function toolbarHeader() {
    const header = markup.match(/<header\b([^>]*class="editor-toolbar"[^>]*)>([^]*?)<\/header>/);
    assert.ok(header, 'Home must retain its controlled toolbar region');
    return { attributes: header[1], content: header[2] };
}

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

test('source contract: toolbar contains only its command area, while the browser PageTitle remains unchanged', () => {
    const { content } = toolbarHeader();
    assert.doesNotMatch(content, /USDM MindMap Editor|<strong\b/, 'Remove only the in-toolbar application title');
    assert.match(content.trim(), /^<div\b[^>]*class="toolbar-actions"[^>]*>[^]*<\/div>$/,
        'Do not leave a separate title container or an empty sibling row');
    assert.equal((content.match(/<div\b/g) ?? []).length, 1, 'The toolbar has one command area');
    assert.match(markup, /<PageTitle>USDM MindMap Editor<\/PageTitle>/, 'Preserve the browser tab title');
});

test('source contract: title removal preserves the toolbar ID and its toggle accessibility relationship', () => {
    const { attributes } = toolbarHeader();
    const toggle = markup.match(/<button\b([^>]*class="toolbar-toggle"[^>]*)>/);
    assert.ok(toggle, 'The existing independent menu toggle remains');
    const toolbarId = attributes.match(/\bid="([^"]+)"/)?.[1];
    const controlledId = toggle[1].match(/\baria-controls="([^"]+)"/)?.[1];
    assert.ok(toolbarId, 'The controlled menu retains its ID');
    assert.equal(controlledId, toolbarId, 'The toggle must control this same toolbar');
    assert.match(attributes, /\bhidden="@\(!_toolbarVisible\)"/);
    assert.match(toggle[1], /\baria-expanded="@_toolbarVisible\.ToString\(\)\.ToLowerInvariant\(\)"/);
    assert.match(toggle[1], /\btabindex="0"/);
    assert.match(toggle[1], /@onclick="ToggleToolbarAsync"/);
});
