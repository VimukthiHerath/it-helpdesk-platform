const USERS_URL = `${process.env.REACT_APP_AUTH_API_URL || process.env.REACT_APP_API_GATEWAY_URL}/auth/v1/users`;

// Agents work a ticket by contacting whoever raised it, so the queue and the
// all-tickets page need that person's name, email and phone, not just the
// numeric createdBy id the ticket carries. Returns { [userId]: contact } for
// each distinct id; an id whose lookup fails is left out so its row can say
// "unavailable" instead of one bad user blanking the whole page.
export const loadRequesterContacts = async (userIds, token) => {
    const distinctIds = [...new Set(userIds.filter((id) => Number.isInteger(id)))];

    const lookups = await Promise.all(distinctIds.map(async (id) => {
        try {
            const response = await fetch(`${USERS_URL}/${id}`, { headers: { Authorization: `Bearer ${token}` } });
            if (!response.ok) return null;
            const user = await response.json();
            return [id, { name: user.name, email: user.email, phone: user.phone }];
        } catch {
            return null;
        }
    }));

    return Object.fromEntries(lookups.filter(Boolean));
};
