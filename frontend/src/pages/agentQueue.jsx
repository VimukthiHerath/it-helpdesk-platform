import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getStoredToken } from '../shared/authToken';
import './agentQueue.css';

const ASSIGNMENT_QUEUE_URL = `${process.env.REACT_APP_ASSIGNMENT_API_URL}/api/assignments/queue`;
const TICKETS_URL = `${process.env.REACT_APP_TICKET_API_URL}/api/ticket`;

// TicketStatus ordinals (Ticket.Api/Model/TicketStatus.cs) - only the two an
// agent can set from here. No "in progress" button: once the planned
// assignment timer exists there's no separate step for the agent to record,
// so it wasn't worth building a control for now.
const RESOLVED_STATUS = 2;
const CLOSED_STATUS = 4;

const urgencyLabels = ['Within 1 hour', 'Within 6 hours', 'Within 12 hours', 'Within 24 hours'];
// Queue is already sorted most-urgent-first by the API; badges just repeat
// that signal visually. Only the existing theme badge variants are reused.
const urgencyBadgeClass = ['badge--error', 'badge--sage', 'badge--neutral', 'badge--neutral'];

const formatDate = (value) => {
    if (!value) return 'Date unavailable';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? 'Date unavailable' : date.toLocaleString(undefined, {
        year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit',
    });
};

const AgentQueue = () => {
    const [tickets, setTickets] = useState([]);
    const [state, setState] = useState({ loading: true, error: '' });
    const [rowErrors, setRowErrors] = useState({});
    const [busyTicketId, setBusyTicketId] = useState(null);
    const navigate = useNavigate();

    useEffect(() => {
        const loadQueue = async () => {
            const token = localStorage.getItem('token');
            if (!token) {
                navigate('/login', { replace: true });
                return;
            }

            try {
                const response = await fetch(ASSIGNMENT_QUEUE_URL, {
                    headers: { Authorization: `Bearer ${token}` },
                });
                const data = await response.json().catch(() => ([]));

                if (response.status === 401) {
                    localStorage.removeItem('token');
                    navigate('/login', { replace: true });
                    return;
                }
                if (!response.ok) throw new Error(data?.message || 'Unable to load your queue.');

                setTickets(Array.isArray(data) ? data : []);
            } catch (error) {
                setState({ loading: false, error: error.message || 'Unable to load your queue.' });
                return;
            }

            setState({ loading: false, error: '' });
        };

        loadQueue();
    }, [navigate]);

    const setRowError = (ticketId, message) => setRowErrors((current) => ({ ...current, [ticketId]: message }));

    // Ownership is implicit here - the queue only ever lists tickets already
    // assigned to the caller, so the backend's own Agent-must-own-it check
    // (TicketStatusService) always passes for anything shown on this page.
    const setStatus = async (ticketId, newStatus) => {
        const token = getStoredToken();
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        setBusyTicketId(ticketId);
        setRowError(ticketId, '');
        try {
            const response = await fetch(`${TICKETS_URL}/${ticketId}/status`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
                body: JSON.stringify({ newStatus }),
            });
            const data = await response.json().catch(() => ({}));

            if (response.status === 401) {
                navigate('/login', { replace: true });
                return;
            }
            if (!response.ok) throw new Error(data?.message || 'Unable to update this ticket\'s status.');

            // Resolved/closed tickets don't belong in an active queue -
            // dropping the row here is what makes "resolve it" feel done,
            // rather than leaving a stale item the agent has to ignore.
            setTickets((current) => current.filter((ticket) => ticket.ticketId !== ticketId));
        } catch (error) {
            setRowError(ticketId, error.message || 'Unable to update this ticket\'s status.');
        } finally {
            setBusyTicketId(null);
        }
    };

    return (
        <main className="queue-page">
            <div className="app-bar">
                <span className="app-bar__brand">IT Helpdesk</span>
                <div className="app-bar__actions">
                    <button type="button" className="btn btn--ghost" onClick={() => navigate('/')}>
                        Back to dashboard
                    </button>
                </div>
            </div>

            <header className="queue-page__header">
                <p className="eyebrow">Support desk / Your workload</p>
                <h1>My queue</h1>
                <p className="queue-page__summary">Tickets assigned to you, most urgent first.</p>
            </header>

            {state.loading && <div className="queue-message panel">Loading your queue...</div>}
            {!state.loading && state.error && (
                <div className="queue-message queue-message--error panel" role="alert">{state.error}</div>
            )}
            {!state.loading && !state.error && tickets.length === 0 && (
                <div className="queue-message panel">
                    <strong>Queue is empty</strong>
                    <span>Tickets assigned to you will appear here.</span>
                </div>
            )}

            {!state.loading && !state.error && tickets.length > 0 && (
                <section className="queue-list panel" aria-label="Your assigned tickets">
                    <div className="panel__titlebar">
                        <span>Assigned tickets</span>
                        <span>{tickets.length}</span>
                    </div>
                    {tickets.map((ticket) => (
                        <div className="queue-list-item" key={ticket.ticketId}>
                            <div className="queue-list-item__row">
                                <span className="queue-list-item__id">Ticket #{ticket.ticketId}</span>
                                <span className={`badge ${urgencyBadgeClass[ticket.urgency] || 'badge--neutral'}`}>
                                    {urgencyLabels[ticket.urgency] || 'Urgency unknown'}
                                </span>
                                <span className="queue-list-item__date">Assigned {formatDate(ticket.assignedAtUtc)}</span>
                                <div className="queue-list-item__actions">
                                    <button
                                        type="button"
                                        className="btn btn--primary"
                                        disabled={busyTicketId === ticket.ticketId}
                                        onClick={() => setStatus(ticket.ticketId, RESOLVED_STATUS)}
                                    >
                                        Resolved
                                    </button>
                                    <button
                                        type="button"
                                        className="btn btn--danger"
                                        disabled={busyTicketId === ticket.ticketId}
                                        onClick={() => setStatus(ticket.ticketId, CLOSED_STATUS)}
                                    >
                                        Closed
                                    </button>
                                </div>
                            </div>
                            {rowErrors[ticket.ticketId] && <span className="error-text">{rowErrors[ticket.ticketId]}</span>}
                        </div>
                    ))}
                </section>
            )}
        </main>
    );
};

export default AgentQueue;
