import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getStoredToken } from '../shared/authToken';
import './ticketAssignments.css';

const TICKETS_URL = `${process.env.REACT_APP_TICKET_API_URL}/api/ticket`;
const ASSIGNMENTS_URL = `${process.env.REACT_APP_ASSIGNMENT_API_URL}/api/assignments`;

const urgencyLabels = ['Within 1 hour', 'Within 6 hours', 'Within 12 hours', 'Within 24 hours'];

const formatLabel = (value, labels) => (typeof value === 'number' && labels[value]) ? labels[value] : 'Unknown';

// Read-only overview by design: no status changes, no reassignment here -
// an agent should only be able to see who currently has each ticket, and an
// admin's reassignment tool lives elsewhere. Just Ticket / Issue / Urgency /
// Assigned to, one plain line per row.
const TicketAssignments = () => {
    const [tickets, setTickets] = useState([]);
    const [assignments, setAssignments] = useState([]);
    const [state, setState] = useState({ loading: true, error: '' });
    const navigate = useNavigate();

    const loadAll = async () => {
        const token = getStoredToken();
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        try {
            const headers = { Authorization: `Bearer ${token}` };
            const [ticketsRes, assignmentsRes] = await Promise.all([
                fetch(TICKETS_URL, { headers }),
                fetch(ASSIGNMENTS_URL, { headers }),
            ]);

            if ([ticketsRes.status, assignmentsRes.status].includes(401)) {
                navigate('/login', { replace: true });
                return;
            }

            const [ticketsData, assignmentsData] = await Promise.all([
                ticketsRes.json().catch(() => ([])),
                assignmentsRes.json().catch(() => ([])),
            ]);

            if (!ticketsRes.ok) throw new Error(ticketsData?.message || 'Unable to load tickets.');
            if (!assignmentsRes.ok) throw new Error(assignmentsData?.message || 'Unable to load assignments.');

            setTickets(Array.isArray(ticketsData) ? ticketsData : []);
            setAssignments(Array.isArray(assignmentsData) ? assignmentsData : []);
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
                <p className="assignments-page__summary">See every ticket and who currently has it.</p>
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
                                    <th>Assigned to</th>
                                </tr>
                            </thead>
                            <tbody>
                                {tickets.map((ticket) => {
                                    const assignment = assignmentFor(ticket.id);

                                    return (
                                        <tr key={ticket.id} data-ticket-id={ticket.id}>
                                            <td>#{ticket.id}</td>
                                            <td>{ticket.issueType || 'General request'}</td>
                                            <td>{formatLabel(ticket.urgency, urgencyLabels)}</td>
                                            <td>{assignment ? `User ID ${assignment.agentUserId}` : 'Unassigned'}</td>
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
