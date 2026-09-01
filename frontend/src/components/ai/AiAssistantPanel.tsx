import React, { FormEvent, useState } from 'react';
import styled from 'styled-components';
import { Bot, Send } from 'lucide-react';
import { aiApi, AiTradeResult } from '../../api/ai';

const Container = styled.section`
  display: flex;
  min-height: 220px;
  flex: 1;
  flex-direction: column;
  color: #d9e2f0;
`;

const Header = styled.header`
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border-bottom: 1px solid rgba(87, 100, 122, 0.38);
  color: #79c0ff;
  font-size: 14px;
  font-weight: 600;
`;

const Conversation = styled.div`
  flex: 1;
  overflow-y: auto;
  padding: 10px 12px;
  font-size: 13px;
  white-space: pre-wrap;
`;

const Hint = styled.p`
  color: #8b949e;
  line-height: 1.5;
`;

const ErrorText = styled.p`
  color: #ff7b72;
`;

const CitationList = styled.div`
  margin-top: 10px;
  color: #8b949e;
  font-size: 11px;
`;

const Form = styled.form`
  display: flex;
  gap: 8px;
  padding: 10px;
  border-top: 1px solid rgba(87, 100, 122, 0.38);
`;

const Input = styled.input`
  min-width: 0;
  flex: 1;
  border: 1px solid #30363d;
  border-radius: 6px;
  background: #0d1117;
  color: #e6edf3;
  padding: 8px 10px;

  &:focus {
    border-color: #58a6ff;
    outline: none;
  }
`;

const Submit = styled.button`
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: 0;
  border-radius: 6px;
  background: #238636;
  color: white;
  cursor: pointer;
  padding: 0 10px;

  &:disabled {
    cursor: not-allowed;
    opacity: 0.55;
  }
`;

const SecondarySubmit = styled(Submit)`
  background: #1f6feb;
`;

const Approval = styled.div`
  margin-top: 12px;
  border: 1px solid #d29922;
  border-radius: 6px;
  background: rgba(187, 128, 9, 0.12);
  padding: 8px;
`;

const ApprovalActions = styled.div`
  display: flex;
  gap: 8px;
  margin-top: 8px;
`;

interface Props {
  symbol: string;
}

export const AiAssistantPanel: React.FC<Props> = ({ symbol }) => {
  const [question, setQuestion] = useState('');
  const [analysis, setAnalysis] = useState('');
  const [citations, setCitations] = useState<string[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [trade, setTrade] = useState<AiTradeResult | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const normalizedQuestion = question.trim();
    if (!normalizedQuestion || loading) return;

    setLoading(true);
    setError('');
    try {
      const result = await aiApi.analyze(normalizedQuestion, symbol);
      setAnalysis(result.analysis);
      setCitations(result.citations);
      setQuestion('');
      setTrade(null);
    } catch (requestError: any) {
      setError(requestError?.message || 'AI 分析请求失败，请检查模型服务是否已启动。');
    } finally {
      setLoading(false);
    }
  };

  const planTrade = async () => {
    const instruction = question.trim();
    if (!instruction || loading) return;

    setLoading(true);
    setError('');
    try {
      const result = await aiApi.planTrade(instruction, symbol);
      setTrade(result);
      setAnalysis(result.summary);
    } catch (requestError: any) {
      setError(requestError?.message || '交易计划创建失败。');
    } finally {
      setLoading(false);
    }
  };

  const decideApproval = async (approved: boolean) => {
    if (!trade?.approvalId) return;
    setLoading(true);
    setError('');
    try {
      const result = approved ? await aiApi.approve(trade.approvalId) : await aiApi.reject(trade.approvalId);
      setTrade(result);
      setAnalysis(result.summary);
    } catch (requestError: any) {
      setError(requestError?.message || '审批操作失败。');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Container>
      <Header><Bot size={16} /> AI 行情助手</Header>
      <Conversation>
        {analysis ? <>{analysis}<CitationList>数据来源：{citations.join('、')}</CitationList></> : (
          <Hint>询问 {symbol} 的行情、趋势或你的持仓情况。分析仅供参考，不构成投资建议。</Hint>
        )}
        {error && <ErrorText>{error}</ErrorText>}
        {trade?.status === 'pending_approval' && trade.approvalId && (
          <Approval>
            <strong>待确认的交易计划</strong>
            <p>{trade.summary}</p>
            <ApprovalActions>
              <Submit type="button" disabled={loading} onClick={() => decideApproval(true)}>确认提交</Submit>
              <SecondarySubmit type="button" disabled={loading} onClick={() => decideApproval(false)}>取消</SecondarySubmit>
            </ApprovalActions>
          </Approval>
        )}
      </Conversation>
      <Form onSubmit={submit}>
        <Input
          value={question}
          onChange={(event) => setQuestion(event.target.value)}
          placeholder={`例如：分析 ${symbol} 当前行情`}
          aria-label="AI analysis question"
          disabled={loading}
        />
        <Submit type="submit" disabled={loading || !question.trim()} aria-label="Submit AI question">
          <Send size={16} />
        </Submit>
        <SecondarySubmit type="button" disabled={loading || !question.trim()} onClick={planTrade}>
          创建交易计划
        </SecondarySubmit>
      </Form>
    </Container>
  );
};
