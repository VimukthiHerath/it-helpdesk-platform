const { expect } = require('@playwright/test');

class MyTicketsPage {
  constructor(page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: /^My tickets$/i });
    this.detail = page.getByRole('article');
  }

  async expectTicket(ticketId, issueType, description) {
    await expect(this.page).toHaveURL(/\/my-tickets\/?$/);
    await expect(this.heading).toBeVisible();
    const item = this.page.getByRole('button').filter({
      has: this.page.getByText(issueType, { exact: true }),
    });
    await expect(item, 'Exactly one card should match this run\'s unique ticket').toHaveCount(1);
    await expect(item).toContainText(`Ticket #${ticketId}`);
    await item.click();
    await expect(this.detail.getByRole('heading', { name: issueType, exact: true })).toBeVisible();
    await expect(this.detail.getByText(description, { exact: true })).toBeVisible();
    await expect(this.detail.getByText('Within 24 hours', { exact: true })).toBeVisible();
  }
}

module.exports = { MyTicketsPage };
