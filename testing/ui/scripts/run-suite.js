const { spawnSync } = require('node:child_process');
const path = require('node:path');
const { promptCredentials } = require('./qa-credentials');

async function main() {
  const env = { ...process.env };
  if (!env.UI_BASE_URL) throw new Error('Set UI_BASE_URL to the Azure frontend origin first.');
  const flags = process.argv.slice(2);
  if (flags.some(flag => !['--journey-only', '--headless'].includes(flag))) throw new Error('Supported options: --journey-only, --headless');
  console.log('Combined journey creates ONE labelled QA ticket. QA Admin may reassign ONLY that new ticket to the supplied QA Agent.');
  console.log('The report records manual reassignment separately from automatic assignment. No rotation or ticket status changes.');
  await promptCredentials(env, ['Employee', 'Agent', 'Admin']);
  const files = flags.includes('--journey-only') ? ['ticket-journey.spec.js']
    : ['login-smoke.spec.js', 'ticket-journey.spec.js', 'ticket-guards.spec.js'];
  const headless = flags.includes('--headless') || Boolean(env.CI);
  const command = headless ? [require.resolve('@playwright/test/cli'), 'test', ...files]
    : [path.join(__dirname, 'run-headed.js'), ...files];
  const result = spawnSync(process.execPath, command, { stdio: 'inherit', env });
  if (result.error) throw new Error('Could not start the UI suite runner.');
  process.exitCode = result.status ?? 1;
}

main().catch(error => {
  console.error(error.name === 'AbortError' ? 'Credential entry cancelled.' : error.message);
  process.exitCode = 1;
});
