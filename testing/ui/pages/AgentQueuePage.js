const { expect } = require('@playwright/test');

class AgentQueuePage {
  constructor(page) { this.page = page; }

  async open() {
    const [queue, tickets] = await Promise.all([
      this.page.waitForResponse(r => r.request().method() === 'GET'
        && new URL(r.url()).pathname.replace(/\/$/, '') === '/assignment/v1/queue'),
      this.page.waitForResponse(r => r.request().method() === 'GET'
        && new URL(r.url()).pathname.replace(/\/$/, '') === '/ticket/v1'),
      new URL(this.page.url()).pathname === '/agent/queue'
        ? this.page.reload()
        : this.page.getByRole('navigation', { name: 'Main', exact: true })
          .getByRole('link', { name: 'My queue', exact: true }).click(),
    ]);
    expect(queue.status(), 'Agent queue API should return HTTP 200').toBe(200);
    expect(tickets.status(), 'Ticket details API should return HTTP 200').toBe(200);
    await expect(this.page.getByRole('heading', { name: 'My queue', exact: true })).toBeVisible();
    await expect(this.page.getByText('Loading your queue...', { exact: true })).not.toBeVisible();
  }

  async expectTicket(ticket) {
    // The card has no semantic role or test ID; scope by the exact ticket ID.
    const card = this.page.getByRole('region', { name: 'Your assigned tickets', exact: true })
      .locator('.queue-list-item').filter({ has: this.page.getByText(`Ticket #${ticket.ticketId}`, { exact: true }) });
    await expect(card).toHaveCount(1);
    await expect(card).toContainText(ticket.issueType);
    await expect(card).toContainText(ticket.description);
    await expect(card).toContainText('Within 24 hours');
    return card;
  }
}

module.exports = { AgentQueuePage };
