export const decodeToken = (token) => {
    if (!token) return null;

    const parts = token.split('.');
    if (parts.length !== 3) return null;

    try {
        return JSON.parse(atob(parts[1].replace(/-/g, '+').replace(/_/g, '/')));
    } catch {
        return null;
    }
};

export const getStoredToken = () => localStorage.getItem('token');

// Auth.Api now builds the token with SecurityTokenDescriptor, which writes the
// role under the short name "role". Before the RS256 change the token was
// built from a plain claim list, which wrote the full claim-type URI instead.
// Read the short name first and keep the long one so a token issued by the
// older build still works until it expires.
const LEGACY_ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

export const getUserRole = () => {
    const payload = decodeToken(getStoredToken());
    return payload?.role ?? payload?.[LEGACY_ROLE_CLAIM] ?? null;
};

// The "sub" claim is a registered JWT claim, written under its short name.
export const getUserId = () => {
    const payload = decodeToken(getStoredToken());
    const sub = payload?.sub;
    return sub ? Number(sub) : null;
};

export const isAdmin = () => getUserRole() === 'Administrator';

export const isAgent = () => getUserRole() === 'Agent';

export const getUserEmail = () => {
    const payload = decodeToken(getStoredToken());
    return payload?.email ?? null;
};
