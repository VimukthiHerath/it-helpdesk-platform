import { useNavigate } from 'react-router-dom';
import TicketCreateForm from '../features/ticket/components/ticketCreateForm';
import { isAdmin } from '../shared/authToken';
import './dashboard.css';

const Dashboard = () => {
    const navigate = useNavigate();

    const handleLogout = () => {
        localStorage.removeItem('token');
        navigate('/login', { replace: true });
    };

    return (
        <main className="dashboard-shell">
            <div className="app-bar">
                <span className="app-bar__brand">IT Helpdesk</span>
                <div className="app-bar__actions">
                    {isAdmin() && (
                        <button type="button" className="btn btn--secondary" onClick={() => navigate('/admin/users')}>
                            Manage users
                        </button>
                    )}
                    <button type="button" className="btn btn--secondary" onClick={() => navigate('/my-tickets')}>
                        My tickets
                    </button>
                    <button type="button" className="btn btn--ghost" onClick={handleLogout}>
                        Log out
                    </button>
                </div>
            </div>

            <div className="dashboard-header">
                <p className="eyebrow">Dashboard</p>
                <h1>Welcome back</h1>
                <p className="dashboard-welcome">Keep an eye on your requests and get support moving.</p>
            </div>

            <TicketCreateForm />
        </main>
    );
};

export default Dashboard;
