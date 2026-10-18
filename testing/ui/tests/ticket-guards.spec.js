const { test, expect } = require('@playwright/test');
const { randomUUID } = require('node:crypto');
const { LoginPage } = require('../pages/LoginPage');
const { TicketCreatePage } = require('../pages/TicketCreatePage');
const { identity } = require('../helpers/session-helpers');

test.use({ trace: 'off', video: 'off', screenshot: 'off' });

test('UI-005: Nonexistent account cannot log in', async ({ page }) => {
  const login = new LoginPage(page);
  await login.open();
  await login.email.fill(`qa-nonexistent-${randomUUID()}@example.invalid`);
  await login.password.fill('Not-a-real-account-123!');
  const [response] = await Promise.all([
    page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname.replace(/\/$/, '') === '/auth/v1/login'),
    login.submit.click(),
  ]);
  expect(response.status(), 'Invalid credentials should be rejected').toBe(401);
  await expect(page).toHaveURL(/\/login\/?$/);
  await expect(page.getByRole('button', { name: 'Log out', exact: true })).toHaveCount(0);
  await expect(page.getByRole('alert').filter({ hasText: /invalid|incorrect|not found|failed|unauthorized/i })).toBeVisible();
  expect(await page.evaluate(() => Boolean(localStorage.getItem('token'))), 'Invalid login must not store a token').toBe(false);
});

test.describe('Ticket form and access guards', () => {
  test.beforeEach(async ({ page }) => {
    const email = process.env.UI_EMPLOYEE_EMAIL;
    const password = process.env.UI_EMPLOYEE_PASSWORD;
    if (!email || !password) throw new Error('QA Employee credentials are required. Use the private suite runner.');
    const login = new LoginPage(page);
    await login.open();
    await login.signIn(email, password);
    await identity(page, 'Employee');
    await new TicketCreatePage(page).expectReady();
  });

  for (const item of [
    { id: 'UI-006', name: 'Missing issue type', issue: '', description: 'QA validation only', errors: ['Issue type is required.'] },
    { id: 'UI-007', name: 'Missing description', issue: 'QA validation only', description: '', errors: ['Description is required.'] },
    { id: 'UI-008', name: 'Whitespace-only fields', issue: '   ', description: '   ', errors: ['Issue type is required.', 'Description is required.'] },
  ]) {
    test(`${item.id}: ${item.name} is rejected before ticket submission`, async ({ page }, testInfo) => {
      const create = new TicketCreatePage(page);
      const submissions = [];
      page.on('request', request => {
        if (request.method() === 'POST' && new URL(request.url()).pathname.replace(/\/$/, '') === '/ticket/v1') submissions.push('POST /ticket/v1');
      });
      // If validation regresses, fail on the attempted request without storing
      // an invalid ticket on Azure. A blocked attempt is still a failed check.
      await page.route(/\/ticket\/v1\/?(?:\?.*)?$/, route =>
        route.request().method() === 'POST' ? route.abort('blockedbyclient') : route.continue());
      await create.fill(item.issue, item.description);
      await create.submit.click();
      for (const error of item.errors) await expect(page.getByText(error, { exact: true })).toBeVisible();
      if (!item.issue.trim()) await expect(create.issueType).toHaveAttribute('aria-invalid', 'true');
      if (!item.description.trim()) await expect(create.description).toHaveAttribute('aria-invalid', 'true');
      await expect(create.submit).toBeEnabled();
      expect(submissions, 'No create request should occur while the form rejects these fields').toHaveLength(0);
      await testInfo.attach('Validation shown by ticket form', {
        body: await page.locator('form.ticket-form').screenshot(), contentType: 'image/png',
      });
    });
  }

  test('UI-009: Employee cannot open the Agent queue route', async ({ page }, testInfo) => {
    await page.goto('/agent/queue');
    await expect(page).toHaveURL(url => url.pathname === '/');
    await new TicketCreatePage(page).expectReady();
    await expect(page.getByRole('heading', { name: 'My queue', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Log out', exact: true })).toBeVisible();
    await testInfo.attach('Frontend route access scope', {
      body: JSON.stringify({ role: 'Employee', requestedRoute: '/agent/queue', finalRoute: '/',
        scope: 'Browser route guard only; this does not prove backend authorization.' }, null, 2), contentType: 'application/json',
    });
  });
});
