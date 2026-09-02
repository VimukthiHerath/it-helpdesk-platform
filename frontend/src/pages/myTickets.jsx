import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import './myTickets.css';

const TICKET_API_URL = 'http://localhost:5164/api/ticket/mine';

const urgencyLabels = ['Within 1 hour', 'Within 6 hours', 'Within 12 hours', 'Within 24 hours'];
const statusLabels = ['Unassigned', 'Assigned', 'Resolved'];

const formatLabel = (value, labels) => {
    if (typeof value === 'number' && labels[value]) return labels[value];
    if (typeof value === 'string') {
        const normalized = value.replaceAll('_', ' ');
        return normalized.charAt(0).toUpperCase() + normalized.slice(1);
    }
    return 'Not specified';
};

const formatDate = (value) => {
    if (!value) return 'Date unavailable';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? 'Date unavailable' : date.toLocaleDateString(undefined, {
        year: 'numeric', month: 'short', day: 'numeric',
    });
};

const MyTickets = () => {
    const [tickets, setTickets] = useState([]);
    const [selectedTicket, setSelectedTicket] = useState(null);
    const [state, setState] = useState({ loading: true, error: '' });
    const navigate = useNavigate();

    useEffect(() => {
        const loadTickets = async () => {
            const token = localStorage.getItem('token');
            if (!token) {
                navigate('/login', { replace: true });
                return;
            }

            try {
                const response = await fetch(TICKET_API_URL, {
                    headers: { Authorization: `Bearer ${token}` },
                });
                const data = await response.json().catch(() => ([]));

                if (response.status === 401) {
                    localStorage.removeItem('token');
                    navigate('/login', { replace: true });
                    return;
                }
                if (!response.ok) throw new Error(data?.message || 'Unable to load your tickets.');

                const loadedTickets = Array.isArray(data) ? data : [];
                setTickets(loadedTickets);
                setSelectedTicket(loadedTickets[0] || null);
            } catch (error) {
                setState({ loading: false, error: error.message || 'Unable to load your tickets.' });
                return;
            }

            setState({ loading: false, error: '' });
        };

        loadTickets();
    }, [navigate]);

    return (
        <main className="tickets-page">
            <header className="tickets-page__header">
                <button type="button" className="back-button" onClick={() => navigate('/')}>
                    Back to dashboard
                </button>
                <p className="tickets-page__kicker">Support desk / Your activity</p>
                <h1>My tickets</h1>
                <p className="tickets-page__summary">A read-only view of every request you have raised.</p>
            </header>

            {state.loading && <div className="tickets-message">Loading your tickets...</div>}
            {!state.loading && state.error && (
                <div className="tickets-message tickets-message--error" role="alert">{state.error}</div>
            )}
            {!state.loading && !state.error && tickets.length === 0 && (
                <div className="tickets-message tickets-message--empty">
                    <strong>No tickets yet</strong>
                    <span>Your submitted requests will appear here.</span>
                </div>
            )}

            {!state.loading && !state.error && tickets.length > 0 && (
                <div className="tickets-layout">
                    <section className="ticket-list" aria-label="Your tickets">
                        <div className="ticket-list__heading">
                            <span>Requests</span>
                            <strong>{tickets.length}</strong>
                        </div>
                        {tickets.map((ticket) => (
                            <button
                                type="button"
                                className={`ticket-list-item ${selectedTicket?.id === ticket.id ? 'ticket-list-item--selected' : ''}`}
                                key={ticket.id}
                                onClick={() => setSelectedTicket(ticket)}
                            >
                                <span className="ticket-list-item__id">Ticket #{ticket.id}</span>
                                <strong>{ticket.issueType || 'General request'}</strong>
                                <span>{formatDate(ticket.createdAt)}</span>
                            </button>
                        ))}
                    </section>

                    {selectedTicket && (
                        <article className="ticket-detail" aria-live="polite">
                            <div className="ticket-detail__topline">
                                <span>Ticket #{selectedTicket.id}</span>
                                <span className="ticket-status-badge">{formatLabel(selectedTicket.status, statusLabels)}</span>
                            </div>
                            <h2>{selectedTicket.issueType || 'General request'}</h2>
                            <p className="ticket-detail__date">Submitted {formatDate(selectedTicket.createdAt)}</p>
                            <div className="ticket-detail__meta">
                                <div><span>Urgency</span><strong>{formatLabel(selectedTicket.urgency, urgencyLabels)}</strong></div>
                                <div><span>Last updated</span><strong>{formatDate(selectedTicket.updatedAt || selectedTicket.createdAt)}</strong></div>
                            </div>
                            <div className="ticket-detail__description">
                                <span>Description</span>
                                <p>{selectedTicket.description || 'No description provided.'}</p>
                            </div>
                        </article>
                    )}
                </div>
            )}
        </main>
    );
};

export default MyTickets;