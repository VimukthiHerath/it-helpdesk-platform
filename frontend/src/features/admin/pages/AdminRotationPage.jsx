import Navbar from '../../../components/layout/Navbar';
import AgentRotationPanel from '../components/AgentRotationPanel';
import './adminPageShell.css';

const AdminRotationPage = () => {
    return (
        <main className="admin-shell">
            <Navbar />

            <div className="page-header">
                <h1>Agent rotation</h1>
            </div>

            <AgentRotationPanel />
        </main>
    );
};

export default AdminRotationPage;
