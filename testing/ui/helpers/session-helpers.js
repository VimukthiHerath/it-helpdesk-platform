const { expect } = require('@playwright/test');

async function identity(page, expectedRole) {
  const user = await page.evaluate(() => {
    try {
      const token = localStorage.getItem('token');
      const body = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
      return { userId: Number(body.sub), role: body.role ?? body['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] };
    } catch { return null; }
  });
  expect(user?.role, `Use a QA ${expectedRole} account`).toBe(expectedRole);
  expect(Number.isInteger(user.userId) && user.userId > 0).toBe(true);
  return user;
}

function observeRequests(page, label, entries) {
  const relevant = pathname => /^\/(auth|ticket|assignment)\/v1(?:\/|$)/.test(pathname);
  page.on('response', response => {
    const pathname = new URL(response.url()).pathname;
    if (relevant(pathname)) entries.push({ actor: label, method: response.request().method(), pathname, status: response.status() });
  });
  page.on('requestfailed', request => {
    const pathname = new URL(request.url()).pathname;
    if (relevant(pathname)) entries.push({ actor: label, method: request.method(), pathname, failed: true });
  });
}

module.exports = { identity, observeRequests };
