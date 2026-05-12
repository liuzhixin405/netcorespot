import React, { Suspense } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import styled from 'styled-components';
import { useAuth } from './contexts/AuthContext';

const Login = React.lazy(() => import('./pages/Login'));
const Register = React.lazy(() => import('./pages/Register'));
const Trading = React.lazy(() => import('./pages/Trading'));

const AppContainer = styled.div`
  height: 100vh;
  background: linear-gradient(135deg, #0a0a0a 0%, #1a1a1a 100%);
  overflow: hidden;
  display: flex;
  flex-direction: column;
`;

const MainContent = styled.main`
  flex: 1;
  overflow: hidden;
  min-height: 0;
`;

const Footer = styled.footer`
  height: 20px;
  background: #0d1117;
  border-top: 1px solid #30363d;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.7rem;
  color: #7d8590;
  flex-shrink: 0;
`;

const LoadingFallback = styled.div`
  display: flex;
  justify-content: center;
  align-items: center;
  height: 100vh;
  font-size: 18px;
  color: #7d8590;
`;

function App() {
  const { user, loading } = useAuth();

  if (loading) {
    return (
      <AppContainer>
        <LoadingFallback>Loading...</LoadingFallback>
      </AppContainer>
    );
  }

  return (
    <AppContainer>
      <MainContent>
        <Suspense fallback={<LoadingFallback>Loading...</LoadingFallback>}>
          <Routes>
            <Route
              path="/login"
              element={user ? <Navigate to="/trading" /> : <Login />}
            />
            <Route
              path="/register"
              element={user ? <Navigate to="/trading" /> : <Register />}
            />
            <Route
              path="/trading"
              element={user ? <Trading /> : <Navigate to="/login" />}
            />
            <Route
              path="/"
              element={<Navigate to={user ? "/trading" : "/login"} />}
            />
          </Routes>
        </Suspense>
      </MainContent>
      <Footer>
        CryptoSpot v1.0.0
      </Footer>
    </AppContainer>
  );
}

export default App;
