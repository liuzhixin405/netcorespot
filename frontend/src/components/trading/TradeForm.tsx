import React, { useState } from 'react';
import styled from 'styled-components';
import { useAuth } from '../../contexts/AuthContext';
import { tradingService } from '../../services/tradingService';

const Container = styled.div`
  height: 100%;
  display: flex;
  flex-direction: column;
  background: #161b22;
  min-height: 0;
`;

const TabHeader = styled.div`
  display: flex;
  border-bottom: 1px solid #30363d;
`;

const Tab = styled.button<{ active: boolean }>`
  flex: 1;
  padding: 0.5rem;
  background: ${props => props.active ? '#f0f6fc' : 'transparent'};
  color: ${props => props.active ? '#0d1117' : '#7d8590'};
  border: none;
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 600;
  transition: all 0.2s;
  
  &:hover {
    background: ${props => props.active ? '#f0f6fc' : 'rgba(255, 255, 255, 0.05)'};
    color: ${props => props.active ? '#0d1117' : '#f0f6fc'};
  }
`;

const FormContent = styled.div`
  flex: 1;
  padding: 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  min-height: 0;
  overflow-y: auto;
`;

const FormGroup = styled.div`
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
`;

const Label = styled.label`
  font-size: 0.8rem;
  color: #7d8590;
  font-weight: 500;
`;

const Input = styled.input`
  padding: 0.75rem;
  background: #0d1117;
  border: 1px solid #30363d;
  border-radius: 6px;
  color: #f0f6fc;
  font-size: 0.9rem;
  
  &:focus {
    outline: none;
    border-color: #f0f6fc;
  }
  
  &::placeholder {
    color: #7d8590;
  }
`;

const Select = styled.select`
  padding: 0.75rem;
  background: #0d1117;
  border: 1px solid #30363d;
  border-radius: 6px;
  color: #f0f6fc;
  font-size: 0.9rem;
  cursor: pointer;
  
  &:focus {
    outline: none;
    border-color: #f0f6fc;
  }
  
  option {
    background: #0d1117;
    color: #f0f6fc;
  }
`;

const PriceRow = styled.div`
  display: grid;
  grid-template-columns: 1fr 80px;
  gap: 0.5rem;
  align-items: end;
`;

const PercentButtons = styled.div`
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 0.25rem;
`;

const PercentButton = styled.button`
  padding: 0.5rem;
  background: #21262d;
  border: 1px solid #30363d;
  border-radius: 4px;
  color: #7d8590;
  font-size: 0.75rem;
  cursor: pointer;
  transition: all 0.2s;
  
  &:hover {
    background: #30363d;
    color: #f0f6fc;
  }
`;

const TradeButton = styled.button<{ isBuy: boolean }>`
  padding: 0.75rem;
  background: ${props => props.isBuy ? '#3fb950' : '#f85149'};
  border: none;
  border-radius: 6px;
  color: white;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
  margin-top: auto;
  flex-shrink: 0;
  
  &:hover {
    background: ${props => props.isBuy ? '#2ea043' : '#da3633'};
  }
  
  &:disabled {
    background: #7d8590;
    cursor: not-allowed;
  }
`;

const LoginPrompt = styled.div`
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: #7d8590;
  text-align: center;
  padding: 2rem;
`;

const LoginButton = styled.button`
  margin-top: 1rem;
  padding: 0.75rem 1.5rem;
  background: #238636;
  border: none;
  border-radius: 6px;
  color: white;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: background-color 0.2s;
  
  &:hover {
    background: #2ea043;
  }
`;

interface TradeFormProps {
  symbol: string;
}

const TradeForm: React.FC<TradeFormProps> = ({ symbol }) => {
  const { user } = useAuth();
  const [activeTab, setActiveTab] = useState<'buy' | 'sell'>('buy');
  const [price, setPrice] = useState('');
  const [amount, setAmount] = useState('');
  const [orderType, setOrderType] = useState<'limit' | 'market'>('limit');
  const [isLoading, setIsLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!user) return;
    if (orderType === 'limit' && !price) return;
    if (!amount) return;

    setIsLoading(true);
    try {
  // send enum values in camelCase to match backend JsonStringEnumConverter expectations
  const sideEnum = activeTab === 'buy' ? 'buy' : 'sell';
  const typeEnum = orderType === 'limit' ? 'limit' : 'market';
      const payload = {
        symbol,
        side: sideEnum,
        type: typeEnum,
        quantity: parseFloat(amount),
        price: orderType === 'limit' ? parseFloat(price) : undefined,
      } as any;
      const res = await tradingService.submitOrder(payload);
      if (!res.success) {
        console.error('下单失败', res.error);
      } else {
        setAmount('');
        if (orderType === 'limit') setPrice('');
      }
    } catch (err) {
      console.error('提交订单异常', err);
    } finally {
      setIsLoading(false);
    }
  };

  const handlePercentClick = (percent: number) => {
    // Mock available balance calculation
    const availableBalance = activeTab === 'buy' ? 1000 : 0.1; // Mock USDT balance or BTC balance
    const calculatedAmount = (availableBalance * percent / 100).toString();
    setAmount(calculatedAmount);
  };

  if (!user) {
    return (
      <Container>
        <LoginPrompt>
          <h3>请先登录</h3>
          <p>登录后即可开始交易</p>
          <LoginButton onClick={() => window.location.href = '/login'}>
            立即登录
          </LoginButton>
        </LoginPrompt>
      </Container>
    );
  }

  return (
    <Container>
      <TabHeader>
        <Tab 
          active={activeTab === 'buy'} 
          onClick={() => setActiveTab('buy')}
        >
          买入
        </Tab>
        <Tab 
          active={activeTab === 'sell'} 
          onClick={() => setActiveTab('sell')}
        >
          卖出
        </Tab>
      </TabHeader>
      
      <FormContent>
        <FormGroup>
          <Label>订单类型</Label>
          <Select value={orderType} onChange={(e) => setOrderType(e.target.value as 'limit' | 'market')}>
            <option value="limit">限价单</option>
            <option value="market">市价单</option>
          </Select>
        </FormGroup>

        {orderType === 'limit' && (
          <FormGroup>
            <Label>价格 (USDT)</Label>
            <Input
              type="number"
              value={price}
              onChange={(e) => setPrice(e.target.value)}
              placeholder="输入价格"
              step="0.01"
            />
          </FormGroup>
        )}

        <FormGroup>
          <Label>数量 ({symbol.replace('USDT', '')})</Label>
          <Input
            type="number"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            placeholder="输入数量"
            step="0.00001"
          />
        </FormGroup>

        <FormGroup>
          <Label>快捷设置</Label>
          <PercentButtons>
            <PercentButton onClick={() => handlePercentClick(25)}>25%</PercentButton>
            <PercentButton onClick={() => handlePercentClick(50)}>50%</PercentButton>
            <PercentButton onClick={() => handlePercentClick(75)}>75%</PercentButton>
            <PercentButton onClick={() => handlePercentClick(100)}>100%</PercentButton>
          </PercentButtons>
        </FormGroup>

        <TradeButton 
          isBuy={activeTab === 'buy'}
          onClick={handleSubmit}
          disabled={isLoading || !amount || (orderType === 'limit' && !price)}
        >
          {isLoading ? '提交中...' : `${activeTab === 'buy' ? '买入' : '卖出'} ${symbol.replace('USDT', '')}`}
        </TradeButton>
      </FormContent>
    </Container>
  );
};

export default TradeForm;