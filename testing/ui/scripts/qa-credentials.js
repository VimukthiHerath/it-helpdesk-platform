const { createInterface } = require('node:readline/promises');
const { Writable } = require('node:stream');

async function promptCredentials(env, roles) {
  if (roles.every(role => env[`UI_${role.toUpperCase()}_EMAIL`] && env[`UI_${role.toUpperCase()}_PASSWORD`])) return;
  if (!process.stdin.isTTY) throw new Error(`Provide QA ${roles.join(', ')} credentials through environment variables or an interactive terminal.`);
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
    for (const role of roles) {
      const emailKey = `UI_${role.toUpperCase()}_EMAIL`;
      const passwordKey = `UI_${role.toUpperCase()}_PASSWORD`;
      if (!env[emailKey]) env[emailKey] = (await rl.question(`QA ${role} email: `, { signal: abort.signal })).trim();
      if (!env[passwordKey]) {
        process.stdout.write(`QA ${role} password (hidden): `);
        hidden = true;
        env[passwordKey] = await rl.question('', { signal: abort.signal });
        hidden = false;
        process.stdout.write('\n');
      }
      if (!env[emailKey] || !env[passwordKey]) throw new Error(`Both QA ${role} email and password are required.`);
    }
  } finally { hidden = false; rl.close(); }
}

module.exports = { promptCredentials };
