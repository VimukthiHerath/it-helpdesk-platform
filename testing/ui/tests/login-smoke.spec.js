const { test } = require('@playwright/test');
const { LoginPage } = require('../pages/LoginPage');

// First setup check only. Authenticated ticket creation and queue checks follow.
test('UI-001: Azure login page shows the required login controls', async ({ page }, testInfo) => {
  const login = new LoginPage(page);

  await test.step('Open the deployed login page', async () => {
    await login.open();
  });

  await test.step('Check the heading, inputs and login button', async () => {
    await login.expectReady();
  });

  await test.step('Save screenshot evidence', async () => {
    const screenshot = testInfo.outputPath('login-page.png');
    await page.screenshot({ path: screenshot, fullPage: true });
    await testInfo.attach('Azure login page', { path: screenshot, contentType: 'image/png' });
  });
});
