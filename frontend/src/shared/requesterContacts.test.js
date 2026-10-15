import { loadRequesterContacts } from './requesterContacts';

const jsonResponse = (body, ok = true) => Promise.resolve({ ok, json: () => Promise.resolve(body) });

describe('loadRequesterContacts', () => {
    afterEach(() => {
        delete global.fetch;
    });

    test('looks each requester up once and keys the contacts by user id', async () => {
        global.fetch = jest.fn((url) => jsonResponse({
            id: 7, name: 'Nimal', email: 'nimal@example.com', phone: '0771234567', password: 'must-not-leak',
        }));

        const contacts = await loadRequesterContacts([7, 7, 7], 'token-abc');

        expect(global.fetch).toHaveBeenCalledTimes(1);
        expect(global.fetch.mock.calls[0][0]).toMatch(/\/auth\/v1\/users\/7$/);
        expect(global.fetch.mock.calls[0][1].headers.Authorization).toBe('Bearer token-abc');
        expect(contacts).toEqual({ 7: { name: 'Nimal', email: 'nimal@example.com', phone: '0771234567' } });
    });

    test('ignores ids that are not integers', async () => {
        global.fetch = jest.fn();

        const contacts = await loadRequesterContacts([undefined, null, 'x'], 'token');

        expect(global.fetch).not.toHaveBeenCalled();
        expect(contacts).toEqual({});
    });

    test('leaves out a requester whose lookup fails without losing the others', async () => {
        global.fetch = jest.fn((url) => {
            if (url.endsWith('/1')) return jsonResponse({ message: 'User not found.' }, false);
            if (url.endsWith('/2')) return Promise.reject(new Error('network down'));
            return jsonResponse({ name: 'Kamal', email: 'kamal@example.com', phone: null });
        });

        const contacts = await loadRequesterContacts([1, 2, 3], 'token');

        expect(contacts).toEqual({ 3: { name: 'Kamal', email: 'kamal@example.com', phone: null } });
    });
});
