import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getStoredToken, isAdmin } from '../shared/authToken';
import Navbar from '../components/layout/Navbar';
import RequesterContact from '../features/ticket/components/RequesterContact';
import { loadRequesterContacts } from '../shared/requesterContacts';
import './ticketAssignments.css';

const TICKETS_URL = `${process.env.REACT_APP_TICKET_API_URL || process.env.REACT_APP_API_GATEWAY_URL}/ticket/v1`;
const ASSIGNMENTS_URL = `${process.env.REACT_APP_ASSIGNMENT_API_URL || process.env.REACT_APP_API_GATEWAY_URL}/assignment/v1`;
const AGENTS_URL = `${process.env.REACT_APP_ASSIGNMENT_API_URL || process.env.REACT_APP_API_GATEWAY_URL}/assignment/v1/agents`;

const urgencyLabels = ['Within 1 hour', 'Within 6 hours', 'Within 12 hours', 'Within 24 hours'];

// Indexed by TicketStatus's raw int value (Ticket.Api/Model/TicketStatus.cs).
// Display only here - no control to change it, unlike the queue page.
const statusLabels = ['Unassigned', 'Assigned', 'Resolved', 'In Progress', 'Closed'];

// Row tint by status, requested so resolved/closed/assigned tickets are
// visually distinguishable at a glance. Unassigned/In progress have no
// specific color asked for, so they stay untinted.
const statusRowClass = ['', 'assignments-table__row--assigned', 'assignments-table__row--resolved', '', 'assignments-table__row--closed'];

const formatLabel = (value, labels) => (typeof value === 'number' && labels[value]) ? labels[value] : 'Unknown';

// Read-only for everyone except the Reassign column, which only an
// Administrator sees and can act on - reassigning is Administrator-only at
// the API too (see role-based-authorization.md), agents just view here.
const TicketAssignments = () => {
    const [tickets, setTickets] = useState([]);
    const [assignments, setAssignments] = useState([]);
    const [agents, setAgents] = useState([]);
    const [contacts, setContacts] = useState({});
    const [state, setState] = useState({ loading: true, error: '' });
    const [selectedAgent, setSelectedAgent] = useState({});
    const [rowErrors, setRowErrors] = useState({});
    const [busyTicketId, setBusyTicketId] = useState(null);
    const navigate = useNavigate();
    const admin = isAdmin();

    const loadAll = async () => {
        const token = getStoredToken();
        if (!token) {
            void navigate('/login', { replace: true });
            return;
        }

        try {
            const headers = { Authorization: `Bearer ${token}` };
            const requests = [
                fetch(TICKETS_URL, { headers }),
                fetch(ASSIGNMENTS_URL, { headers }),
            ];
            // Only an admin can reassign, so only an admin needs the rotation list.
            if (admin) requests.push(fetch(AGENTS_URL, { headers }));

            const responses = await Promise.all(requests);
            const [ticketsRes, assignmentsRes, agentsRes] = responses;

            if (responses.some((res) => res.status === 401)) {
                void navigate('/login', { replace: true });
                return;
            }

            const bodies = await Promise.all(responses.map((res) => res.json().catch(() => ([]))));
            const [ticketsData, assignmentsData, agentsData] = bodies;

            if (!ticketsRes.ok) throw new Error(ticketsData?.message || 'Unable to load tickets.');
            if (!assignmentsRes.ok) throw new Error(assignmentsData?.message || 'Unable to load assignments.');
            if (admin && !agentsRes.ok) throw new Error(agentsData?.message || 'Unable to load the agent rotation.');

            setTickets(Array.isArray(ticketsData) ? ticketsData : []);
            setAssignments(Array.isArray(assignmentsData) ? assignmentsData : []);
            if (admin) setAgents(Array.isArray(agentsData) ? agentsData : []);
            setState({ loading: false, error: '' });

            // Enrichment only - the table is usable without it, so it loads
            // after the page is already showing and never blocks or fails it.
            const requesterIds = (Array.isArray(ticketsData) ? ticketsData : []).map((ticket) => ticket.createdBy);
            setContacts(await loadRequesterContacts(requesterIds, token));
        } catch (error) {
            setState({ loading: false, error: error.message || 'Unable to load tickets.' });
        }
    };

    useEffect(() => {
        void loadAll();
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
            void navigate('/login', { replace: true });
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
                void navigate('/login', { replace: true });
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

    return (
        <main className="assignments-page">
            <Navbar />

            <div className="page-header">
                <h1>All tickets</h1>
            </div>

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
                        <table className={`assignments-table${admin ? ' assignments-table--admin' : ''}`}>
                            <thead>
                                <tr>
                                    <th>Ticket</th>
                                    <th>Issue</th>
                                    <th className="assignments-table__description">Description</th>
                                    <th>Urgency</th>
                                    <th>Status</th>
                                    <th>Assigned to</th>
                                    <th className="assignments-table__requester">Requester</th>
                                    {admin && <th>Reassign</th>}
                                </tr>
                            </thead>
                            <tbody>
                                {tickets.map((ticket) => {
                                    const assignment = assignmentFor(ticket.id);
                                    const isBusy = busyTicketId === ticket.id;

                                    return (
                                        <tr key={ticket.id} data-ticket-id={ticket.id} className={statusRowClass[ticket.status] || ''}>
                                            <td>#{ticket.id}</td>
                                            <td>{ticket.issueType || 'General request'}</td>
                                            <td className="assignments-table__description">{ticket.description || '—'}</td>
                                            <td>{formatLabel(ticket.urgency, urgencyLabels)}</td>
                                            <td>{formatLabel(ticket.status, statusLabels)}</td>
                                            <td>{assignment ? `User ID ${assignment.agentUserId}` : 'Unassigned'}</td>
                                            <td className="assignments-table__requester">
                                                <RequesterContact contact={contacts[ticket.createdBy]} />
                                            </td>
                                            {admin && (
                                                <td>
                                                    {assignment ? (
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
                                                    ) : (
                                                        <span className="assignments-table__unassigned-note">Not yet assigned</span>
                                                    )}
                                                </td>
                                            )}
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
