import { useNavigate } from 'react-router-dom';
import TicketCreateForm from '../features/ticket/components/ticketCreateForm';
import './dashboard.css';

const Dashboard = () => {
    const navigate = useNavigate();

    const handleLogout = () => {
        localStorage.removeItem('token');
        navigate('/login', { replace: true });
    };

    return (
        <main className="dashboard-shell">
            <div className="dashboard-header">
                <div>
                    <p className="dashboard-kicker">IT helpdesk</p>
                    <h1>Dashboard</h1>
                    <p className="dashboard-welcome">Keep an eye on your requests and get support moving.</p>
                </div>
                <div className="dashboard-actions">
                    <button type="button" className="dashboard-tickets-button" onClick={() => navigate('/my-tickets')}>
                        View my tickets
                    </button>
                    <button type="button" className="dashboard-logout-button" onClick={handleLogout}>
                        Log out
                    </button>
                </div>
            </div>

            <TicketCreateForm />
        </main>
    );
};

export default Dashboard;
