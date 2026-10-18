const { readdir } = require('node:fs/promises');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { createInterface } = require('node:readline/promises');

async function main() {
  const history = path.resolve(__dirname, '../reports-history');
  const runs = (await readdir(history, { withFileTypes: true })
    .catch((error) => { if (error.code === 'ENOENT') return []; throw error; }))
    .filter((entry) => entry.isDirectory()).map((entry) => entry.name).sort().reverse();
  if (!runs.length) { console.log('No saved reports yet. Run a test first.'); return; }
  let selected = process.argv[2];
  if (!selected) {
    runs.forEach((run, index) => console.log(`${index + 1}. ${run}`));
    if (!process.stdin.isTTY) return;
    const rl = createInterface({ input: process.stdin, output: process.stdout });
    try {
      const answer = await rl.question('Report number to open (1 = newest): ');
      selected = runs[Number(answer.trim() || '1') - 1];
    } finally { rl.close(); }
  }
  if (!runs.includes(selected)) throw new Error('Choose a report from the list.');
  const result = spawnSync(process.execPath,
    [require.resolve('@playwright/test/cli'), 'show-report', path.join(history, selected, 'html')],
    { stdio: 'inherit' });
  if (result.error) throw result.error;
  process.exitCode = result.status ?? 1;
}

main().catch((error) => { console.error(error.message); process.exitCode = 1; });
