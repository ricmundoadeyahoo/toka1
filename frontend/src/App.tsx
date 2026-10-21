import { Navigate, Route, Routes } from 'react-router-dom';
import { Toasts } from './components/common';
import { Layout, ProtectedRoute } from './components/Layout';
import { AuditPage, RolesPage } from './pages/AdminPages';
import { AssistantPage } from './pages/AssistantPage';
import { LoginPage, RegisterPage } from './pages/AuthPages';
import { ProfilePage, UsersPage } from './pages/UsersPages';
import { Permissions } from './types';

export default function App() {
  return (
    <>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/registro" element={<RegisterPage />} />
        <Route
          element={
            <ProtectedRoute>
              <Layout />
            </ProtectedRoute>
          }
        >
          <Route path="/perfil" element={<ProfilePage />} />
          <Route path="/usuarios" element={<ProtectedRoute permission={Permissions.UsersRead}><UsersPage /></ProtectedRoute>} />
          <Route path="/roles" element={<ProtectedRoute permission={Permissions.RolesRead}><RolesPage /></ProtectedRoute>} />
          <Route path="/auditoria" element={<ProtectedRoute permission={Permissions.AuditRead}><AuditPage /></ProtectedRoute>} />
          <Route path="/asistente" element={<ProtectedRoute permission={Permissions.AiUse}><AssistantPage /></ProtectedRoute>} />
        </Route>
        <Route path="*" element={<Navigate to="/perfil" replace />} />
      </Routes>
      <Toasts />
    </>
  );
}
