import { useNavigate } from 'react-router-dom';
import TicketCreateForm from '../features/ticket/components/ticketCreateForm';
import Navbar from '../components/layout/Navbar';
import { getUserRole } from '../shared/authToken';
import './dashboard.css';

// Admins and agents have nothing to submit, so their home screen is a list of
// equal-size bars, one per place they work in.
const BARS_BY_ROLE = {
    Administrator: [
        { to: '/admin/accounts', label: 'All accounts' },
        { to: '/admin/users/new', label: 'Create user' },
        { to: '/admin/rotation', label: 'Agent rotation' },
        { to: '/admin/report', label: 'Reports' },
        { to: '/tickets', label: 'All tickets' },
    ],
    Agent: [
        { to: '/agent/queue', label: 'My queue' },
        { to: '/tickets', label: 'All tickets' },
    ],
};

const Dashboard = () => {
    const navigate = useNavigate();
    const role = getUserRole();
    const bars = BARS_BY_ROLE[role] || [];

    return (
        <main className="dashboard-shell">
            <Navbar />

            {role === 'Employee' ? (
                <TicketCreateForm />
            ) : (
                <nav className="action-bars" aria-label="Home">
                    <h1 className="sr-only">Home</h1>
                    {bars.map((bar) => (
                        <button key={bar.to} type="button" className="action-bar" onClick={() => navigate(bar.to)}>
                            <span>{bar.label}</span>
                            <span aria-hidden="true">→</span>
                        </button>
                    ))}
                </nav>
            )}
        </main>
    );
};

export default Dashboard;
