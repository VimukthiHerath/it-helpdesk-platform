const { expect } = require('@playwright/test');

class TicketAssignmentsPage {
  constructor(page) { this.page = page; }

  async open(refresh = false) {
    if (refresh) await this.page.reload();
    else await this.page.getByRole('navigation', { name: 'Main', exact: true })
      .getByRole('link', { name: 'All tickets', exact: true }).click();
    await expect(this.page.getByRole('heading', { name: 'All tickets', exact: true })).toBeVisible();
    await expect(this.page.getByText('Loading tickets...', { exact: true })).not.toBeVisible();
    await expect(this.page.locator('.assignments-message[role="alert"]'), 'All tickets should load without an API error').toHaveCount(0);
  }

  row(ticket) {
    return this.page.locator(`tr[data-ticket-id="${ticket.ticketId}"]`);
  }

  async assignedUserId(ticket) {
    await this.open();
    let userId;
    let first = true;
    await expect.poll(async () => {
      if (!first) await this.open(true);
      first = false;
      const row = this.row(ticket);
      await expect(row).toBeVisible();
      await expect(row).toContainText(ticket.issueType);
      const cell = row.getByRole('cell').filter({ hasText: /^User ID \d+$/ });
      if (!await cell.count()) return false;
      userId = Number((await cell.innerText()).match(/\d+/)[0]);
      return Number.isInteger(userId) && userId > 0;
    }, { timeout: 30_000, intervals: [1000, 2000, 5000], message: `Ticket #${ticket.ticketId} should be assigned` }).toBe(true);
    return userId;
  }

  async reassign(ticket, agentUserId) {
    const row = this.row(ticket);
    await expect(row).toContainText(ticket.issueType);
    await row.getByRole('combobox').selectOption(String(agentUserId));
    const [response] = await Promise.all([
      this.page.waitForResponse(r => r.request().method() === 'PATCH'
        && new URL(r.url()).pathname.replace(/\/$/, '') === `/assignment/v1/${ticket.ticketId}/reassign`),
      row.getByRole('button', { name: 'Reassign', exact: true }).click(),
    ]);
    expect(response.status(), 'Admin should reassign only the newly created QA ticket').toBe(200);
    await expect(row.getByRole('cell').filter({ hasText: /^User ID \d+$/ }))
      .toHaveText(`User ID ${agentUserId}`);
  }
}

module.exports = { TicketAssignmentsPage };
