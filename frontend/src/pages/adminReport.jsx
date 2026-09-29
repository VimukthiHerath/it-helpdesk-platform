import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getStoredToken } from '../shared/authToken';
import './adminReport.css';

const REPORT_URL = `${process.env.REACT_APP_TICKET_API_URL}/api/ticket/report`;

const STATUS_OPTIONS = [
    { label: 'All statuses', value: '' },
    { label: 'Unassigned', value: '0' },
    { label: 'Assigned', value: '1' },
    { label: 'Resolved', value: '2' },
    { label: 'In Progress', value: '3' },
    { label: 'Closed', value: '4' },
];

const URGENCY_OPTIONS = [
    { label: 'All urgencies', value: '' },
    { label: 'Within 1 hour', value: '0' },
    { label: 'Within 6 hours', value: '1' },
    { label: 'Within 12 hours', value: '2' },
    { label: 'Within 24 hours', value: '3' },
];

const AdminReportPage = () => {
    const navigate = useNavigate();

    const handleLogout = () => {
        localStorage.removeItem('token');
        navigate('/login', { replace: true });
    };

    const [filters, setFilters] = useState({ status: '', urgency: '', startDate: '', endDate: '' });
    const [tickets, setTickets] = useState([]);
    const [state, setState] = useState({ loading: false, error: '', searched: false });

    const handleChange = (e) => {
        setFilters((prev) => ({ ...prev, [e.target.name]: e.target.value }));
    };

    const handleSearch = async (e) => {
        e.preventDefault();
        const token = getStoredToken();
        if (!token) { navigate('/login', { replace: true }); return; }

        setState({ loading: true, error: '', searched: false });

        const params = new URLSearchParams();
        if (filters.status !== '') params.set('status', filters.status);
        if (filters.urgency !== '') params.set('urgency', filters.urgency);
        if (filters.startDate) params.set('startDate', filters.startDate);
        if (filters.endDate) params.set('endDate', filters.endDate);

        try {
            const res = await fetch(`${REPORT_URL}?${params.toString()}`, {
                headers: { Authorization: `Bearer ${token}` },
            });

            if (res.status === 401) { navigate('/login', { replace: true }); return; }
            if (!res.ok) throw new Error('Failed to load report.');

            const data = await res.json();
            setTickets(data);
            setState({ loading: false, error: '', searched: true });
        } catch (err) {
            setState({ loading: false, error: err.message, searched: true });
        }
    };

    const handleClear = () => {
        setFilters({ status: '', urgency: '', startDate: '', endDate: '' });
        setTickets([]);
        setState({ loading: false, error: '', searched: false });
    };

    return (
        <main className="report-page">
            <div className="app-bar">
                <span className="app-bar__brand">IT Helpdesk</span>
                <div className="app-bar__actions">
                    <button type="button" className="btn btn--secondary" onClick={() => navigate('/admin/accounts')}>
                        All accounts
                    </button>
                    <button type="button" className="btn btn--secondary" onClick={() => navigate('/tickets')}>
                        All tickets
                    </button>
                    <button type="button" className="btn btn--secondary" onClick={() => navigate('/')}>
                        Dashboard
                    </button>
                    <button type="button" className="btn btn--ghost" onClick={handleLogout}>
                        Log out
                    </button>
                </div>
            </div>

            <div className="report-page__header">
                <button type="button" className="btn btn--ghost report-page__back" onClick={() => navigate('/')}>
                    ← Dashboard
                </button>
                <p className="eyebrow">Administrator</p>
                <h1>Ticket Report</h1>
                <p className="report-page__subtitle">
                    Filter by status, urgency, or date range to review team performance.
                </p>
            </div>

            <section className="report-filters panel">
                <form className="report-filters__form" onSubmit={handleSearch}>
                    <div className="report-filters__grid">
                        <div className="report-filters__field">
                            <label htmlFor="report-status" className="label">Status</label>
                            <select
                                id="report-status"
                                name="status"
                                className="input"
                                value={filters.status}
                                onChange={handleChange}
                            >
                                {STATUS_OPTIONS.map((o) => (
                                    <option key={o.value} value={o.value}>{o.label}</option>
                                ))}
                            </select>
                        </div>

                        <div className="report-filters__field">
                            <label htmlFor="report-urgency" className="label">Urgency</label>
                            <select
                                id="report-urgency"
                                name="urgency"
                                className="input"
                                value={filters.urgency}
                                onChange={handleChange}
                            >
                                {URGENCY_OPTIONS.map((o) => (
                                    <option key={o.value} value={o.value}>{o.label}</option>
                                ))}
                            </select>
                        </div>

                        <div className="report-filters__field">
                            <label htmlFor="report-startDate" className="label">From date</label>
                            <input
                                id="report-startDate"
                                name="startDate"
                                type="date"
                                className="input"
                                value={filters.startDate}
                                onChange={handleChange}
                            />
                        </div>

                        <div className="report-filters__field">
                            <label htmlFor="report-endDate" className="label">To date</label>
                            <input
                                id="report-endDate"
                                name="endDate"
                                type="date"
                                className="input"
                                value={filters.endDate}
                                onChange={handleChange}
                            />
                        </div>
                    </div>

                    <div className="report-filters__actions">
                        <button id="report-apply-btn" type="submit" className="btn btn--primary" disabled={state.loading}>
                            {state.loading ? 'Loading…' : 'Apply filters'}
                        </button>
                        <button id="report-clear-btn" type="button" className="btn btn--ghost" onClick={handleClear}>
                            Clear
                        </button>
                    </div>
                </form>
            </section>

            {state.error && (
                <p className="report-message report-message--error">{state.error}</p>
            )}

            {/* AC4: No tickets found message */}
            {state.searched && !state.loading && !state.error && tickets.length === 0 && (
                <section className="report-message panel">
                    <div className="panel__body">
                        <strong>No tickets found</strong>
                        <p>No tickets match your selected filters. Try adjusting them and applying again.</p>
                    </div>
                </section>
            )}

            {/* AC2: Results table */}
            {tickets.length > 0 && (
                <section className="report-results">
                    <p className="report-results__count">{tickets.length} ticket{tickets.length !== 1 ? 's' : ''} found</p>
                    <div className="report-table-wrap">
                        <table className="report-table" aria-label="Ticket report results">
                            <thead>
                                <tr>
                                    <th scope="col">Ticket ID</th>
                                    <th scope="col" className="report-table__subject">Subject</th>
                                    <th scope="col">Status</th>
                                    <th scope="col">Urgency</th>
                                    <th scope="col">Created Date</th>
                                    <th scope="col">Assigned Agent</th>
                                </tr>
                            </thead>
                            <tbody>
                                {tickets.map((t) => (
                                    <tr key={t.ticketId}>
                                        <td>#{t.ticketId}</td>
                                        <td className="report-table__subject">{t.subject}</td>
                                        <td><span className={`report-badge report-badge--${t.status.toLowerCase().replace(' ', '-')}`}>{t.status}</span></td>
                                        <td>{t.urgency}</td>
                                        <td>{new Date(t.createdDate).toLocaleDateString()}</td>
                                        <td>{t.assignedAgent != null ? `Agent #${t.assignedAgent}` : <span className="report-unassigned">Unassigned</span>}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </section>
            )}
        </main>
    );
};

export default AdminReportPage;
