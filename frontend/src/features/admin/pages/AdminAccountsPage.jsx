import Navbar from '../../../components/layout/Navbar';
import UserListPanel from '../components/UserListPanel';
import './adminPageShell.css';

const AdminAccountsPage = () => {
    return (
        <main className="admin-shell">
            <Navbar />

            <div className="page-header">
                <h1>All accounts</h1>
            </div>

            <UserListPanel />
        </main>
    );
};

export default AdminAccountsPage;
