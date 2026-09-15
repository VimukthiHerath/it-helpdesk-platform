import { useNavigate } from 'react-router-dom';
import UserListPanel from '../components/UserListPanel';
import './adminPageShell.css';

const AdminAccountsPage = () => {
    const navigate = useNavigate();

    const handleLogout = () => {
        localStorage.removeItem('token');
        navigate('/login', { replace: true });
    };

    return (
        <main className="admin-shell">
            <div className="app-bar">
                <span className="app-bar__brand">IT Helpdesk</span>
                <div className="app-bar__actions">
                    <button type="button" className="btn btn--secondary" onClick={() => navigate('/admin/users/new')}>
                        Create user
                    </button>
                    <button type="button" className="btn btn--secondary" onClick={() => navigate('/admin/rotation')}>
                        Agent rotation
                    </button>
                    <button type="button" className="btn btn--secondary" onClick={() => navigate('/')}>
                        Dashboard
                    </button>
                    <button type="button" className="btn btn--ghost" onClick={handleLogout}>
                        Log out
                    </button>
                </div>
            </div>

            <div className="admin-header">
                <p className="eyebrow">Administration</p>
                <h1>All accounts</h1>
                <p className="admin-welcome">Every employee, agent, and administrator account on the platform.</p>
            </div>

            <UserListPanel />
        </main>
    );
};

export default AdminAccountsPage;
