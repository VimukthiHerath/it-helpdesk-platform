const { expect } = require('@playwright/test');

class TicketCreatePage {
  constructor(page) {
    this.page = page;
    this.issueType = page.getByLabel(/^Issue type\s*\*?$/i);
    this.description = page.getByLabel(/^Description\s*\*?$/i);
    this.requiredBy = page.getByLabel(/^Required by$/i);
    this.submit = page.getByRole('button', { name: /^Create ticket$/i });
  }

  async expectReady() {
    await expect(this.issueType, 'An Employee should see the new-ticket form').toBeVisible();
    await expect(this.description).toBeVisible();
    await expect(this.submit).toBeEnabled();
  }

  async fill(issueType, description) {
    await this.issueType.fill(issueType);
    await this.description.fill(description);
    // Use the least urgent tier for repeatable QA demonstration data.
    await this.requiredBy.selectOption('3');
  }

  async submitTicket() {
    const [response] = await Promise.all([
      this.page.waitForResponse((r) => r.request().method() === 'POST'
        && new URL(r.url()).pathname.replace(/\/$/, '') === '/ticket/v1'),
      this.submit.click(),
    ]);
    return { status: response.status(), body: await response.json().catch(() => ({})) };
  }
}

module.exports = { TicketCreatePage };
