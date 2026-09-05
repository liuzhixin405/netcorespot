import React from 'react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import styled from 'styled-components';
import { Eye, EyeOff, LogIn, User } from 'lucide-react';
import toast from 'react-hot-toast';

const LoginContainer = styled.div`
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 100vh;
  padding: 2rem;
`;

const LoginCard = styled.div`
  background: rgba(26, 26, 26, 0.9);
  border: 1px solid #333;
  border-radius: 12px;
  padding: 2rem;
  width: 100%;
  max-width: 400px;
  backdrop-filter: blur(10px);
`;

const Title = styled.h1`
  text-align: center;
  margin-bottom: 2rem;
  color: #00d4ff;
  font-size: 2rem;
`;

const Form = styled.form`
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
`;

const InputGroup = styled.div`
  position: relative;
`;

const Input = styled.input`
  width: 100%;
  padding: 1rem;
  background: rgba(51, 51, 51, 0.5);
  border: 1px solid #555;
  border-radius: 8px;
  color: white;
  font-size: 1rem;
  transition: border-color 0.2s;

  &:focus {
    outline: none;
    border-color: #00d4ff;
  }

  &::placeholder {
    color: #888;
  }
`;

const PasswordToggle = styled.button`
  position: absolute;
  right: 1rem;
  top: 50%;
  transform: translateY(-50%);
  background: none;
  border: none;
  color: #888;
  cursor: pointer;
  padding: 0.25rem;

  &:hover {
    color: #ccc;
  }
`;

const Button = styled.button`
  width: 100%;
  padding: 1rem;
  background: linear-gradient(135deg, #00d4ff, #0099cc);
  color: white;
  border: none;
  border-radius: 8px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: transform 0.2s, box-shadow 0.2s;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;

  &:hover {
    transform: translateY(-2px);
    box-shadow: 0 8px 25px rgba(0, 212, 255, 0.3);
  }

  &:disabled {
    opacity: 0.6;
    cursor: not-allowed;
    transform: none;
    box-shadow: none;
  }
`;

const TestAccountPanel = styled.div`
  margin-top: 1.25rem;
  padding: 1rem;
  background: rgba(0, 212, 255, 0.08);
  border: 1px solid rgba(0, 212, 255, 0.22);
  border-radius: 8px;
`;

const TestAccountTitle = styled.div`
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
  color: #d8f7ff;
  font-size: 0.9rem;
  font-weight: 600;
`;

const TestAccountList = styled.div`
  display: grid;
  gap: 0.5rem;
`;

const TestAccountButton = styled.button`
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
  min-height: 2.5rem;
  padding: 0.6rem 0.75rem;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  color: #f0f6fc;
  cursor: pointer;
  font-size: 0.85rem;
  transition: border-color 0.2s, background 0.2s;

  &:hover:not(:disabled) {
    background: rgba(0, 212, 255, 0.12);
    border-color: rgba(0, 212, 255, 0.36);
  }

  &:disabled {
    opacity: 0.6;
    cursor: not-allowed;
  }
`;

const TestAccountPassword = styled.span`
  color: #8b949e;
`;

const LinkText = styled.div`
  text-align: center;
  margin-top: 1rem;
  color: #888;

  a {
    color: #00d4ff;
    text-decoration: none;

    &:hover {
      text-decoration: underline;
    }
  }
`;

const Login: React.FC = () => {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();
  const navigate = useNavigate();

  const testAccounts = ['test_user_1', 'test_user_2', 'test_user_3'];

  const fillTestAccount = (account: string) => {
    setUsername(account);
    setPassword('test123');
  };


  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!username || !password) {
      toast.error('Please fill in all fields');
      return;
    }

    setLoading(true);
    try {
      const success = await login({ username, password });
      
      if (success) {
        toast.success('Login successful!');
        // 使用 setTimeout 确保状态更新完成后再跳转
        setTimeout(() => {
          navigate('/trading');
        }, 100);
      } else {
        toast.error('Invalid username or password');
      }
    } catch (error: any) {
      toast.error('Login failed. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <LoginContainer>
      <LoginCard>
        <Title>Welcome Back</Title>
        <Form onSubmit={handleSubmit}>
          <InputGroup>
            <Input
              type="text"
              placeholder="Username"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              disabled={loading}
            />
          </InputGroup>
          
          <InputGroup>
            <Input
              type={showPassword ? 'text' : 'password'}
              placeholder="Password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={loading}
            />
            <PasswordToggle
              type="button"
              onClick={() => setShowPassword(!showPassword)}
              disabled={loading}
            >
              {showPassword ? <EyeOff size={20} /> : <Eye size={20} />}
            </PasswordToggle>
          </InputGroup>

          <Button type="submit" disabled={loading}>
            <LogIn size={20} />
            {loading ? 'Signing In...' : 'Sign In'}
          </Button>
        </Form>

        <TestAccountPanel>
          <TestAccountTitle>
            <User size={16} />
            测试账号
          </TestAccountTitle>
          <TestAccountList>
            {testAccounts.map((account) => (
              <TestAccountButton
                key={account}
                type="button"
                onClick={() => fillTestAccount(account)}
                disabled={loading}
                title="点击填入测试账号"
              >
                <span>{account}</span>
                <TestAccountPassword>test123</TestAccountPassword>
              </TestAccountButton>
            ))}
          </TestAccountList>
        </TestAccountPanel>

        <LinkText>
          Don't have an account? <Link to="/register">Sign up</Link>
        </LinkText>
      </LoginCard>
    </LoginContainer>
  );
};

export default Login;
