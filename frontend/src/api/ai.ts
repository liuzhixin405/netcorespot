import { BaseApi } from './base';

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

class AiApi extends BaseApi {
  async analyze(question: string, symbol: string): Promise<AiAnalysisResult> {
    return this.post<AiAnalysisResult>('/ai/analyze', { question, symbol });
  }

  async planTrade(instruction: string, symbol: string): Promise<AiTradeResult> {
    return this.post<AiTradeResult>('/ai/trade', { instruction, symbol });
  }

  async approve(approvalId: number): Promise<AiTradeResult> {
    return this.post<AiTradeResult>(`/ai/approvals/${approvalId}/approve`);
  }

  async reject(approvalId: number): Promise<AiTradeResult> {
    return this.post<AiTradeResult>(`/ai/approvals/${approvalId}/reject`);
  }

  async answerDocumentation(question: string): Promise<DocumentAssistantResult> {
    return this.post<DocumentAssistantResult>('/ai/assistant', { question });
  }

  async analyzeNewsSentiment(headline: string, content?: string, symbol?: string): Promise<NewsSentimentResult> {
    return this.post<NewsSentimentResult>('/ai/news/sentiment', { headline, content, symbol });
  }

  async getPortfolioReport(): Promise<PortfolioReportResult> {
    return this.post<PortfolioReportResult>('/ai/portfolio/report');
  }
}

export const aiApi = new AiApi();
