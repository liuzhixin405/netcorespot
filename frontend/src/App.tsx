import React, { useEffect } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import styled from 'styled-components';
import { useAuth } from './contexts/AuthContext';
import Login from './pages/Login';
import Register from './pages/Register';
import Trading from './pages/Trading';
import Navbar from './components/Navbar';

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

function App() {
  const { user, loading } = useAuth();

  if (loading) {
    return (
      <AppContainer>
        <div style={{ 
          display: 'flex', 
          justifyContent: 'center', 
          alignItems: 'center', 
          height: '100vh',
          fontSize: '18px'
        }}>
          Loading...
        </div>
      </AppContainer>
    );
  }

  return (
    <AppContainer>
      {user && <Navbar />}
      <MainContent>
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
      </MainContent>
      <Footer>
        CryptoSpot v1.0.0
      </Footer>
    </AppContainer>
  );
}

export default App;
