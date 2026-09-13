import { useNavigate } from 'react-router-dom';
import TicketCreateForm from '../features/ticket/components/ticketCreateForm';
import { isAdmin, isAgent, getUserRole } from '../shared/authToken';
import './dashboard.css';

const Dashboard = () => {
    const navigate = useNavigate();
    const isEmployee = getUserRole() === 'Employee';
    const isAgentUser = isAgent();

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
                    {isEmployee && (
                        <button type="button" className="btn btn--secondary" onClick={() => navigate('/my-tickets')}>
                            My tickets
                        </button>
                    )}
                    {isAgentUser && (
                        <button type="button" className="btn btn--secondary" onClick={() => navigate('/agent/queue')}>
                            My queue
                        </button>
                    )}
                    <button type="button" className="btn btn--ghost" onClick={handleLogout}>
                        Log out
                    </button>
                </div>
            </div>

            <div className="dashboard-header">
                <p className="eyebrow">Dashboard</p>
                <h1>Welcome back</h1>
                <p className="dashboard-welcome">
                    {isEmployee && 'Keep an eye on your requests and get support moving.'}
                    {isAgentUser && 'Check your queue to see what needs your attention.'}
                    {!isEmployee && !isAgentUser && 'Ticket submission is for employees only.'}
                </p>
            </div>

            {isEmployee && <TicketCreateForm />}

            {isAgentUser && (
                <section className="dashboard-empty panel">
                    <div className="panel__body">
                        <p>Nothing to submit here — head to My queue to see your assigned tickets.</p>
                        <button type="button" className="btn btn--primary" onClick={() => navigate('/agent/queue')}>
                            My queue
                        </button>
                    </div>
                </section>
            )}

            {!isEmployee && !isAgentUser && isAdmin() && (
                <section className="dashboard-empty panel">
                    <div className="panel__body">
                        <p>Nothing to submit here — use Manage users to onboard new accounts.</p>
                        <button type="button" className="btn btn--primary" onClick={() => navigate('/admin/users')}>
                            Manage users
                        </button>
                    </div>
                </section>
            )}
        </main>
    );
};

export default Dashboard;
