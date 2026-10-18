const { test, expect } = require('@playwright/test');
const { randomUUID } = require('node:crypto');
const { writeFile } = require('node:fs/promises');
const { LoginPage } = require('../pages/LoginPage');
const { TicketCreatePage } = require('../pages/TicketCreatePage');
const { MyTicketsPage } = require('../pages/MyTicketsPage');

// Login traces can contain passwords and JWTs. Keep this authenticated case's
// trace/video off and attach only the deliberately selected QA evidence below.
test.use({ trace: 'off', video: 'off', screenshot: 'off' });

test('UI-002: Employee creates a ticket and sees the same ticket after refresh', async ({ page }, testInfo) => {
  test.setTimeout(120_000);
  const email = process.env.UI_EMPLOYEE_EMAIL;
  const password = process.env.UI_EMPLOYEE_PASSWORD;
  if (!email || !password) {
    throw new Error('QA Employee credentials are missing. Run npm.cmd run test:employee '
      + 'for private prompts, or set UI_EMPLOYEE_EMAIL and UI_EMPLOYEE_PASSWORD.');
  }

  const issueType = `QA E2E ${new Date().toISOString()} ${randomUUID().slice(0, 8)}`;
  const description = 'Automated QA ticket for the Ticket-service assignment. '
    + 'Please treat this as demonstration data.';
  const login = new LoginPage(page);
  const create = new TicketCreatePage(page);
  const mine = new MyTicketsPage(page);
  let ticketId;

  await test.step('Log in as the QA Employee and open the ticket form', async () => {
    await login.open();
    await login.signIn(email, password);
    await create.expectReady();
  });

  await test.step('Create one uniquely named QA ticket through the UI', async () => {
    await create.fill(issueType, description);
    const response = await create.submitTicket();
    ticketId = response.body.ticketId;
    // Capture the identifier even if Kafka publication fails after the row was
    // saved. Never retry creation automatically, which could create duplicates.
    if (Number.isInteger(ticketId) && ticketId > 0) {
      const evidence = testInfo.outputPath('created-ticket.json');
      await writeFile(evidence, JSON.stringify({
        ticketId, issueType, description, urgency: 3,
        creationHttpStatus: response.status,
        targetOrigin: new URL(process.env.UI_BASE_URL).origin,
        recordedAtUtc: new Date().toISOString(),
        cleanup: 'No Ticket delete API exists in the checked source; QA ticket is retained.',
      }, null, 2));
      await testInfo.attach('Created QA ticket', { path: evidence, contentType: 'application/json' });
    }
    expect(response.status, 'Ticket creation should return HTTP 201').toBe(201);
    expect(Number.isInteger(ticketId) && ticketId > 0, 'Creation should return a positive ticket ID').toBe(true);
  });

  await test.step('Verify this ticket in My Tickets and its details', async () => {
    await mine.expectTicket(ticketId, issueType, description);
  });

  await test.step('Refresh and confirm the same ticket is still stored', async () => {
    await page.reload();
    await mine.expectTicket(ticketId, issueType, description);
    const screenshot = testInfo.outputPath('my-created-ticket.png');
    await page.screenshot({ path: screenshot, fullPage: true });
    await testInfo.attach('My Tickets after refresh', { path: screenshot, contentType: 'image/png' });
  });
});
