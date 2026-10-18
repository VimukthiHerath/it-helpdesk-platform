const { spawnSync } = require('node:child_process');
const { createInterface } = require('node:readline/promises');
const { Writable } = require('node:stream');
const path = require('node:path');
const { findCreatedTicket } = require('./find-created-ticket');

async function main() {
  const env = { ...process.env };
  if (!env.UI_BASE_URL) throw new Error('Set UI_BASE_URL to the Azure frontend origin first.');
  const ticket = findCreatedTicket(path.resolve(__dirname, '..'), env.UI_BASE_URL);
  env.UI_EXISTING_QA_TICKET = JSON.stringify(ticket);
  console.log(`Read-only Agent check for saved Ticket #${ticket.ticketId}: ${ticket.issueType}`);
  console.log('Use the Agent assigned to this ticket. This run creates no ticket and changes no status.');
  if (!env.UI_AGENT_EMAIL || !env.UI_AGENT_PASSWORD) {
    if (!process.stdin.isTTY) throw new Error('Use an interactive terminal for private prompts, or set UI_AGENT_EMAIL and UI_AGENT_PASSWORD.');
    let hidden = false;
    const output = new Writable({ write(chunk, encoding, done) {
      if (!hidden) process.stdout.write(chunk, encoding);
      done();
    } });
    const rl = createInterface({ input: process.stdin, output, terminal: true });
    const abort = new AbortController();
    rl.on('SIGINT', () => abort.abort());
    rl.on('close', () => abort.abort());
    try {
      if (!env.UI_AGENT_EMAIL) env.UI_AGENT_EMAIL = (await rl.question('QA Agent email: ', { signal: abort.signal })).trim();
      if (!env.UI_AGENT_PASSWORD) {
        process.stdout.write('QA Agent password (hidden): ');
        hidden = true;
        env.UI_AGENT_PASSWORD = await rl.question('', { signal: abort.signal });
        hidden = false;
        process.stdout.write('\n');
      }
    } finally { hidden = false; rl.close(); }
  }
  if (!env.UI_AGENT_EMAIL || !env.UI_AGENT_PASSWORD) throw new Error('Both QA Agent email and password are required.');
  const result = spawnSync(process.execPath,
    [path.join(__dirname, 'run-headed.js'), 'agent-queue.spec.js', ...process.argv.slice(2)],
    { stdio: 'inherit', env });
  if (result.error) throw new Error('Could not start the Agent test runner.');
  process.exitCode = result.status ?? 1;
}

main().catch((error) => {
  console.error(error.name === 'AbortError' ? 'Credential entry cancelled.' : error.message);
  process.exitCode = 1;
});
