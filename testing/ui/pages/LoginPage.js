const { expect } = require('@playwright/test');

// Page object: keep the page's locators and actions together, outside the test.
class LoginPage {
  constructor(page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: 'IT Helpdesk', exact: true });
    // Required-field asterisks can be included in the label text on deployment.
    this.email = page.getByLabel(/^Email\s*\*?$/i);
    this.password = page.getByLabel(/^Password\s*\*?$/i);
    this.submit = page.getByRole('button', { name: 'Log in', exact: true });
  }

  async open() {
    const response = await this.page.goto('/login');
    expect(response, 'The login page should return an HTTP response').not.toBeNull();
    expect(response.ok(), `Login page returned HTTP ${response.status()}`).toBeTruthy();
    await expect(this.page).toHaveURL(/\/login\/?$/);
  }

  async expectReady() {
    await expect(this.heading).toBeVisible();
    await expect(this.email).toBeVisible();
    await expect(this.email).toHaveAttribute('type', 'email');
    await expect(this.password).toBeVisible();
    await expect(this.password).toHaveAttribute('type', 'password');
    await expect(this.submit).toBeVisible();
    await expect(this.submit).toBeEnabled();
  }

  async signIn(email, password) {
    await this.email.fill(email);
    await this.password.fill(password);
    // Observe the real request from the UI; do not mock authentication.
    const [response] = await Promise.all([
      this.page.waitForResponse((r) => r.request().method() === 'POST'
        && new URL(r.url()).pathname.replace(/\/$/, '') === '/auth/v1/login'),
      this.submit.click(),
    ]);
    expect(response.status(), 'QA account login should return HTTP 200').toBe(200);
    // A URL change occurs before the protected page finishes checking the session.
    await expect(this.page.getByRole('button', { name: 'Log out', exact: true }),
      'The signed-in screen must be ready after login; HTTP 200 alone is insufficient')
      .toBeVisible({ timeout: 30_000 });
  }
}

module.exports = { LoginPage };
