import React from 'react';
import { getUserRole } from '../../shared/authToken';
import './Navbar.css';

const formatRoleTitleCase = (role) => {
    if (!role) return '';
    // Normalize role string to Title Case / user friendly labels
    if (role.toLowerCase() === 'administrator') return 'Admin';
    if (role.toLowerCase() === 'agent') return 'Support Agent';
    if (role.toLowerCase() === 'employee') return 'Employee';
    // Fallback: Title Case capitalization
    return role.charAt(0).toUpperCase() + role.slice(1);
};

const Navbar = ({ children }) => {
    const role = getUserRole();
    const formattedRole = formatRoleTitleCase(role);

    return (
        <header className="app-bar">
            <div className="app-bar__left">
                <span className="app-bar__brand">IT Helpdesk</span>
                {formattedRole && (
                    <span
                        className="app-bar__role-badge"
                        title={`Logged in as ${formattedRole}`}
                        aria-label={`Role: ${formattedRole}`}
                    >
                        {formattedRole}
                    </span>
                )}
            </div>
            <div className="app-bar__actions">
                {children}
            </div>
        </header>
    );
};

export default Navbar;
