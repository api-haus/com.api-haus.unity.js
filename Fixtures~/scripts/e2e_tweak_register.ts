// E2E: standalone script that registers params at load time.

param('e2e.numEnum', [0, 1, 2], 'Test number enum')
param('e2e.strEnum', ['alpha', 'beta'], 'Test string enum')
param('e2e.range', { min: 0, max: 100, step: 1 }, 'Test range')
