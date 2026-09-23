import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getStoredToken } from '../../../shared/authToken';
import './AgentRotationPanel.css';

const AGENTS_URL = `${process.env.REACT_APP_ASSIGNMENT_API_URL}/api/assignments/agents`;
const USERS_URL = `${process.env.REACT_APP_AUTH_API_URL}/api/auth/users`;

const AgentRotationPanel = () => {
    const [agents, setAgents] = useState([]);
    const [availableAgents, setAvailableAgents] = useState([]);
    const [state, setState] = useState({ loading: true, error: '' });
    const [userIdInput, setUserIdInput] = useState('');
    const [formError, setFormError] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);
    const navigate = useNavigate();

    const loadAgents = async () => {
        const token = getStoredToken();
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        try {
            const response = await fetch(AGENTS_URL, {
                headers: { Authorization: `Bearer ${token}` },
            });
            const data = await response.json().catch(() => ([]));

            if (response.status === 401) {
                navigate('/login', { replace: true });
                return;
            }
            if (!response.ok) throw new Error(data?.message || 'Unable to load the agent rotation.');

            const usersResponse = await fetch(USERS_URL, {
                headers: { Authorization: `Bearer ${token}` },
            });
            const usersData = await usersResponse.json().catch(() => ([]));
            if (usersResponse.ok) {
                setAvailableAgents(usersData.filter(u => u.role === 2 && u.isActive));
            }

            setAgents(Array.isArray(data) ? data : []);
            setState({ loading: false, error: '' });
        } catch (error) {
            setState({ loading: false, error: error.message || 'Unable to load the agent rotation.' });
        }
    };

    useEffect(() => {
        loadAgents();
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const handleSubmit = async (event) => {
        event.preventDefault();
        setFormError('');

        const userId = Number(userIdInput);
        if (!userIdInput || !Number.isInteger(userId) || userId <= 0) {
            setFormError('Please select an agent from the list.');
            return;
        }

        const token = getStoredToken();
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        setIsSubmitting(true);
        try {
            const response = await fetch(AGENTS_URL, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    Authorization: `Bearer ${token}`,
                },
                body: JSON.stringify({ userId }),
            });

            if (response.status === 401) {
                navigate('/login', { replace: true });
                return;
            }
            if (response.status === 403) {
                setFormError('Your account no longer has administrator access.');
                return;
            }
            if (response.status === 409) {
                setFormError('That user is already in the rotation.');
                return;
            }

            const data = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(data?.message || 'Unable to add that agent.');

            setUserIdInput('');
            await loadAgents();
        } catch (error) {
            setFormError(error.message || 'Unable to add that agent.');
        } finally {
            setIsSubmitting(false);
        }
    };

    return (
        <section className="admin-create-panel panel" aria-labelledby="agent-rotation-title">
            <div className="panel__titlebar">
                <span id="agent-rotation-title">Agent rotation</span>
                <span>Round-robin</span>
            </div>

            <div className="panel__body">
                <p className="admin-create-panel__intro">
                    Tickets are assigned to agents in this order. Select an active agent
                    from the list below to put them into the rotation.
                </p>

                {state.loading && <p>Loading rotation...</p>}
                {!state.loading && state.error && <span className="error-text">{state.error}</span>}

                {!state.loading && !state.error && (
                    <ol className="agent-rotation-list">
                        {agents.length === 0 && <li className="agent-rotation-list__empty">No agents in the rotation yet.</li>}
                        {agents.map((agent) => (
                            <li key={agent.id}>
                                <span>User ID {agent.userId}</span>
                                <span className="badge badge--neutral">Slot {agent.displayOrder}</span>
                            </li>
                        ))}
                    </ol>
                )}

                <form className="agent-rotation-form" onSubmit={handleSubmit} noValidate>
                    <div className="field">
                        <label htmlFor="rotation-user-id">Agent to add</label>
                        <select
                            id="rotation-user-id"
                            value={userIdInput}
                            onChange={(event) => { setUserIdInput(event.target.value); setFormError(''); }}
                            className={formError ? 'input input--error' : 'input'}
                        >
                            <option value="">Select an agent...</option>
                            {availableAgents.map((agent) => (
                                <option key={agent.id} value={agent.id}>
                                    {agent.name} (ID {agent.id})
                                </option>
                            ))}
                        </select>
                        {formError && <span className="error-text">{formError}</span>}
                    </div>
                    <button type="submit" className="btn btn--secondary" disabled={isSubmitting}>
                        {isSubmitting ? 'Adding...' : 'Add to rotation'}
                    </button>
                </form>
            </div>
        </section>
    );
};

export default AgentRotationPanel;
