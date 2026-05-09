import React, { useCallback, useEffect, useMemo, useState } from 'react';
import styled from 'styled-components';
import toast from 'react-hot-toast';
import { LogIn, RefreshCcw } from 'lucide-react';
import { useAuth } from '../../contexts/AuthContext';
import { useSignalRTicker } from '../../hooks/useSignalRTicker';
import { tradingService } from '../../services/tradingService';
import { Asset } from '../../types';

const Container = styled.div`
  height: 100%;
  min-height: 0;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  background: #111823;
`;

const Header = styled.div`
  padding: 8px;
  border-bottom: 1px solid rgba(87, 100, 122, 0.38);
`;

const TitleRow = styled.div`
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
`;

const Title = styled.div`
  font-size: 13px;
  font-weight: 700;
  color: #f0f6fc;
`;

const IconButton = styled.button`
  width: 28px;
  height: 28px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: 1px solid rgba(87, 100, 122, 0.48);
  border-radius: 6px;
  background: #0d131d;
  color: #8b949e;
  cursor: pointer;

  &:hover {
    color: #f0f6fc;
    border-color: #58a6ff;
  }
`;

const Segmented = styled.div`
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 4px;
  padding: 3px;
  border-radius: 8px;
  background: #0b111a;
  border: 1px solid rgba(87, 100, 122, 0.38);
`;

const SegmentButton = styled.button<{ active: boolean; tone?: 'buy' | 'sell' }>`
  height: 30px;
  border: 0;
  border-radius: 6px;
  color: ${({ active }) => (active ? '#ffffff' : '#8b949e')};
  background: ${({ active, tone }) => {
    if (!active) return 'transparent';
    return tone === 'sell' ? '#da3633' : '#238636';
  }};
  font-weight: 700;
  cursor: pointer;
`;

const Form = styled.form`
  min-height: 0;
  display: grid;
  grid-template-rows: minmax(0, 1fr) auto;
`;

const ScrollBody = styled.div`
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 7px;
  padding: 8px 8px 6px;
`;

const Field = styled.label`
  display: flex;
  flex-direction: column;
  gap: 6px;
`;

const FieldHeader = styled.div`
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  font-size: 11px;
  color: #8b949e;
`;

const Balance = styled.span`
  color: #c9d1d9;
  white-space: nowrap;
`;

const InputShell = styled.div`
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  min-height: 34px;
  border: 1px solid rgba(87, 100, 122, 0.5);
  border-radius: 7px;
  background: #0b111a;
  overflow: hidden;

  &:focus-within {
    border-color: #58a6ff;
    box-shadow: 0 0 0 2px rgba(88, 166, 255, 0.14);
  }
`;

const Input = styled.input`
  width: 100%;
  min-width: 0;
  border: 0;
  outline: 0;
  padding: 8px 10px;
  background: transparent;
  color: #f0f6fc;
  font-size: 12px;

  &::placeholder {
    color: #6e7681;
  }
`;

const Suffix = styled.span`
  padding-right: 12px;
  color: #8b949e;
  font-size: 11px;
  font-weight: 700;
`;

const PercentButtons = styled.div`
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 5px;
`;

const PercentButton = styled.button`
  height: 28px;
  border: 1px solid rgba(87, 100, 122, 0.42);
  border-radius: 6px;
  color: #8b949e;
  background: #0d131d;
  font-size: 11px;
  cursor: pointer;

  &:hover {
    color: #f0f6fc;
    border-color: #58a6ff;
  }
`;

const Summary = styled.div`
  display: grid;
  gap: 5px;
  padding: 7px 8px;
  border-radius: 7px;
  background: rgba(13, 19, 29, 0.9);
  border: 1px solid rgba(87, 100, 122, 0.28);
`;

const SummaryRow = styled.div`
  display: flex;
  justify-content: space-between;
  gap: 10px;
  font-size: 11px;
  color: #8b949e;

  strong {
    color: #f0f6fc;
    font-weight: 700;
    text-align: right;
  }
`;

const Footer = styled.div`
  padding: 6px 8px 8px;
  border-top: 1px solid rgba(87, 100, 122, 0.24);
  background: linear-gradient(180deg, rgba(17, 24, 35, 0.92), rgba(17, 24, 35, 1));
`;

const SubmitButton = styled.button<{ isBuy: boolean }>`
  width: 100%;
  min-height: 40px;
  border: 0;
  border-radius: 7px;
  color: #ffffff;
  background: ${({ isBuy }) => (isBuy ? '#238636' : '#da3633')};
  font-size: 12px;
  font-weight: 800;
  cursor: pointer;

  &:hover {
    filter: brightness(1.08);
  }

  &:disabled {
    background: #30363d;
    color: #8b949e;
    cursor: not-allowed;
  }
`;

const LoginPrompt = styled.div`
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 24px;
  text-align: center;
  color: #8b949e;
`;

const LoginButton = styled.button`
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  border: 0;
  border-radius: 7px;
  color: #ffffff;
  background: #238636;
  font-weight: 700;
  cursor: pointer;
`;

interface TradeFormProps {
  symbol: string;
}

const splitSymbol = (symbol: string) => {
  if (symbol.endsWith('USDT')) return { base: symbol.slice(0, -4), quote: 'USDT' };
  return { base: symbol.slice(0, 3), quote: symbol.slice(3) || 'USDT' };
};

const toNumber = (value: string) => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
};

const formatBalance = (value?: number) => {
  if (value === undefined) return '--';
  return value >= 1 ? value.toFixed(4) : value.toFixed(8);
};

const TradeForm: React.FC<TradeFormProps> = ({ symbol }) => {
  const { user } = useAuth();
  const [side, setSide] = useState<'buy' | 'sell'>('buy');
  const [orderType, setOrderType] = useState<'limit' | 'market'>('limit');
  const [price, setPrice] = useState('');
  const [amount, setAmount] = useState('');
  const [assets, setAssets] = useState<Asset[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [restPrice, setRestPrice] = useState(0);

  const { tickerData } = useSignalRTicker(symbol);
  const tickerPrice = tickerData?.lastPrice ?? 0;
  const marketPrice = tickerPrice > 0 ? tickerPrice : restPrice;

  const { base, quote } = useMemo(() => splitSymbol(symbol), [symbol]);
  const baseAsset = assets.find(asset => asset.symbol === base);
  const quoteAsset = assets.find(asset => asset.symbol === quote);
  const numericPrice = toNumber(price);
  const numericAmount = toNumber(amount);
  const effectivePrice = orderType === 'limit' && numericPrice > 0 ? numericPrice : marketPrice;
  const estimatedTotal = numericAmount * effectivePrice;
  const fee = estimatedTotal * 0.001;

  // REST API 兜底获取当前价
  useEffect(() => {
    let cancelled = false;
    tradingService.getTradingPair(symbol).then(pair => {
      if (!cancelled && pair?.price) setRestPrice(pair.price);
    }).catch(() => {});
    return () => { cancelled = true; };
  }, [symbol]);

  const refreshAssets = useCallback(async () => {
    if (!user) return;
    try {
      setAssets(await tradingService.getUserAssets());
    } catch (err) {
      console.error('Failed to load assets:', err);
    }
  }, [user]);

  useEffect(() => {
    setAmount('');
    setPrice('');
  }, [symbol]);

  useEffect(() => {
    refreshAssets();
  }, [refreshAssets]);

  const handlePercentClick = (percent: number) => {
    const pct = percent / 100;

    if (side === 'sell') {
      const availableBase = baseAsset?.available ?? 0;
      if (availableBase <= 0) return;
      setAmount((availableBase * pct).toFixed(8));
      return;
    }

    // 买入：用限价单价格（已填写）或市价
    const priceForCalc = orderType === 'limit' && numericPrice > 0 ? numericPrice : marketPrice;
    if (priceForCalc <= 0) {
      toast.error('请先填写价格，或等待市价加载');
      return;
    }

    const availableQuote = quoteAsset?.available ?? 0;
    if (availableQuote <= 0) {
      toast.error(`${quote} 余额不足`);
      return;
    }

    setAmount((availableQuote * pct / priceForCalc).toFixed(8));
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!user) return;

    if (numericAmount <= 0) {
      toast.error('请输入有效数量');
      return;
    }

    if (orderType === 'limit' && numericPrice <= 0) {
      toast.error('限价单需要填写有效价格');
      return;
    }

    setIsLoading(true);
    try {
      const result = await tradingService.submitOrder({
        symbol,
        side,
        type: orderType,
        quantity: numericAmount,
        price: orderType === 'limit' ? numericPrice : undefined,
      });

      if (!result.success) {
        toast.error(result.error || '下单失败');
        return;
      }

      toast.success('订单已提交');
      setAmount('');
      if (orderType === 'limit') setPrice('');
      await refreshAssets();
    } catch (err: any) {
      toast.error(err?.message || '提交订单异常');
    } finally {
      setIsLoading(false);
    }
  };

  if (!user) {
    return (
      <Container>
        <LoginPrompt>
          <LogIn size={28} />
          <div>
            <strong style={{ color: '#f0f6fc' }}>登录后开始现货交易</strong>
            <div style={{ marginTop: 6 }}>查看余额、提交委托并跟踪成交。</div>
          </div>
          <LoginButton onClick={() => { window.location.href = '/login'; }}>
            <LogIn size={16} />
            立即登录
          </LoginButton>
        </LoginPrompt>
      </Container>
    );
  }

  const canSubmit = numericAmount > 0 && (orderType === 'market' || numericPrice > 0);
  const availableBalance = side === 'buy' ? formatBalance(quoteAsset?.available) : formatBalance(baseAsset?.available);
  const availableUnit = side === 'buy' ? quote : base;

  return (
    <Container>
      <Header>
        <TitleRow>
          <Title>现货下单</Title>
          <IconButton type="button" onClick={refreshAssets} title="刷新余额">
            <RefreshCcw size={15} />
          </IconButton>
        </TitleRow>
        <Segmented>
          <SegmentButton active={side === 'buy'} tone="buy" type="button" onClick={() => setSide('buy')}>
            买入
          </SegmentButton>
          <SegmentButton active={side === 'sell'} tone="sell" type="button" onClick={() => setSide('sell')}>
            卖出
          </SegmentButton>
        </Segmented>
      </Header>

      <Form onSubmit={handleSubmit}>
        <ScrollBody>
          <Segmented>
            <SegmentButton active={orderType === 'limit'} type="button" onClick={() => setOrderType('limit')}>
              限价
            </SegmentButton>
            <SegmentButton active={orderType === 'market'} type="button" onClick={() => setOrderType('market')}>
              市价
            </SegmentButton>
          </Segmented>

          {orderType === 'limit' && (
            <Field>
              <FieldHeader>
                <span>价格</span>
                <Balance>{quote}</Balance>
              </FieldHeader>
              <InputShell>
                <Input
                  type="number"
                  min="0"
                  step="0.01"
                  value={price}
                  onChange={(event) => setPrice(event.target.value)}
                  placeholder="输入委托价格"
                />
                <Suffix>{quote}</Suffix>
              </InputShell>
            </Field>
          )}

          <Field>
            <FieldHeader>
              <span>数量</span>
              <Balance>可用 {availableBalance} {availableUnit}</Balance>
            </FieldHeader>
            <InputShell>
              <Input
                type="number"
                min="0"
                step="0.00000001"
                value={amount}
                onChange={(event) => setAmount(event.target.value)}
                placeholder={`输入 ${base} 数量`}
              />
              <Suffix>{base}</Suffix>
            </InputShell>
          </Field>

          <PercentButtons>
            {[25, 50, 75, 100].map(percent => (
              <PercentButton key={percent} type="button" onClick={() => handlePercentClick(percent)}>
                {percent}%
              </PercentButton>
            ))}
          </PercentButtons>

          <Summary>
            <SummaryRow>
              <span>参考价格</span>
              <strong>{effectivePrice > 0 ? `${effectivePrice.toFixed(4)} ${quote}` : '加载中...'}</strong>
            </SummaryRow>
            <SummaryRow>
              <span>预计成交额</span>
              <strong>{numericAmount > 0 ? `${estimatedTotal.toFixed(4)} ${quote}` : '--'}</strong>
            </SummaryRow>
            <SummaryRow>
              <span>预估手续费</span>
              <strong>{numericAmount > 0 ? `${fee.toFixed(4)} ${quote}` : '--'}</strong>
            </SummaryRow>
            <SummaryRow>
              <span>冻结资产</span>
              <strong>{side === 'buy' ? quote : base}</strong>
            </SummaryRow>
          </Summary>
        </ScrollBody>

        <Footer>
          <SubmitButton isBuy={side === 'buy'} disabled={isLoading || !canSubmit}>
            {isLoading ? '提交中...' : `${side === 'buy' ? '买入' : '卖出'} ${base}`}
          </SubmitButton>
        </Footer>
      </Form>
    </Container>
  );
};

export default TradeForm;
