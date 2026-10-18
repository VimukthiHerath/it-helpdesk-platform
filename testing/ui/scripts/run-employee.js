const { spawnSync } = require('node:child_process');
const { createInterface } = require('node:readline/promises');
const { Writable } = require('node:stream');
const path = require('node:path');

async function main() {
  const env = { ...process.env };
  if (!env.UI_BASE_URL) {
    throw new Error('Set UI_BASE_URL to the Azure frontend origin first.');
  }
  console.log('QA Employee flow: creates ONE labelled QA ticket on Azure per run.');
  if (!env.UI_EMPLOYEE_EMAIL || !env.UI_EMPLOYEE_PASSWORD) {
    if (!process.stdin.isTTY) {
      throw new Error('Use an interactive terminal for private prompts, or supply '
        + 'UI_EMPLOYEE_EMAIL and UI_EMPLOYEE_PASSWORD through the environment.');
    }
    let hidden = false;
    const output = new Writable({
      write(chunk, encoding, done) {
        if (!hidden) process.stdout.write(chunk, encoding);
        done();
      },
    });
    const rl = createInterface({ input: process.stdin, output, terminal: true });
    const abort = new AbortController();
    rl.on('SIGINT', () => abort.abort());
    rl.on('close', () => abort.abort());
    try {
      if (!env.UI_EMPLOYEE_EMAIL) {
        env.UI_EMPLOYEE_EMAIL = (await rl.question('QA Employee email: ', { signal: abort.signal })).trim();
      }
      if (!env.UI_EMPLOYEE_PASSWORD) {
        process.stdout.write('QA Employee password (hidden): ');
        hidden = true;
        env.UI_EMPLOYEE_PASSWORD = await rl.question('', { signal: abort.signal });
        hidden = false;
        process.stdout.write('\n');
      }
    } finally {
      hidden = false;
      rl.close();
    }
  }
  if (!env.UI_EMPLOYEE_EMAIL || !env.UI_EMPLOYEE_PASSWORD) {
    throw new Error('Both QA Employee email and password are required.');
  }

  // Pass credentials only in the child environment, never in command arguments.
  const result = spawnSync(process.execPath,
    [path.join(__dirname, 'run-headed.js'), 'employee-ticket.spec.js', ...process.argv.slice(2)],
    { stdio: 'inherit', env });
  if (result.error) throw new Error('Could not start the Employee test runner.');
  process.exitCode = result.status ?? 1;
}

main().catch((error) => {
  console.error(error.name === 'AbortError' ? 'Credential entry cancelled.' : error.message);
  process.exitCode = 1;
});
