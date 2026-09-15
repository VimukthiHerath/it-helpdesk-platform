import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getStoredToken, getUserId, isAdmin } from '../shared/authToken';
import './ticketAssignments.css';

const TICKETS_URL = `${process.env.REACT_APP_TICKET_API_URL}/api/ticket`;
const ASSIGNMENTS_URL = `${process.env.REACT_APP_ASSIGNMENT_API_URL}/api/assignments`;
const AGENTS_URL = `${process.env.REACT_APP_ASSIGNMENT_API_URL}/api/assignments/agents`;

const urgencyLabels = ['Within 1 hour', 'Within 6 hours', 'Within 12 hours', 'Within 24 hours'];

// Indexed by TicketStatus's raw int value - unassigned/assigned are
// system-managed (round-robin, reassignment); in_progress/resolved/closed
// are the three an agent or admin can set through this page (SCRUM-17 AC1).
const statusLabels = ['Unassigned', 'Assigned', 'Resolved', 'In Progress', 'Closed'];
const statusOptions = [
    { value: 3, label: 'In Progress' },
    { value: 2, label: 'Resolved' },
    { value: 4, label: 'Closed' },
];

const formatLabel = (value, labels) => (typeof value === 'number' && labels[value]) ? labels[value] : 'Unknown';

const TicketAssignments = () => {
    const [tickets, setTickets] = useState([]);
    const [assignments, setAssignments] = useState([]);
    const [agents, setAgents] = useState([]);
    const [state, setState] = useState({ loading: true, error: '' });
    const [selectedAgent, setSelectedAgent] = useState({});
    const [rowErrors, setRowErrors] = useState({});
    const [busyTicketId, setBusyTicketId] = useState(null);
    const [selectedStatus, setSelectedStatus] = useState({});
    const [statusRowErrors, setStatusRowErrors] = useState({});
    const [busyStatusTicketId, setBusyStatusTicketId] = useState(null);
    const navigate = useNavigate();
    const myUserId = getUserId();

    const loadAll = async () => {
        const token = getStoredToken();
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        try {
            const headers = { Authorization: `Bearer ${token}` };
            const [ticketsRes, assignmentsRes, agentsRes] = await Promise.all([
                fetch(TICKETS_URL, { headers }),
                fetch(ASSIGNMENTS_URL, { headers }),
                fetch(AGENTS_URL, { headers }),
            ]);

            if ([ticketsRes.status, assignmentsRes.status, agentsRes.status].includes(401)) {
                navigate('/login', { replace: true });
                return;
            }

            const [ticketsData, assignmentsData, agentsData] = await Promise.all([
                ticketsRes.json().catch(() => ([])),
                assignmentsRes.json().catch(() => ([])),
                agentsRes.json().catch(() => ([])),
            ]);

            if (!ticketsRes.ok) throw new Error(ticketsData?.message || 'Unable to load tickets.');
            if (!assignmentsRes.ok) throw new Error(assignmentsData?.message || 'Unable to load assignments.');
            if (!agentsRes.ok) throw new Error(agentsData?.message || 'Unable to load the agent rotation.');

            setTickets(Array.isArray(ticketsData) ? ticketsData : []);
            setAssignments(Array.isArray(assignmentsData) ? assignmentsData : []);
            setAgents(Array.isArray(agentsData) ? agentsData : []);
            setState({ loading: false, error: '' });
        } catch (error) {
            setState({ loading: false, error: error.message || 'Unable to load tickets.' });
        }
    };

    useEffect(() => {
        loadAll();
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const assignmentFor = (ticketId) => assignments.find((assignment) => assignment.ticketId === ticketId);

    const setRowError = (ticketId, message) => setRowErrors((current) => ({ ...current, [ticketId]: message }));

    const reassign = async (ticketId) => {
        const newAgentUserId = Number(selectedAgent[ticketId]);
        if (!newAgentUserId) {
            setRowError(ticketId, 'Choose an agent first.');
            return;
        }

        const token = getStoredToken();
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        setBusyTicketId(ticketId);
        setRowError(ticketId, '');
        try {
            const response = await fetch(`${ASSIGNMENTS_URL}/${ticketId}/reassign`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
                body: JSON.stringify({ newAgentUserId }),
            });
            const data = await response.json().catch(() => ({}));

            if (response.status === 401) {
                navigate('/login', { replace: true });
                return;
            }
            if (!response.ok) throw new Error(data?.message || 'Unable to reassign this ticket.');

            setAssignments((current) => [...current.filter((a) => a.ticketId !== ticketId), data]);
            setSelectedAgent((current) => ({ ...current, [ticketId]: '' }));
        } catch (error) {
            setRowError(ticketId, error.message || 'Unable to reassign this ticket.');
        } finally {
            setBusyTicketId(null);
        }
    };

    const setStatusRowError = (ticketId, message) => setStatusRowErrors((current) => ({ ...current, [ticketId]: message }));

    // SCRUM-17 AC2: mirrors the backend's own ownership check exactly -
    // an admin can always act, an agent only on a ticket currently
    // assigned to them (Ticket.Api's own AssignedTo, not Assignment.Api's
    // copy - same field the backend checks against).
    const canChangeStatus = (ticket) => isAdmin() || ticket.assignedTo === myUserId;

    const updateStatus = async (ticketId) => {
        const newStatus = Number(selectedStatus[ticketId]);
        if (!newStatus) {
            setStatusRowError(ticketId, 'Choose a status first.');
            return;
        }

        const token = getStoredToken();
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        setBusyStatusTicketId(ticketId);
        setStatusRowError(ticketId, '');
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

            setTickets((current) => current.map((ticket) => (ticket.id === ticketId ? { ...ticket, status: data.status } : ticket)));
            setSelectedStatus((current) => ({ ...current, [ticketId]: '' }));
        } catch (error) {
            setStatusRowError(ticketId, error.message || 'Unable to update this ticket\'s status.');
        } finally {
            setBusyStatusTicketId(null);
        }
    };

    return (
        <main className="assignments-page">
            <div className="app-bar">
                <span className="app-bar__brand">IT Helpdesk</span>
                <div className="app-bar__actions">
                    <button type="button" className="btn btn--ghost" onClick={() => navigate('/')}>
                        Back to dashboard
                    </button>
                </div>
            </div>

            <header className="assignments-page__header">
                <p className="eyebrow">Support desk / All tickets</p>
                <h1>Ticket assignments</h1>
                <p className="assignments-page__summary">See every ticket's current agent and reassign it if needed.</p>
            </header>

            {state.loading && <div className="assignments-message panel">Loading tickets...</div>}
            {!state.loading && state.error && (
                <div className="assignments-message assignments-message--error panel" role="alert">{state.error}</div>
            )}
            {!state.loading && !state.error && tickets.length === 0 && (
                <div className="assignments-message panel">
                    <strong>No tickets yet</strong>
                    <span>Submitted tickets will show up here.</span>
                </div>
            )}

            {!state.loading && !state.error && tickets.length > 0 && (
                <section className="assignments-list panel" aria-label="All tickets">
                    <div className="assignments-table-wrap">
                        <table className="assignments-table">
                            <thead>
                                <tr>
                                    <th>Ticket</th>
                                    <th>Issue</th>
                                    <th>Urgency</th>
                                    <th>Status</th>
                                    <th>Assigned to</th>
                                    <th />
                                </tr>
                            </thead>
                            <tbody>
                                {tickets.map((ticket) => {
                                    const assignment = assignmentFor(ticket.id);
                                    const isBusy = busyTicketId === ticket.id;

                                    return (
                                        <tr key={ticket.id} data-ticket-id={ticket.id}>
                                            <td>#{ticket.id}</td>
                                            <td>{ticket.issueType || 'General request'}</td>
                                            <td>{formatLabel(ticket.urgency, urgencyLabels)}</td>
                                            <td>
                                                {canChangeStatus(ticket) ? (
                                                    <div className="assignments-table__actions">
                                                        <span className="assignments-table__current-status">{formatLabel(ticket.status, statusLabels)}</span>
                                                        <div className="assignments-table__actions-row">
                                                            <select
                                                                className="input"
                                                                value={selectedStatus[ticket.id] || ''}
                                                                onChange={(event) => setSelectedStatus((current) => ({ ...current, [ticket.id]: event.target.value }))}
                                                            >
                                                                <option value="">Change to...</option>
                                                                {statusOptions
                                                                    .filter((option) => option.value !== ticket.status)
                                                                    .map((option) => (
                                                                        <option key={option.value} value={option.value}>{option.label}</option>
                                                                    ))}
                                                            </select>
                                                            <button type="button" className="btn btn--secondary" disabled={busyStatusTicketId === ticket.id} onClick={() => updateStatus(ticket.id)}>
                                                                {busyStatusTicketId === ticket.id ? 'Updating...' : 'Update'}
                                                            </button>
                                                        </div>
                                                        {statusRowErrors[ticket.id] && <span className="error-text">{statusRowErrors[ticket.id]}</span>}
                                                    </div>
                                                ) : (
                                                    formatLabel(ticket.status, statusLabels)
                                                )}
                                            </td>
                                            <td>{assignment ? `User ID ${assignment.agentUserId}` : 'Unassigned'}</td>
                                            <td>
                                                {/* Reassign is Administrator-only - agents can see who has a
                                                    ticket but can no longer move it off themselves or anyone else. */}
                                                {isAdmin() && assignment && (
                                                    <div className="assignments-table__actions">
                                                        <div className="assignments-table__actions-row">
                                                            <select
                                                                className="input"
                                                                value={selectedAgent[ticket.id] || ''}
                                                                onChange={(event) => setSelectedAgent((current) => ({ ...current, [ticket.id]: event.target.value }))}
                                                            >
                                                                <option value="">Choose agent...</option>
                                                                {agents
                                                                    .filter((agent) => agent.userId !== assignment.agentUserId)
                                                                    .map((agent) => (
                                                                        <option key={agent.id} value={agent.userId}>User ID {agent.userId}</option>
                                                                    ))}
                                                            </select>
                                                            <button type="button" className="btn btn--secondary" disabled={isBusy} onClick={() => reassign(ticket.id)}>
                                                                {isBusy ? 'Reassigning...' : 'Reassign'}
                                                            </button>
                                                        </div>
                                                        {rowErrors[ticket.id] && <span className="error-text">{rowErrors[ticket.id]}</span>}
                                                    </div>
                                                )}
                                                {isAdmin() && !assignment && (
                                                    <span className="assignments-table__unassigned-note">Not yet assigned</span>
                                                )}
                                            </td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>
                </section>
            )}
        </main>
    );
};

export default TicketAssignments;
