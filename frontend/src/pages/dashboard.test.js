import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Dashboard from './dashboard';

// Builds an unsigned token: the app only reads the payload to know the role.
const tokenFor = (role) => {
    const encode = (value) => btoa(JSON.stringify(value)).replace(/=+$/, '');
    return `${encode({ alg: 'none' })}.${encode({ role, sub: '1' })}.sig`;
};

const renderAs = (role) => {
    localStorage.setItem('token', tokenFor(role));
    return render(
        <MemoryRouter>
            <Dashboard />
        </MemoryRouter>,
    );
};

afterEach(() => localStorage.clear());

test('admin home is five equal bars, one per work area', () => {
    renderAs('Administrator');

    const bars = within(screen.getByRole('navigation', { name: 'Home' })).getAllByRole('button');
    expect(bars.map((bar) => bar.textContent.replace('→', ''))).toEqual([
        'All accounts',
        'Create user',
        'Agent rotation',
        'Reports',
        'All tickets',
    ]);
    bars.forEach((bar) => expect(bar).toHaveClass('action-bar'));
});

test('admin home has no filler text', () => {
    renderAs('Administrator');

    expect(screen.queryByText(/welcome back/i)).toBeNull();
    expect(screen.queryByText(/nothing to submit/i)).toBeNull();
    expect(screen.queryByText(/employees only/i)).toBeNull();
});

test('the role is shown in the navbar in title case', () => {
    renderAs('Administrator');
    expect(screen.getByLabelText('Role: Admin')).toBeInTheDocument();
});

test('agent role reads Support Agent and gets the agent menu', () => {
    renderAs('Agent');

    expect(screen.getByLabelText('Role: Support Agent')).toBeInTheDocument();
    const menu = within(screen.getByRole('navigation', { name: 'Main' }));
    expect(menu.getByText('My queue')).toBeInTheDocument();
    expect(menu.queryByText('All accounts')).toBeNull();
});

test('employee lands on the ticket form with an employee-only menu', () => {
    renderAs('Employee');

    expect(screen.getByLabelText('Role: Employee')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /create ticket/i })).toBeInTheDocument();
    const menu = within(screen.getByRole('navigation', { name: 'Main' }));
    expect(menu.getByText('My tickets')).toBeInTheDocument();
    expect(menu.queryByText('Reports')).toBeNull();
});
