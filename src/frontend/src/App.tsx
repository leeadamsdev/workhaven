import { Route, Routes } from 'react-router';
import { ConfirmEmailPage } from './features/identity/ConfirmEmailPage';
import { SessionPage } from './features/identity/SessionPage';
import { NotFoundPage } from './NotFoundPage';

export function App() {
  return (
    <Routes>
      <Route path="/" element={<SessionPage view="account" />} />
      <Route path="/sign-in" element={<SessionPage view="sign-in" />} />
      <Route path="/confirm-email" element={<ConfirmEmailPage />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}
