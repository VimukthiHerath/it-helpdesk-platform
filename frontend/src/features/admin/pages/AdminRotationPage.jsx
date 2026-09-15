import { useNavigate } from 'react-router-dom';
import AgentRotationPanel from '../components/AgentRotationPanel';
import './adminPageShell.css';

const AdminRotationPage = () => {
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
                    <button type="button" className="btn btn--secondary" onClick={() => navigate('/admin/accounts')}>
                        All accounts
                    </button>
                    <button type="button" className="btn btn--secondary" onClick={() => navigate('/admin/users/new')}>
                        Create user
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
                <h1>Agent rotation</h1>
                <p className="admin-welcome">Manage who's in the round-robin ticket assignment order.</p>
            </div>

            <AgentRotationPanel />
        </main>
    );
};

export default AdminRotationPage;
