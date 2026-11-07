import { BaseApi } from './base';
import { API_BASE_URL } from './base';

const AI_REQUEST_TIMEOUT_MS = 420000;

export interface AiAnalysisResult {
  analysis: string;
  citations: string[];
}

export interface AiTradeResult {
  summary: string;
  status: string;
  approvalId?: number;
  orderId?: number;
  approvalIds?: number[];
  executedActions?: Array<{ toolName: string; summary: string; orderId?: number }>;
}

export interface DocumentAssistantResult {
  answer: string;
  citations: string[];
}

export interface NewsSentimentResult {
  sentiment: 'positive' | 'negative' | 'neutral';
  score: number;
  summary: string;
  symbol?: string;
}

export interface PortfolioReportResult {
  report: string;
  totalValue: number;
  citations: string[];
}

export interface ChatStreamResult {
  conversationId?: number;
}

export interface AiConversationSummary {
  id: number;
  title?: string;
  updatedAt: string;
}

export interface AiAuditEntry {
  id?: number;
  action: string;
  toolName: string;
  status: string;
  elapsedMs: number;
}

export interface AiApprovalSummary {
  id: number;
  actionType: string;
  payloadJson: string;
  status: string;
  expiresAt: string;
}

export interface AiStatusResult {
  enabled: boolean;
  modelId: string;
  protocol: string;
  checkedAt: string;
}

class AiApi extends BaseApi {
  async analyze(question: string, symbol: string): Promise<AiAnalysisResult> {
    return this.post<AiAnalysisResult>('/ai/analyze', { question, symbol }, { timeout: AI_REQUEST_TIMEOUT_MS });
  }

  async planTrade(instruction: string, symbol: string): Promise<AiTradeResult> {
    return this.post<AiTradeResult>('/ai/trade', { instruction, symbol }, { timeout: AI_REQUEST_TIMEOUT_MS });
  }

  async approve(approvalId: number): Promise<AiTradeResult> {
    return this.post<AiTradeResult>(`/ai/approvals/${approvalId}/approve`);
  }

  async reject(approvalId: number): Promise<AiTradeResult> {
    return this.post<AiTradeResult>(`/ai/approvals/${approvalId}/reject`);
  }

  async answerDocumentation(question: string): Promise<DocumentAssistantResult> {
    return this.post<DocumentAssistantResult>('/ai/assistant', { question }, { timeout: AI_REQUEST_TIMEOUT_MS });
  }

  async analyzeNewsSentiment(headline: string, content?: string, symbol?: string): Promise<NewsSentimentResult> {
    return this.post<NewsSentimentResult>('/ai/news/sentiment', { headline, content, symbol }, { timeout: AI_REQUEST_TIMEOUT_MS });
  }

  async getPortfolioReport(): Promise<PortfolioReportResult> {
    return this.post<PortfolioReportResult>('/ai/portfolio/report', undefined, { timeout: AI_REQUEST_TIMEOUT_MS });
  }

  async getConversations(): Promise<AiConversationSummary[]> {
    return this.get<AiConversationSummary[]>('/ai/conversations', { timeout: AI_REQUEST_TIMEOUT_MS });
  }

  async getAudit(): Promise<AiAuditEntry[]> {
    return this.get<AiAuditEntry[]>('/ai/audit', { timeout: AI_REQUEST_TIMEOUT_MS });
  }

  async getApprovals(): Promise<AiApprovalSummary[]> {
    return this.get<AiApprovalSummary[]>('/ai/approvals', { timeout: AI_REQUEST_TIMEOUT_MS });
  }

  async getStatus(): Promise<AiStatusResult> {
    return this.get<AiStatusResult>('/ai/status', { timeout: AI_REQUEST_TIMEOUT_MS });
  }

  async streamChat(message: string, onChunk: (content: string) => void, conversationId?: number): Promise<ChatStreamResult> {
    const controller = new AbortController();
    const timeoutId = window.setTimeout(() => controller.abort(), AI_REQUEST_TIMEOUT_MS);

    try {
      const response = await fetch(`${API_BASE_URL}/ai/chat`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(localStorage.getItem('token') ? { Authorization: `Bearer ${localStorage.getItem('token')}` } : {})
        },
        body: JSON.stringify({ message, conversationId }),
        signal: controller.signal
      });

      if (!response.ok || !response.body) {
        throw new Error(`AI 对话请求失败: ${response.status}`);
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';
      let result: ChatStreamResult = {};

      while (true) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });

        const events = buffer.split('\n\n');
        buffer = events.pop() ?? '';

        for (const event of events) {
          const eventName = event.split('\n').find((line) => line.startsWith('event: '))?.slice(7).trim();
          const data = event.split('\n').find((line) => line.startsWith('data: '))?.slice(6);
          if (!data) continue;

          const payload = JSON.parse(data);
          if (eventName === 'done') {
            result = { conversationId: payload.conversationId };
            continue;
          }

          const content = payload.contentDelta ?? payload.ContentDelta;
          if (content) onChunk(content);
        }
      }

      return result;
    } finally {
      window.clearTimeout(timeoutId);
    }
  }
}

export const aiApi = new AiApi();
