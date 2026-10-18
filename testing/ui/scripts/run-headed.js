const { spawnSync } = require('node:child_process');

// Windows already has Edge. Use it for the visible demo while CI keeps Chromium.
// This is still Playwright automation; no Selenium or extra driver is involved.
const env = { ...process.env };
if (process.platform === 'win32' && !env.UI_BROWSER_CHANNEL) {
  env.UI_BROWSER_CHANNEL = 'msedge';
}
console.log(`Visible browser: ${env.UI_BROWSER_CHANNEL || 'bundled Chromium'}`);

const cli = require.resolve('@playwright/test/cli');

// Video-on-failure still needs FFmpeg when a new browser page is created.
// Run this in the same environment as the test so the user's cache is prepared.
console.log('Checking Playwright video helper...');
const install = spawnSync(
  process.execPath,
  [cli, 'install', 'ffmpeg'],
  { stdio: 'inherit', env },
);
if (install.error) {
  console.error('Could not install the Playwright video helper:', install.error.message);
}
if (install.error || install.status !== 0) {
  process.exit(install.status ?? 1);
}

const result = spawnSync(
  process.execPath,
  [cli, 'test', '--headed', ...process.argv.slice(2)],
  { stdio: 'inherit', env },
);
if (result.error) {
  console.error('Could not start the Playwright test runner:', result.error.message);
}
process.exit(result.status ?? 1);
