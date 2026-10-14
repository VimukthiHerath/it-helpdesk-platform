import Navbar from '../../../components/layout/Navbar';
import CreateUserForm from '../components/CreateUserForm';
import './adminPageShell.css';

const AdminCreateUserPage = () => {
    return (
        <main className="admin-shell">
            <Navbar />

            <div className="page-header">
                <h1>Create user</h1>
            </div>

            <CreateUserForm />
        </main>
    );
};

export default AdminCreateUserPage;
