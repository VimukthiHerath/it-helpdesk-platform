const { defineConfig, devices } = require('@playwright/test');

// The same tests can target Azure or a local stack without changing the code.
const baseURL = process.env.UI_BASE_URL;
if (!baseURL) {
  throw new Error('Set UI_BASE_URL to the frontend address before running the tests.');
}
const target = new URL(baseURL);
if (!['http:', 'https:'].includes(target.protocol) || target.username || target.password) {
  throw new Error('UI_BASE_URL must be an HTTP(S) address without embedded credentials.');
}
const browserChannel = process.env.UI_BROWSER_CHANNEL;

module.exports = defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: Boolean(process.env.CI),
  timeout: 60_000,
  globalTimeout: 480_000,
  expect: { timeout: 15_000 },
  outputDir: 'reports/artifacts',
  reporter: [
    ['./reporters/RedactSecretsReporter.js'],
    ['list'],
    ['html', { outputFolder: 'reports/html', open: 'never' }],
    ['junit', { outputFile: 'reports/junit.xml' }],
    ['json', { outputFile: 'reports/results.json' }],
    ['./reporters/ArchiveReportsReporter.js'],
  ],
  metadata: {
    targetOrigin: target.origin,
    browserChannel: browserChannel || 'bundled Chromium',
    azureDeployedCommit: 'not independently verified',
  },
  use: {
    baseURL: target.origin,
    navigationTimeout: 60_000,
    actionTimeout: 15_000,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: browserChannel || 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        ...(browserChannel ? { channel: browserChannel } : {}),
      },
    },
  ],
});
