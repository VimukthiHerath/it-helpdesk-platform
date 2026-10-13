import { render } from '@testing-library/react';
import App from './App';

test('renders the application shell', () => {
  render(<App />);
  expect(document.body).toBeInTheDocument();
});
