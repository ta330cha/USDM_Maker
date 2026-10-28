import assert from 'node:assert/strict';
import test from 'node:test';
import { showNodeDialog, closeNodeDialog } from '../../src/USDM_MindMap_Editor.Web/wwwroot/js/nodeInputDialog.js';

function harness() {
    const document = { activeElement: null };
    globalThis.document = document;
    const control = (visible = true) => ({
        isConnected: true, focusCalls: [],
        focus(options) { this.focusCalls.push(options); document.activeElement = this; },
        getClientRects: () => visible ? [{}] : []
    });
    const opener = control();
    const input = control();
    const save = control();
    const cancel = control();
    const hidden = control(false);
    let controls = [input, hidden, save, cancel];
    const calls = [];
    let shown = 0;
    let closed = 0;
    const dialog = new EventTarget();
    Object.assign(dialog, {
        showModal: () => { shown++; }, close: () => { closed++; },
        querySelector: selector => selector === 'textarea' ? input : null,
        querySelectorAll: () => controls
    });
    opener.focus();
    return {
        document, opener, input, save, cancel, hidden, dialog, calls,
        shown: () => shown, closed: () => closed,
        setControls: value => { controls = value; },
        show: () => showNodeDialog(dialog, {
            invokeMethodAsync: method => { calls.push(method); return Promise.resolve(); }
        }),
        emit(type, properties = {}) {
            const event = new Event(type, { cancelable: true });
            Object.assign(event, properties);
            dialog.dispatchEvent(event);
            return event;
        },
        key(key, properties = {}) {
            return this.emit('keydown', { key, shiftKey: false, altKey: false, ctrlKey: false, metaKey: false, ...properties });
        }
    };
}

test('show focuses input once and close restores the actual opener without scrolling', () => {
    const h = harness();
    try {
        h.show();
        assert.equal(h.document.activeElement, h.input);
        assert.deepEqual(h.input.focusCalls, [{ preventScroll: true }]);
        h.show();
        assert.equal(h.shown(), 1, 'a rerender must not reopen the same dialog or replace its opener');
        h.key('a');
        h.key('Enter');
        closeNodeDialog(h.dialog);
        assert.equal(h.document.activeElement, h.opener);
        assert.deepEqual(h.opener.focusCalls.at(-1), { preventScroll: true });
        assert.equal(h.closed(), 1);
        closeNodeDialog(h.dialog);
        assert.equal(h.closed(), 1, 'repeated disposal must not restore focus a second time');
    } finally { closeNodeDialog(h.dialog); }
});

test('Escape cancellation is delegated once and its handler is removed when the dialog closes', () => {
    const h = harness();
    try {
        h.show();
        h.show();
        const escape = h.emit('cancel');
        assert.equal(escape.defaultPrevented, true, 'the owner must finish cancel and restore canvas state');
        assert.deepEqual(h.calls, ['CancelFromBrowser']);
        closeNodeDialog(h.dialog);
        const afterClose = h.emit('cancel');
        assert.equal(afterClose.defaultPrevented, false);
        assert.deepEqual(h.calls, ['CancelFromBrowser']);
        h.show();
        h.emit('cancel');
        assert.deepEqual(h.calls, ['CancelFromBrowser', 'CancelFromBrowser'], 'reopening must not accumulate handlers');
    } finally { closeNodeDialog(h.dialog); }
});

test('Tab and Shift+Tab trap visible controls while text entry and modified keys keep native behavior', () => {
    const h = harness();
    try {
        h.show();
        assert.equal(h.key('Tab', { shiftKey: true }).defaultPrevented, true);
        assert.equal(h.document.activeElement, h.cancel);
        assert.equal(h.key('Tab').defaultPrevented, true);
        assert.equal(h.document.activeElement, h.input);
        assert.equal(h.key('Tab').defaultPrevented, false, 'ordinary intermediate focus navigation remains native');
        h.save.focus();
        assert.equal(h.key('Tab', { shiftKey: true }).defaultPrevented, false);
        for (const key of ['a', 'Enter', ' ', 'Escape']) assert.equal(h.key(key).defaultPrevented, false);
        for (const modifier of ['altKey', 'ctrlKey', 'metaKey']) {
            h.cancel.focus();
            assert.equal(h.key('Tab', { [modifier]: true }).defaultPrevented, false);
            assert.equal(h.document.activeElement, h.cancel);
        }
        h.opener.focus();
        assert.equal(h.key('Tab').defaultPrevented, true);
        assert.equal(h.document.activeElement, h.input);
        h.opener.focus();
        assert.equal(h.key('Tab', { shiftKey: true }).defaultPrevented, true);
        assert.equal(h.document.activeElement, h.cancel);
        h.setControls([h.hidden]);
        assert.equal(h.key('Tab').defaultPrevented, true, 'an empty visible-control list must not escape the modal');
    } finally { closeNodeDialog(h.dialog); }
});

test('close does not focus a disconnected opener and leaves no active key handler', () => {
    const h = harness();
    h.show();
    h.opener.isConnected = false;
    closeNodeDialog(h.dialog);
    assert.equal(h.document.activeElement, h.input);
    assert.equal(h.opener.focusCalls.length, 1, 'a removed parent is not a valid focus target');
    h.cancel.focus();
    assert.equal(h.key('Tab').defaultPrevented, false);
    assert.equal(h.document.activeElement, h.cancel);
});
