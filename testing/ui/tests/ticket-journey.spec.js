const { test, expect } = require('@playwright/test');
const { randomUUID } = require('node:crypto');
const { writeFile } = require('node:fs/promises');
const { LoginPage } = require('../pages/LoginPage');
const { TicketCreatePage } = require('../pages/TicketCreatePage');
const { MyTicketsPage } = require('../pages/MyTicketsPage');
const { TicketAssignmentsPage } = require('../pages/TicketAssignmentsPage');
const { AgentQueuePage } = require('../pages/AgentQueuePage');
const { identity, observeRequests } = require('../helpers/session-helpers');

test.use({ trace: 'off', video: 'off', screenshot: 'off' });

test('UI-004: Employee creates one ticket, Admin assigns it, and Agent sees the same ticket', async ({ page, browser }, testInfo) => {
  test.setTimeout(180_000);
  for (const role of ['EMPLOYEE', 'AGENT', 'ADMIN']) {
    if (!process.env[`UI_${role}_EMAIL`] || !process.env[`UI_${role}_PASSWORD`]) {
      throw new Error('Run npm.cmd run test:journey for private QA Employee, Agent and Admin prompts, or set their environment variables.');
    }
  }
  const baseURL = new URL(process.env.UI_BASE_URL).origin;
  const contexts = [];
  const requests = [];
  observeRequests(page, 'Employee', requests);
  try {
    const agentContext = await browser.newContext({ baseURL });
    contexts.push(agentContext);
    const adminContext = await browser.newContext({ baseURL });
    contexts.push(adminContext);
    const agentPage = await agentContext.newPage();
    const adminPage = await adminContext.newPage();
    observeRequests(agentPage, 'Agent', requests);
    observeRequests(adminPage, 'Admin', requests);
    let agent;

    await test.step('Verify all three QA logins before creating any ticket', async () => {
      for (const [actorPage, key, expectedRole] of [
        [agentPage, 'AGENT', 'Agent'], [adminPage, 'ADMIN', 'Administrator'], [page, 'EMPLOYEE', 'Employee'],
      ]) {
        const login = new LoginPage(actorPage);
        await login.open();
        await login.signIn(process.env[`UI_${key}_EMAIL`], process.env[`UI_${key}_PASSWORD`]);
        const user = await identity(actorPage, expectedRole);
        if (key === 'AGENT') agent = user;
      }
    });

    const create = new TicketCreatePage(page);
    const mine = new MyTicketsPage(page);
    const ticket = {
      issueType: `QA E2E ${new Date().toISOString()} ${randomUUID().slice(0, 8)}`,
      description: 'Automated QA ticket for the Ticket-service assignment. Please treat this as demonstration data.',
      urgency: 3, targetOrigin: baseURL,
    };
    await test.step('Employee creates exactly one uniquely labelled QA ticket', async () => {
      await create.expectReady();
      await create.fill(ticket.issueType, ticket.description);
      const response = await create.submitTicket();
      ticket.ticketId = response.body.ticketId;
      ticket.creationHttpStatus = response.status;
      ticket.recordedAtUtc = new Date().toISOString();
      ticket.cleanup = 'QA ticket is retained; no Ticket delete endpoint exists in checked source. Do not retry creation blindly.';
      // Keep the ID before asserting the status, including a partial-save failure.
      const evidence = testInfo.outputPath('created-ticket.json');
      await writeFile(evidence, JSON.stringify(ticket, null, 2));
      await testInfo.attach('Created QA ticket', { path: evidence, contentType: 'application/json' });
      expect(response.status, 'Ticket creation must return HTTP 201').toBe(201);
      expect(Number.isInteger(ticket.ticketId) && ticket.ticketId > 0).toBe(true);
    });

    await test.step('Employee sees the same stored ticket before and after refresh', async () => {
      await mine.expectTicket(ticket.ticketId, ticket.issueType, ticket.description);
      await page.reload();
      await mine.expectTicket(ticket.ticketId, ticket.issueType, ticket.description);
      await testInfo.attach('Employee ticket after refresh', {
        body: await mine.detail.screenshot(), contentType: 'image/png',
      });
    });

    await test.step('QA Admin reassigns only this new ticket to the supplied QA Agent if needed', async () => {
      const assignments = new TicketAssignmentsPage(adminPage);
      const originalAgentUserId = await assignments.assignedUserId(ticket);
      const alreadyAssigned = originalAgentUserId === agent.userId;
      // Never change rotation membership, other tickets, or this ticket's status.
      if (!alreadyAssigned) await assignments.reassign(ticket, agent.userId);
      const finalAgentUserId = await assignments.assignedUserId(ticket);
      expect(finalAgentUserId).toBe(agent.userId);
      testInfo.annotations.push({ type: 'assignment', description: alreadyAssigned
        ? 'Automatically assigned to the supplied QA Agent; no manual reassignment needed.'
        : 'QA Admin manually reassigned this newly created ticket; not an automatic-assignment proof.' });
      await testInfo.attach('Assignment method and exact ticket IDs', {
        body: JSON.stringify({ ticketId: ticket.ticketId, issueType: ticket.issueType,
          originalAgentUserId, finalAgentUserId,
          method: alreadyAssigned ? 'automatic-to-supplied-agent' : 'admin-manual-reassignment',
          scope: 'Only this newly created QA ticket; no rotation changes.' }, null, 2), contentType: 'application/json',
      });
    });

    await test.step('Agent sees that exact ticket and its details in My queue after refresh', async () => {
      const queue = new AgentQueuePage(agentPage);
      await queue.open();
      await queue.expectTicket(ticket);
      await queue.open();
      const card = await queue.expectTicket(ticket);
      await testInfo.attach('Same ticket in Agent queue after refresh', {
        body: await card.screenshot(), contentType: 'image/png',
      });
    });
  } finally {
    await testInfo.attach('Safe API navigation diagnostics', {
      body: JSON.stringify(requests, null, 2), contentType: 'application/json',
    });
    for (const context of contexts) await context.close();
  }
});
