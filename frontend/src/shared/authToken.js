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

// ASP.NET's JwtSecurityTokenHandler writes ClaimTypes.Role out under its
// full claim-type URI, not the short "role" name.
const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

export const getUserRole = () => {
    const payload = decodeToken(getStoredToken());
    return payload?.[ROLE_CLAIM] ?? null;
};

// The "sub" claim is a registered JWT claim, written under its short name
// as-is (unlike the role claim above, which gets expanded to a long URI).
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
