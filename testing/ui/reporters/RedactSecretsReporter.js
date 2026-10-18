const { readdir, readFile, writeFile } = require('node:fs/promises');
const path = require('node:path');

// This reporter must run before the HTML/JSON/JUnit reporters: Playwright API
// steps can include fill() values, even when browser tracing is disabled.
class RedactSecretsReporter {
  constructor() {
    this.secrets = ['UI_EMPLOYEE_PASSWORD', 'UI_EMPLOYEE_EMAIL', 'UI_AGENT_PASSWORD', 'UI_AGENT_EMAIL', 'UI_ADMIN_PASSWORD', 'UI_ADMIN_EMAIL']
      .map((key) => process.env[key]).filter(Boolean).sort((a, b) => b.length - a.length);
  }

  redact(text) {
    if (typeof text !== 'string') return text;
    for (const secret of this.secrets) {
      text = text.split(secret).join('[redacted]');
      text = text.split(JSON.stringify(secret).slice(1, -1)).join('[redacted]');
    }
    return text;
  }

  scrubError(error) {
    if (!error) return;
    for (const key of ['message', 'stack', 'value', 'snippet']) {
      if (typeof error[key] === 'string') error[key] = this.redact(error[key]);
    }
  }

  scrubStep(step) {
    const locator = step.params?.locator || step.subtitle || '';
    if (/password/i.test(String(locator)) && /^Fill\b/.test(step.title)) {
      step.title = 'Fill QA password [redacted]';
      if (step.params && 'value' in step.params) step.params.value = '[redacted]';
    }
    step.title = this.redact(step.title);
    step.subtitle = this.redact(step.subtitle);
    for (const [key, value] of Object.entries(step.params || {})) {
      if (typeof value === 'string') step.params[key] = this.redact(value);
    }
    this.scrubError(step.error);
  }

  onStepBegin(test, result, step) { this.scrubStep(step); }
  onStepEnd(test, result, step) { this.scrubStep(step); }
  onTestEnd(test, result) { for (const error of result.errors) this.scrubError(error); }
  onError(error) { this.scrubError(error); }

  async onEnd() {
    // Failure context Markdown is generated independently of reporters.
    const artifacts = path.resolve(__dirname, '../reports/artifacts');
    const scrubDirectory = async (dir) => {
      let entries;
      try { entries = await readdir(dir, { withFileTypes: true }); }
      catch (error) { if (error.code === 'ENOENT') return; throw error; }
      for (const entry of entries) {
        const file = path.join(dir, entry.name);
        if (entry.isDirectory()) await scrubDirectory(file);
        else if (entry.name.endsWith('.md')) {
          const original = await readFile(file, 'utf8');
          const redacted = this.redact(original);
          if (redacted !== original) await writeFile(file, redacted);
        }
      }
    };
    await scrubDirectory(artifacts);
  }
}

module.exports = RedactSecretsReporter;
