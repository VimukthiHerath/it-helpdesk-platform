import React from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import { getUserRole } from '../../shared/authToken';
import './Navbar.css';

const ROLE_LABELS = {
    administrator: 'Admin',
    agent: 'Support Agent',
    employee: 'Employee',
};

const formatRoleTitleCase = (role) => {
    if (!role) return '';
    return ROLE_LABELS[role.toLowerCase()] || role.charAt(0).toUpperCase() + role.slice(1);
};

// One place decides what each role can navigate to, so every screen shows the same menu.
const LINKS_BY_ROLE = {
    Employee: [
        { to: '/', label: 'New ticket' },
        { to: '/my-tickets', label: 'My tickets' },
    ],
    Agent: [
        { to: '/', label: 'Home' },
        { to: '/agent/queue', label: 'My queue' },
        { to: '/tickets', label: 'All tickets' },
    ],
    Administrator: [
        { to: '/', label: 'Home' },
        { to: '/admin/accounts', label: 'All accounts' },
        { to: '/admin/users/new', label: 'Create user' },
        { to: '/admin/rotation', label: 'Agent rotation' },
        { to: '/admin/report', label: 'Reports' },
        { to: '/tickets', label: 'All tickets' },
    ],
};

const Navbar = () => {
    const navigate = useNavigate();
    const role = getUserRole();
    const formattedRole = formatRoleTitleCase(role);
    const links = LINKS_BY_ROLE[role] || [];

    const handleLogout = () => {
        localStorage.removeItem('token');
        void navigate('/login', { replace: true });
    };

    return (
        <header className="app-bar">
            <div className="app-bar__top">
                <div className="app-bar__left">
                    <span className="app-bar__brand">IT Helpdesk</span>
                    {formattedRole && (
                        <span
                            className="app-bar__role-badge"
                            title={`Signed in as ${formattedRole}`}
                            aria-label={`Role: ${formattedRole}`}
                        >
                            {formattedRole}
                        </span>
                    )}
                </div>
                <button type="button" className="btn btn--ghost app-bar__logout" onClick={handleLogout}>
                    Log out
                </button>
            </div>
            {links.length > 0 && (
                <nav className="app-bar__nav" aria-label="Main">
                    {links.map((link) => (
                        <NavLink
                            key={link.to}
                            to={link.to}
                            end
                            className={({ isActive }) => `app-bar__link${isActive ? ' app-bar__link--active' : ''}`}
                        >
                            {link.label}
                        </NavLink>
                    ))}
                </nav>
            )}
        </header>
    );
};

export default Navbar;
