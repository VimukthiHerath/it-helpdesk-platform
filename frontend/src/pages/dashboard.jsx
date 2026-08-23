import { useNavigate } from 'react-router-dom';
import TicketCreateForm from '../features/ticket/components/ticketCreateForm';

const Dashboard = () => {
    const navigate = useNavigate();

    const handleLogout = () => {
        localStorage.removeItem('token');
        navigate('/login', { replace: true });
    };

    return (
        <div style={{ padding: '32px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <h1>Dashboard</h1>
                <button onClick={handleLogout} style={{ padding: '10px 16px', cursor: 'pointer' }}>
                    Logout
                </button>
            </div>

            <p>Welcome to the dashboard.</p>
            <TicketCreateForm />
        </div>
    );
};

export default Dashboard;
