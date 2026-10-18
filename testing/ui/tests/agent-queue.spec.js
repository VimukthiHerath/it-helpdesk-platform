const { test, expect } = require('@playwright/test');
const { LoginPage } = require('../pages/LoginPage');
const { AgentQueuePage } = require('../pages/AgentQueuePage');

test.use({ trace: 'off', video: 'off', screenshot: 'off' });

const diagnostics = new WeakMap();
test.beforeEach(async ({ page }) => {
  const entries = [];
  diagnostics.set(page, entries);
  const relevant = pathname => /^\/(auth|ticket|assignment)\/v1(?:\/|$)/.test(pathname);
  page.on('response', response => {
    const pathname = new URL(response.url()).pathname;
    if (relevant(pathname)) entries.push({ method: response.request().method(), pathname, status: response.status() });
  });
  page.on('requestfailed', request => {
    const pathname = new URL(request.url()).pathname;
    if (relevant(pathname)) entries.push({ method: request.method(), pathname, failed: true });
  });
});
test.afterEach(async ({ page }, testInfo) => {
  await testInfo.attach('Safe API navigation diagnostics', {
    body: JSON.stringify({ finalPath: new URL(page.url()).pathname, requests: diagnostics.get(page) || [] }, null, 2),
    contentType: 'application/json',
  });
});

test('UI-003: Assigned Agent sees the Employee-created ticket in My queue', async ({ page }, testInfo) => {
  test.setTimeout(120_000);
  const { UI_AGENT_EMAIL: email, UI_AGENT_PASSWORD: password } = process.env;
  if (!email || !password || !process.env.UI_EXISTING_QA_TICKET) {
    throw new Error('Run npm.cmd run test:agent to reuse saved Employee ticket evidence and enter Agent credentials privately.');
  }
  const ticket = JSON.parse(process.env.UI_EXISTING_QA_TICKET);
  expect(ticket.targetOrigin, 'Ticket evidence must belong to the selected deployment')
    .toBe(new URL(process.env.UI_BASE_URL).origin);
  expect(Number.isInteger(ticket.ticketId) && ticket.ticketId > 0).toBe(true);

  await test.step('Log in as the QA Agent', async () => {
    const login = new LoginPage(page);
    await login.open();
    await login.signIn(email, password);
    await expect(page).not.toHaveURL(/\/login\/?$/);
    await expect(page.getByRole('navigation', { name: 'Main', exact: true })
      .getByRole('link', { name: 'All tickets', exact: true })).toBeVisible();
  });

  await test.step('Verify the logged-in Agent owns the existing ticket assignment', async () => {
    // Return only identity claims, never the token or login response.
    const identity = await page.evaluate(() => {
      try {
        const token = localStorage.getItem('token');
        const body = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
        return { userId: Number(body.sub), role: body.role ?? body['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] };
      } catch { return null; }
    });
    expect(identity?.role, 'Use an account with the Agent role').toBe('Agent');
    expect(Number.isInteger(identity.userId) && identity.userId > 0).toBe(true);

    let assignedUserId;
    let firstVisit = true;
    await expect.poll(async () => {
      if (firstVisit) {
        await page.getByRole('navigation', { name: 'Main', exact: true })
          .getByRole('link', { name: 'All tickets', exact: true }).click();
        firstVisit = false;
      } else await page.reload();
      await expect(page.getByRole('heading', { name: 'All tickets', exact: true }),
        'Signed-in All tickets page should load; see API navigation diagnostics if redirected').toBeVisible();
      await expect(page.getByText('Loading tickets...', { exact: true })).not.toBeVisible();
      await expect(page.locator('.assignments-message[role="alert"]'), 'All tickets must load without an API error').toHaveCount(0);
      const row = page.locator(`tr[data-ticket-id="${ticket.ticketId}"]`);
      await expect(row).toBeVisible();
      await expect(row).toContainText(ticket.issueType);
      const cell = row.getByRole('cell').filter({ hasText: /^User ID \d+$/ });
      if (!await cell.count()) return false;
      assignedUserId = Number((await cell.innerText()).match(/\d+/)[0]);
      return Number.isInteger(assignedUserId) && assignedUserId > 0;
    }, { timeout: 30_000, intervals: [1000, 2000, 5000], message: 'Wait for the existing ticket assignment to appear in the UI' }).toBe(true);

    await testInfo.attach('Ticket and Agent identity check', {
      body: JSON.stringify({ ticketId: ticket.ticketId, issueType: ticket.issueType,
        loggedInAgentUserId: identity.userId, assignedAgentUserId: assignedUserId,
        sameAgent: identity.userId === assignedUserId }, null, 2), contentType: 'application/json',
    });
    if (assignedUserId !== identity.userId) {
      testInfo.annotations.push({ type: 'setup', description: 'Credentials belong to another Agent; queue check not executed.' });
      throw new Error(`TEST SETUP: Ticket #${ticket.ticketId} is assigned to Agent User ID ${assignedUserId}, `
        + `but this login is Agent User ID ${identity.userId}. Use the assigned Agent account. `
        + 'The queue check has not run; this mismatch is not proof of a product bug.');
    }
  });

  await test.step('Find the same ticket and its details in My queue, including after refresh', async () => {
    const queue = new AgentQueuePage(page);
    await queue.open();
    await queue.expectTicket(ticket);
    await queue.open();
    const card = await queue.expectTicket(ticket);
    await testInfo.attach('Same QA ticket in assigned Agent queue', {
      body: await card.screenshot(), contentType: 'image/png',
    });
  });
});
