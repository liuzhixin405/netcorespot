import React from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import styled from 'styled-components';
import { BarChart3, LogOut, User } from 'lucide-react';

const NavbarContainer = styled.nav`
  height: 44px;
  background: rgba(18, 22, 28, 0.96);
  border-bottom: 1px solid rgba(87, 100, 122, 0.28);
  display: grid;
  grid-template-columns: 1fr auto;
  align-items: center;
  gap: 12px;
  padding: 0 12px;
  flex-shrink: 0;
`;

const LeftGroup = styled.div`
  display: flex;
  align-items: center;
  gap: 14px;
  min-width: 0;
`;

const Logo = styled(Link)`
  color: #00d4ff;
  text-decoration: none;
  font-size: 12px;
  font-weight: 900;
  letter-spacing: 0.04em;
  white-space: nowrap;
`;

const SectionTag = styled(Link)<{ $active?: boolean }>`
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 26px;
  padding: 0 9px;
  border-radius: 6px;
  text-decoration: none;
  font-size: 12px;
  font-weight: 800;
  color: ${props => props.$active ? '#00d4ff' : '#8b949e'};
  background: ${props => props.$active ? 'rgba(0, 212, 255, 0.08)' : 'transparent'};
  border: 1px solid ${props => props.$active ? 'rgba(0, 212, 255, 0.18)' : 'transparent'};
`;

const RightGroup = styled.div`
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
`;

const UserInfo = styled.div`
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: #c9d1d9;
  font-size: 12px;
  white-space: nowrap;
`;

const LogoutButton = styled.button`
  height: 28px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 0 10px;
  border: none;
  border-radius: 6px;
  background: #ff4d4f;
  color: white;
  font-size: 12px;
  font-weight: 800;
  cursor: pointer;

  &:hover {
    background: #ff5e61;
  }
`;

const Navbar: React.FC = () => {
  const { user, logout } = useAuth();
  const location = useLocation();

  return (
    <NavbarContainer>
      <LeftGroup>
        <Logo to="/trading">CryptoSpot</Logo>
        <SectionTag to="/trading" $active={location.pathname === '/trading'}>
          <BarChart3 size={14} />
          交易
        </SectionTag>
      </LeftGroup>

      <RightGroup>
        <UserInfo>
          <User size={14} />
          {user?.username}
        </UserInfo>
        <LogoutButton onClick={logout}>
          <LogOut size={14} />
          Logout
        </LogoutButton>
      </RightGroup>
    </NavbarContainer>
  );
};

export default Navbar;
