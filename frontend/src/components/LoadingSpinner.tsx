import React from 'react';
import styled, { keyframes } from 'styled-components';

const spin = keyframes`
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
`;

const SpinnerContainer = styled.div`
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 4rem;
`;

const Spinner = styled.div`
  width: 50px;
  height: 50px;
  border: 3px solid rgba(0, 212, 255, 0.3);
  border-top: 3px solid #00d4ff;
  border-radius: 50%;
  animation: ${spin} 1s linear infinite;
`;

const LoadingText = styled.div`
  margin-left: 1rem;
  color: #888;
  font-size: 1.1rem;
`;

const LoadingSpinner: React.FC = () => {
  return (
    <SpinnerContainer>
      <Spinner />
      <LoadingText>Loading cryptocurrency data...</LoadingText>
    </SpinnerContainer>
  );
};

export default LoadingSpinner;
