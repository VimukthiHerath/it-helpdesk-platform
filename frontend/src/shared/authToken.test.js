import { decodeToken, getUserEmail, getUserId, getUserRole, isAdmin, isAgent } from './authToken';

// Builds an unsigned token: the frontend only reads the payload, the services
// are the ones that verify the signature.
const makeToken = (payload) => {
    const encode = (value) => btoa(JSON.stringify(value))
        .replace(/\+/g, '-')
        .replace(/\//g, '_')
        .replace(/=+$/, '');
    return `${encode({ alg: 'RS256', typ: 'JWT' })}.${encode(payload)}.signature`;
};

const LONG_ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

describe('authToken', () => {
    beforeEach(() => localStorage.clear());

    test('reads the role from the short "role" claim that Auth.Api writes now', () => {
        localStorage.setItem('token', makeToken({ sub: '1', email: 'a@b.c', role: 'Administrator' }));

        expect(getUserRole()).toBe('Administrator');
        expect(isAdmin()).toBe(true);
        expect(isAgent()).toBe(false);
    });

    test('still reads the long claim name used by tokens from the older build', () => {
        localStorage.setItem('token', makeToken({ sub: '2', [LONG_ROLE_CLAIM]: 'Agent' }));

        expect(getUserRole()).toBe('Agent');
        expect(isAgent()).toBe(true);
        expect(isAdmin()).toBe(false);
    });

    test('prefers the short claim when both are present', () => {
        localStorage.setItem('token', makeToken({ role: 'Employee', [LONG_ROLE_CLAIM]: 'Administrator' }));

        expect(getUserRole()).toBe('Employee');
    });

    test('has no role when there is no token or the token has no role', () => {
        expect(getUserRole()).toBeNull();
        expect(isAdmin()).toBe(false);

        localStorage.setItem('token', makeToken({ sub: '3' }));
        expect(getUserRole()).toBeNull();
    });

    test('reads the user id and email from the token', () => {
        localStorage.setItem('token', makeToken({ sub: '7', email: 'mia@example.com', role: 'Employee' }));

        expect(getUserId()).toBe(7);
        expect(getUserEmail()).toBe('mia@example.com');
    });

    test('returns null for a malformed token instead of throwing', () => {
        expect(decodeToken('not-a-token')).toBeNull();
        expect(decodeToken('a.b.c')).toBeNull();
        expect(decodeToken(null)).toBeNull();
    });
});
