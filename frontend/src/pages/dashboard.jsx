import { useNavigate } from 'react-router-dom';
import TicketCreateForm from '../features/ticket/components/ticketCreateForm';
import { isAdmin, getUserRole } from '../shared/authToken';
import './dashboard.css';

const Dashboard = () => {
    const navigate = useNavigate();
    const isEmployee = getUserRole() === 'Employee';

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
                    <button type="button" className="btn btn--ghost" onClick={handleLogout}>
                        Log out
                    </button>
                </div>
            </div>

            <div className="dashboard-header">
                <p className="eyebrow">Dashboard</p>
                <h1>Welcome back</h1>
                <p className="dashboard-welcome">
                    {isEmployee
                        ? 'Keep an eye on your requests and get support moving.'
                        : 'Ticket submission is for employees only.'}
                </p>
            </div>

            {isEmployee ? (
                <TicketCreateForm />
            ) : (
                isAdmin() && (
                    <section className="dashboard-empty panel">
                        <div className="panel__body">
                            <p>Nothing to submit here — use Manage users to onboard new accounts.</p>
                            <button type="button" className="btn btn--primary" onClick={() => navigate('/admin/users')}>
                                Manage users
                            </button>
                        </div>
                    </section>
                )
            )}
        </main>
    );
};

export default Dashboard;
