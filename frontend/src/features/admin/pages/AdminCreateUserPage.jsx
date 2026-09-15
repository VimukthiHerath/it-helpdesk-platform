import { useNavigate } from 'react-router-dom';
import CreateUserForm from '../components/CreateUserForm';
import './adminPageShell.css';

const AdminCreateUserPage = () => {
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
                <h1>Create user</h1>
                <p className="admin-welcome">Onboard an employee, agent, or another administrator onto the platform.</p>
            </div>

            <CreateUserForm />
        </main>
    );
};

export default AdminCreateUserPage;
