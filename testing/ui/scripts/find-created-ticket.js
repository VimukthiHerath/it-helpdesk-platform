const { readdirSync, readFileSync } = require('node:fs');
const path = require('node:path');

// Search retained evidence, not a hardcoded ticket number. This runs before
// Playwright clears its latest artifacts folder.
function findCreatedTicket(root, targetURL) {
  const candidates = [];
  const visit = (directory) => {
    let entries;
    try { entries = readdirSync(directory, { withFileTypes: true }); }
    catch (error) { if (error.code === 'ENOENT') return; throw error; }
    for (const entry of entries) {
      const file = path.join(directory, entry.name);
      if (entry.isDirectory()) visit(file);
      else if (entry.name === 'created-ticket.json') {
        const ticket = JSON.parse(readFileSync(file, 'utf8'));
        if (ticket.targetOrigin === new URL(targetURL).origin
            && Number.isInteger(ticket.ticketId) && ticket.ticketId > 0
            && ticket.creationHttpStatus === 201
            && ticket.issueType?.startsWith('QA E2E ')
            && Number.isFinite(Date.parse(ticket.recordedAtUtc))) candidates.push(ticket);
      }
    }
  };
  visit(path.join(root, 'reports-history'));
  visit(path.join(root, 'reports/artifacts'));
  candidates.sort((a, b) => Date.parse(b.recordedAtUtc) - Date.parse(a.recordedAtUtc));
  if (!candidates.length) throw new Error('No saved QA ticket for this deployment. Run the Employee journey first.');
  return candidates[0];
}

module.exports = { findCreatedTicket };
