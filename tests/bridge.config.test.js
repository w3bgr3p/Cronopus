const test = require('node:test');
const assert = require('node:assert/strict');
const { createConfigController } = require('../wwwroot/js/bridge-config.js');
test('empty key during connection edit preserves the saved secret', async () => {
    const calls = [];
    const controller = createConfigController({ send: async (...a) => { calls.push(a); return { hasKey: true }; } });
    await controller.save({ id: 'srv', name: 'Server', key: '' });
    assert.equal(Object.hasOwn(calls[0][2], 'secret'), false);
});
