const test = require('node:test');
const assert = require('node:assert/strict');

global.window = { innerWidth: 1024 };
global.document = {
    addEventListener() {},
};

const { UpdateManager } = require('./BotControlPanel.js');

test('an up-to-date release disables the update action', async () => {
    const elements = new Map([
        ['current-version', { textContent: '' }],
        ['new-version', { textContent: '' }],
        ['changelog-content', { textContent: '', innerHTML: '' }],
        ['confirm-update', { textContent: 'Update Now', disabled: false }],
        ['update-availability-message', { textContent: '' }],
    ]);

    global.document.getElementById = id => elements.get(id) ?? null;
    global.fetch = async () => ({
        ok: true,
        async json() {
            return {
                version: 'v1.3.8',
                changelog: 'No changes',
                available: false,
            };
        },
    });

    const manager = new UpdateManager({
        api: {
            endpoints: {
                instances: '/instances',
                updateCheck: '/update/check',
            },
            async get() {
                return { instances: [{ version: 'v1.3.8' }] };
            },
        },
    });

    await manager.showUpdateModal();

    assert.equal(manager.updateAvailable, false);
    assert.equal(elements.get('confirm-update').disabled, true);
    assert.equal(elements.get('confirm-update').textContent, 'Up to Date');
    assert.equal(
        elements.get('update-availability-message').textContent,
        'This installation is already running the latest release.',
    );
});

test('an up-to-date release cannot start a forced update', async () => {
    let postCalls = 0;
    const infoMessages = [];
    const updateState = {};

    global.document.getElementById = () => null;

    const manager = new UpdateManager({
        api: {
            endpoints: { updateAll: '/update/all' },
            async post() {
                postCalls++;
                return { ok: true, sessionId: 'unexpected' };
            },
        },
        state: {
            get() {
                return updateState;
            },
            set() {},
        },
        toastManager: {
            info(message) {
                infoMessages.push(message);
            },
            error() {},
        },
    });

    manager.updateAvailable = false;
    manager.startStatusCheck = () => {};
    await manager.confirmUpdate();

    assert.equal(postCalls, 0);
    assert.deepEqual(infoMessages, ['PokeBuilder is already up to date.']);
});

test('a newer release starts a normal non-forced update', async () => {
    const postedBodies = [];
    const updateState = {};

    global.document.getElementById = () => null;

    const manager = new UpdateManager({
        api: {
            endpoints: { updateAll: '/update/all' },
            async post(_url, body) {
                postedBodies.push(body);
                return { ok: true, sessionId: 'session-1' };
            },
        },
        state: {
            get() {
                return updateState;
            },
            set() {},
        },
        toastManager: {
            info() {},
            error() {},
        },
    });

    manager.updateAvailable = true;
    manager.startStatusCheck = () => {};
    await manager.confirmUpdate();

    assert.deepEqual(postedBodies, [{ force: false }]);
});
