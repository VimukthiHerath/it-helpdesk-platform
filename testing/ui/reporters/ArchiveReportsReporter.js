const { cp, mkdir, access } = require('node:fs/promises');
const path = require('node:path');
const { randomUUID } = require('node:crypto');

async function archiveReports() {
  const root = path.resolve(__dirname, '..');
  const source = path.join(root, 'reports');
  try { await access(path.join(source, 'html', 'index.html')); }
  catch (error) { if (error.code === 'ENOENT') return; throw error; }
  const runId = `${new Date().toISOString().replace(/[:.]/g, '-')}-${randomUUID().slice(0, 8)}`;
  const destination = path.join(root, 'reports-history', runId);
  await mkdir(path.dirname(destination), { recursive: true });
  await cp(source, destination, { recursive: true, errorOnExist: true, force: false });
  console.log(`Saved report history: ${runId}`);
  console.log(`Open: npm.cmd run report:history -- ${runId}`);
  return destination;
}

class ArchiveReportsReporter {
  // All reporters have finished onEnd, including redaction and HTML output.
  async onExit() { await archiveReports(); }
}

module.exports = ArchiveReportsReporter;
module.exports.archiveReports = archiveReports;

if (require.main === module) {
  archiveReports().catch((error) => {
    console.error(`Could not save report history: ${error.message}`);
    process.exitCode = 1;
  });
}
