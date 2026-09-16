import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getStoredToken } from '../../../shared/authToken';
import './UserListPanel.css';

const USERS_URL = `${process.env.REACT_APP_AUTH_API_URL}/api/auth/users`;

const roleOptions = [
    { value: 1, label: 'Employee' },
    { value: 2, label: 'Agent' },
    { value: 3, label: 'Administrator' },
];

const roleLabel = (value) => roleOptions.find((option) => option.value === value)?.label || value;

// Employee/Agent/Administrator each get a distinct badge color so a role is
// recognizable at a glance down a long list, not just legible on close read.
const roleBadgeClass = { 1: 'badge--neutral', 2: 'badge--amber', 3: 'badge--sage' };

// Row tint by role - Employees are the common case and stay plain; Agent and
// Administrator rows get their own tone so staff accounts stand out from the
// crowd, not just an alternating stripe with no meaning behind it.
const roleRowClass = { 2: 'user-table__row--agent', 3: 'user-table__row--admin' };

const UserListPanel = () => {
    const [users, setUsers] = useState([]);
    const [state, setState] = useState({ loading: true, error: '' });
    const [editingId, setEditingId] = useState(null);
    const [editForm, setEditForm] = useState({ name: '', email: '', role: 1 });
    const [rowErrors, setRowErrors] = useState({});
    const [busyId, setBusyId] = useState(null);
    const navigate = useNavigate();

    const loadUsers = async () => {
        const token = getStoredToken();
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        try {
            const response = await fetch(USERS_URL, { headers: { Authorization: `Bearer ${token}` } });
            const data = await response.json().catch(() => ([]));

            if (response.status === 401) {
                navigate('/login', { replace: true });
                return;
            }
            if (!response.ok) throw new Error(data?.message || 'Unable to load users.');

            setUsers(Array.isArray(data) ? data : []);
            setState({ loading: false, error: '' });
        } catch (error) {
            setState({ loading: false, error: error.message || 'Unable to load users.' });
        }
    };

    useEffect(() => {
        loadUsers();
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const setRowError = (id, message) => setRowErrors((current) => ({ ...current, [id]: message }));

    const startEdit = (user) => {
        setEditingId(user.id);
        setEditForm({ name: user.name, email: user.email, role: user.role });
        setRowError(user.id, '');
    };

    const cancelEdit = (id) => {
        setEditingId(null);
        setRowError(id, '');
    };

    const saveEdit = async (id) => {
        const token = getStoredToken();
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        setBusyId(id);
        setRowError(id, '');
        try {
            const response = await fetch(`${USERS_URL}/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
                body: JSON.stringify(editForm),
            });
            const data = await response.json().catch(() => ({}));

            if (response.status === 401) {
                navigate('/login', { replace: true });
                return;
            }
            if (!response.ok) throw new Error(data?.message || 'Unable to update user.');

            setUsers((current) => current.map((user) => (user.id === id ? data : user)));
            setEditingId(null);
        } catch (error) {
            setRowError(id, error.message || 'Unable to update user.');
        } finally {
            setBusyId(null);
        }
    };

    const deactivate = async (user) => {
        if (!window.confirm(`Deactivate ${user.name}? They will no longer be able to log in.`)) {
            return;
        }

        const token = getStoredToken();
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        setBusyId(user.id);
        setRowError(user.id, '');
        try {
            const response = await fetch(`${USERS_URL}/${user.id}/deactivate`, {
                method: 'PATCH',
                headers: { Authorization: `Bearer ${token}` },
            });
            const data = await response.json().catch(() => ({}));

            if (response.status === 401) {
                navigate('/login', { replace: true });
                return;
            }
            if (!response.ok) throw new Error(data?.message || 'Unable to deactivate user.');

            setUsers((current) => current.map((existing) => (existing.id === user.id ? data : existing)));
        } catch (error) {
            setRowError(user.id, error.message || 'Unable to deactivate user.');
        } finally {
            setBusyId(null);
        }
    };

    return (
        <section className="accounts-panel panel" aria-labelledby="user-list-title">
            <div className="panel__titlebar">
                <span id="user-list-title">All accounts</span>
                <span>{users.length}</span>
            </div>

            <div className="panel__body">
                {state.loading && <p>Loading users...</p>}
                {!state.loading && state.error && <span className="error-text">{state.error}</span>}

                {!state.loading && !state.error && (
                    <div className="user-table-wrap">
                        <table className="user-table">
                            <thead>
                                <tr>
                                    <th className="user-table__id">ID</th>
                                    <th className="user-table__name">Name</th>
                                    <th className="user-table__email">Email</th>
                                    <th>Role</th>
                                    <th>Status</th>
                                    <th />
                                </tr>
                            </thead>
                            <tbody>
                                {users.map((user) => {
                                    const isEditing = editingId === user.id;
                                    const isBusy = busyId === user.id;

                                    return (
                                        <tr key={user.id} data-user-id={user.id} className={roleRowClass[user.role] || ''}>
                                            <td className="user-table__id">{user.id}</td>
                                            <td className="user-table__name">
                                                {isEditing ? (
                                                    <input
                                                        className="input"
                                                        value={editForm.name}
                                                        onChange={(event) => setEditForm((form) => ({ ...form, name: event.target.value }))}
                                                    />
                                                ) : user.name}
                                            </td>
                                            <td className="user-table__email">
                                                {isEditing ? (
                                                    <input
                                                        className="input"
                                                        type="email"
                                                        value={editForm.email}
                                                        onChange={(event) => setEditForm((form) => ({ ...form, email: event.target.value }))}
                                                    />
                                                ) : user.email}
                                            </td>
                                            <td>
                                                {isEditing ? (
                                                    <select
                                                        className="input"
                                                        value={editForm.role}
                                                        onChange={(event) => setEditForm((form) => ({ ...form, role: Number(event.target.value) }))}
                                                    >
                                                        {roleOptions.map((option) => (
                                                            <option key={option.value} value={option.value}>{option.label}</option>
                                                        ))}
                                                    </select>
                                                ) : (
                                                    <span className={`badge ${roleBadgeClass[user.role] || 'badge--neutral'}`}>
                                                        {roleLabel(user.role)}
                                                    </span>
                                                )}
                                            </td>
                                            <td>
                                                <span className={`badge ${user.isActive ? 'badge--sage' : 'badge--neutral'}`}>
                                                    {user.isActive ? 'Active' : 'Inactive'}
                                                </span>
                                            </td>
                                            <td>
                                                <div className="user-table__actions">
                                                    <div className="user-table__actions-row">
                                                        {isEditing ? (
                                                            <>
                                                                <button type="button" className="btn btn--primary" disabled={isBusy} onClick={() => saveEdit(user.id)}>
                                                                    {isBusy ? 'Saving...' : 'Save'}
                                                                </button>
                                                                <button type="button" className="btn btn--ghost" onClick={() => cancelEdit(user.id)}>
                                                                    Cancel
                                                                </button>
                                                            </>
                                                        ) : (
                                                            <>
                                                                <button type="button" className="btn btn--secondary" onClick={() => startEdit(user)}>
                                                                    Edit
                                                                </button>
                                                                {user.isActive && (
                                                                    <button type="button" className="btn btn--ghost" disabled={isBusy} onClick={() => deactivate(user)}>
                                                                        {isBusy ? 'Working...' : 'Deactivate'}
                                                                    </button>
                                                                )}
                                                            </>
                                                        )}
                                                    </div>
                                                    {rowErrors[user.id] && <span className="error-text">{rowErrors[user.id]}</span>}
                                                </div>
                                            </td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>
        </section>
    );
};

export default UserListPanel;
