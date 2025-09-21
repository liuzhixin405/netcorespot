import React from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import styled from 'styled-components';
import { LogOut, User, BarChart3, TrendingUp } from 'lucide-react';

const NavbarContainer = styled.nav`
  height: 80px;
  background: rgba(26, 26, 26, 0.95);
  backdrop-filter: blur(10px);
  border-bottom: 1px solid #333;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 2rem;
  flex-shrink: 0;
`;

const Logo = styled.div`
  font-size: 1.5rem;
  font-weight: bold;
  color: #00d4ff;
`;

const Navigation = styled.div`
  display: flex;
  align-items: center;
  gap: 2rem;
`;

const NavLink = styled(Link)<{ $active?: boolean }>`
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: ${props => props.$active ? '#00d4ff' : '#ccc'};
  text-decoration: none;
  padding: 0.5rem 1rem;
  border-radius: 6px;
  transition: all 0.2s;

  &:hover {
    color: #00d4ff;
    background: rgba(0, 212, 255, 0.1);
  }
`;

const UserSection = styled.div`
  display: flex;
  align-items: center;
  gap: 1rem;
`;

const UserInfo = styled.div`
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #ccc;
`;

const LogoutButton = styled.button`
  background: #ff4444;
  color: white;
  border: none;
  padding: 0.5rem 1rem;
  border-radius: 6px;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  transition: background-color 0.2s;

  &:hover {
    background: #ff6666;
  }
`;

const Navbar: React.FC = () => {
  const { user, logout } = useAuth();
  const location = useLocation();

  return (
    <NavbarContainer>
      <Logo>CryptoSpot</Logo>
      
      <Navigation>
        <NavLink 
          to="/trading" 
          $active={location.pathname === '/trading'}
        >
          <BarChart3 size={16} />
          交易
        </NavLink>
      </Navigation>
      
      <UserSection>
        <UserInfo>
          <User size={20} />
          {user?.username}
        </UserInfo>
        <LogoutButton onClick={logout}>
          <LogOut size={16} />
          Logout
        </LogoutButton>
      </UserSection>
    </NavbarContainer>
  );
};

export default Navbar;
