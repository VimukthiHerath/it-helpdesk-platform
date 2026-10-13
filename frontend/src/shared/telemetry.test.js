import { initTelemetry, redactUrl, sanitizeEnvelope } from './telemetry';

afterEach(() => localStorage.clear());

test('does nothing without a connection string', () => {
    expect(initTelemetry()).toBeNull();
});

test('reset tokens are blanked wherever they appear', () => {
    expect(redactUrl('https://app/reset-password?token=abc123&x=1')).toBe('https://app/reset-password?token=[redacted]&x=1');
    expect(redactUrl('/reset-password?x=1&token=abc123')).toBe('/reset-password?x=1&token=[redacted]');
    expect(redactUrl('/tickets')).toBe('/tickets');
});

test('page views keep only the path', () => {
    const envelope = {
        name: 'Microsoft.ApplicationInsights.pageview.PageView',
        baseData: { uri: 'https://app/reset-password?token=abc123#top', refUri: 'https://app/login?next=1' },
    };
    sanitizeEnvelope(envelope);
    expect(envelope.baseData.uri).toBe('https://app/reset-password');
    expect(envelope.baseData.refUri).toBe('https://app/login');
});

test('failed calls keep their url but lose any token', () => {
    const envelope = {
        name: 'Microsoft.ApplicationInsights.RemoteDependency',
        baseData: { name: 'POST https://gw/auth/v1/reset-password?token=abc123', data: 'https://gw/x?token=zzz' },
    };
    sanitizeEnvelope(envelope);
    expect(envelope.baseData.name).toContain('token=[redacted]');
    expect(envelope.baseData.data).toContain('token=[redacted]');
});

test('only the role is attached, no user details', () => {
    const encode = (value) => btoa(JSON.stringify(value)).replace(/=+$/, '');
    localStorage.setItem('token', `${encode({ alg: 'none' })}.${encode({ role: 'Agent', email: 'a@b.c', sub: '7' })}.sig`);

    const envelope = { name: 'Microsoft.ApplicationInsights.Exception', baseData: {} };
    sanitizeEnvelope(envelope);
    expect(envelope.baseData.properties).toEqual({ role: 'Agent' });
});
