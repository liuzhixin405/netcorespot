import React, { FormEvent, useState } from 'react';
import styled from 'styled-components';
import { Bot, ChevronDown, FileText, MessageSquare, Newspaper, PieChart, Send, TrendingUp } from 'lucide-react';
import { aiApi, AiTradeResult, NewsSentimentResult } from '../../api/ai';

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

const CapabilitySelector = styled.div`
  padding: 10px;
  border-bottom: 1px solid rgba(87, 100, 122, 0.28);
`;

const SelectedCapability = styled.button<{ $open: boolean }>`
  display: grid;
  grid-template-columns: 28px 1fr 18px;
  gap: 8px;
  align-items: center;
  width: 100%;
  min-height: 50px;
  padding: 8px;
  border: 1px solid #58a6ff;
  border-radius: 6px;
  background: rgba(88, 166, 255, 0.14);
  color: #d9e2f0;
  cursor: pointer;
  text-align: left;

  svg:last-child {
    transform: ${({ $open }) => ($open ? 'rotate(180deg)' : 'rotate(0deg)')};
    transition: transform 0.2s;
  }

  &:disabled {
    cursor: not-allowed;
    opacity: 0.55;
  }
`;

const CapabilityDropdown = styled.div`
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-height: 280px;
  overflow-y: auto;
  margin-top: 8px;
`;

const CapabilityItem = styled.button<{ $active: boolean }>`
  display: grid;
  grid-template-columns: 28px 1fr;
  gap: 8px;
  align-items: center;
  width: 100%;
  min-height: 52px;
  padding: 8px;
  border: 1px solid ${({ $active }) => ($active ? '#58a6ff' : '#30363d')};
  border-radius: 6px;
  background: ${({ $active }) => ($active ? 'rgba(88, 166, 255, 0.16)' : '#0d1117')};
  color: #d9e2f0;
  cursor: pointer;
  text-align: left;
  transition: background 0.2s, border-color 0.2s;

  &:hover:not(:disabled) {
    background: rgba(88, 166, 255, 0.1);
    border-color: rgba(88, 166, 255, 0.55);
  }

  &:disabled {
    cursor: not-allowed;
    opacity: 0.55;
  }
`;

const CapabilityIcon = styled.span<{ $active: boolean }>`
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 6px;
  background: ${({ $active }) => ($active ? 'rgba(88, 166, 255, 0.22)' : 'rgba(139, 148, 158, 0.14)')};
  color: ${({ $active }) => ($active ? '#79c0ff' : '#8b949e')};
`;

const CapabilityName = styled.div`
  color: #f0f6fc;
  font-size: 13px;
  font-weight: 600;
`;

const CapabilityDescription = styled.div`
  margin-top: 3px;
  color: #8b949e;
  font-size: 12px;
  line-height: 1.35;
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

const ResultMeta = styled.div`
  margin-bottom: 10px;
  color: #79c0ff;
  font-size: 12px;
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

type AiMode = 'analyze' | 'trade' | 'chat' | 'docs' | 'news' | 'portfolio' | 'approvals' | 'audit' | 'conversations' | 'status';

const capabilities: Array<{
  key: AiMode;
  label: string;
  description: string;
  icon: React.ReactNode;
  placeholder: (symbol: string) => string;
  requiresInput: boolean;
}> = [
  { key: 'analyze', label: 'AI 行情分析', description: '读取行情、K 线、订单簿和账户数据。', icon: <TrendingUp size={15} />, placeholder: (symbol) => `分析 ${symbol} 当前行情`, requiresInput: true },
  { key: 'trade', label: 'AI 交易助手', description: '自然语言下单、撤单和批量撤单。', icon: <Send size={15} />, placeholder: (symbol) => `用 100 USDT 市价买入 ${symbol}`, requiresInput: true },
  { key: 'approvals', label: '人工审批', description: '查看 AI 交易产生的待审批操作。', icon: <Send size={15} />, placeholder: () => '查看待审批操作', requiresInput: false },
  { key: 'audit', label: '审计追踪', description: '查看 AI 工具调用和交易编排记录。', icon: <FileText size={15} />, placeholder: () => '查看 AI 审计记录', requiresInput: false },
  { key: 'conversations', label: '对话历史', description: '查看最近的 AI 会话列表。', icon: <MessageSquare size={15} />, placeholder: () => '查看 AI 会话历史', requiresInput: false },
  { key: 'chat', label: 'AI 实时对话', description: 'SSE 流式回复，适合自由问答。', icon: <MessageSquare size={15} />, placeholder: () => '问 AI 一个问题', requiresInput: true },
  { key: 'docs', label: '平台文档助手', description: '基于项目文档回答平台使用问题。', icon: <FileText size={15} />, placeholder: () => '这个平台怎么取消订单？', requiresInput: true },
  { key: 'news', label: '新闻情绪分析', description: '分析标题或摘要对当前交易对的影响。', icon: <Newspaper size={15} />, placeholder: () => '粘贴新闻标题或摘要', requiresInput: true },
  { key: 'portfolio', label: '持仓报告', description: '基于真实资产和成交生成账户报告。', icon: <PieChart size={15} />, placeholder: () => '生成当前账户持仓报告', requiresInput: false },
  { key: 'status', label: '模型状态', description: '查看当前 AI 模型和协议配置。', icon: <Bot size={15} />, placeholder: () => '查看 AI 模型状态', requiresInput: false }
];

interface Props {
  symbol: string;
}

export const AiAssistantPanel: React.FC<Props> = ({ symbol }) => {
  const [mode, setMode] = useState<AiMode>('analyze');
  const [selectorOpen, setSelectorOpen] = useState(false);
  const [prompt, setPrompt] = useState('');
  const [resultText, setResultText] = useState('');
  const [citations, setCitations] = useState<string[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [trade, setTrade] = useState<AiTradeResult | null>(null);
  const [sentiment, setSentiment] = useState<NewsSentimentResult | null>(null);
  const [conversationId, setConversationId] = useState<number | undefined>();

  const activeMode = capabilities.find((item) => item.key === mode) ?? capabilities[0];

  const clearResultState = () => {
    setError('');
    setCitations([]);
    setTrade(null);
    setSentiment(null);
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const text = prompt.trim();
    if ((activeMode.requiresInput && !text) || loading) return;

    setLoading(true);
    clearResultState();
    try {
      if (mode === 'analyze') {
        const result = await aiApi.analyze(text, symbol);
        setResultText(result.analysis);
        setCitations(result.citations);
      } else if (mode === 'trade') {
        const result = await aiApi.planTrade(text, symbol);
        setTrade(result);
        setResultText(result.summary);
      } else if (mode === 'chat') {
        setResultText('');
        const chatResult = await aiApi.streamChat(text, (chunk) => setResultText((current) => current + chunk), conversationId);
        setConversationId(chatResult.conversationId ?? conversationId);
      } else if (mode === 'docs') {
        const result = await aiApi.answerDocumentation(text);
        setResultText(result.answer);
        setCitations(result.citations);
      } else if (mode === 'news') {
        const result = await aiApi.analyzeNewsSentiment(text, undefined, symbol);
        setSentiment(result);
        setResultText(result.summary);
      } else if (mode === 'portfolio') {
        const result = await aiApi.getPortfolioReport();
        setResultText(result.report);
        setCitations(result.citations);
      } else if (mode === 'approvals') {
        const result = await aiApi.getApprovals();
        setResultText(result.length ? result.map((item) => `#${item.id} ${item.actionType} / ${item.status}\n${item.payloadJson}\n过期时间：${item.expiresAt}`).join('\n\n') : '暂无 AI 审批记录。');
      } else if (mode === 'audit') {
        const result = await aiApi.getAudit();
        setResultText(result.length ? result.map((item) => `#${item.id ?? '-'} ${item.action} / ${item.status}\n工具：${item.toolName}\n耗时：${item.elapsedMs}ms`).join('\n\n') : '暂无 AI 审计记录。');
      } else if (mode === 'conversations') {
        const result = await aiApi.getConversations();
        setResultText(result.length ? result.map((item) => `#${item.id} ${item.title || '未命名会话'}\n更新时间：${item.updatedAt}`).join('\n\n') : '暂无 AI 会话记录。');
      } else if (mode === 'status') {
        const result = await aiApi.getStatus();
        setResultText(`启用状态：${result.enabled ? '已启用' : '已关闭'}\n模型：${result.modelId}\n协议：${result.protocol}\n检查时间：${result.checkedAt}`);
      }
      if (activeMode.requiresInput) setPrompt('');
    } catch (requestError: any) {
      setError(requestError?.message || 'AI 请求失败，请检查模型服务是否已启动。');
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
      setResultText(result.summary);
    } catch (requestError: any) {
      setError(requestError?.message || '审批操作失败。');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Container>
      <Header><Bot size={16} /> AI 助手</Header>
      <CapabilitySelector>
        <SelectedCapability
          type="button"
          $open={selectorOpen}
          disabled={loading}
          onClick={() => setSelectorOpen((open) => !open)}
        >
          <CapabilityIcon $active>{activeMode.icon}</CapabilityIcon>
          <span>
            <CapabilityName>{activeMode.label}</CapabilityName>
            <CapabilityDescription>{activeMode.description}</CapabilityDescription>
          </span>
          <ChevronDown size={16} />
        </SelectedCapability>
        {selectorOpen && (
          <CapabilityDropdown>
            {capabilities.map((item) => (
              <CapabilityItem
                key={item.key}
                type="button"
                $active={mode === item.key}
                disabled={loading}
                onClick={() => {
                  setMode(item.key);
                  setSelectorOpen(false);
                  setPrompt('');
                  clearResultState();
                  setResultText('');
                }}
              >
                <CapabilityIcon $active={mode === item.key}>{item.icon}</CapabilityIcon>
                <span>
                  <CapabilityName>{item.label}</CapabilityName>
                  <CapabilityDescription>{item.description}</CapabilityDescription>
                </span>
              </CapabilityItem>
            ))}
          </CapabilityDropdown>
        )}
      </CapabilitySelector>
      <Conversation>
        {sentiment && (
          <ResultMeta>情绪：{sentiment.sentiment} / 评分：{sentiment.score}</ResultMeta>
        )}
        {!activeMode.requiresInput && !resultText && !error && (
          <Hint>{activeMode.placeholder(symbol)}</Hint>
        )}
        {resultText ? <>{resultText}{citations.length > 0 && <CitationList>数据来源：{citations.join('、')}</CitationList>}</> : activeMode.requiresInput && (
          <Hint>{activeMode.placeholder(symbol)}</Hint>
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
          value={prompt}
          onChange={(event) => setPrompt(event.target.value)}
          placeholder={activeMode.placeholder(symbol)}
          aria-label="AI prompt"
          disabled={loading || !activeMode.requiresInput}
        />
        <Submit type="submit" disabled={loading || (activeMode.requiresInput && !prompt.trim())} aria-label="Submit AI prompt">
          <Send size={16} />
        </Submit>
      </Form>
    </Container>
  );
};
