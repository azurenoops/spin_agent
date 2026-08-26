import axios, { AxiosInstance, AxiosError } from 'axios';
import {
  ChatResponse,
  ChatMessage,
  Conversation,
  SendMessageRequest,
  CreateConversationRequest,
  MessageAttachment,
  ErrorResponse,
} from '../types/chat';

// ────────────────────────────────────────────────────────────────
//  ATO Copilot Chat — REST API Client
// ────────────────────────────────────────────────────────────────

const API_BASE_URL = process.env.REACT_APP_API_BASE_URL || '/api';

const apiClient: AxiosInstance = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 60000, // 60s
});

// ─── Error handling ──────────────────────────────────────────────

function handleApiError(error: unknown): ErrorResponse {
  if (axios.isAxiosError(error)) {
    const axiosError = error as AxiosError<ErrorResponse>;
    if (axiosError.response?.data) {
      return axiosError.response.data;
    }
    if (axiosError.code === 'ECONNABORTED') {
      return {
        message: 'The request timed out — try a shorter question',
        error: 'Timeout',
        suggestion: 'Simplify your request or try again shortly',
      };
    }
    return {
      message: axiosError.message || 'An unexpected error occurred',
      error: 'NetworkError',
      suggestion: 'Check your network connection and try again',
    };
  }
  return {
    message: 'An unexpected error occurred',
    error: 'Unknown',
    suggestion: 'Try again; contact support if the issue persists',
  };
}

// ─── Messages (US1) ──────────────────────────────────────────────

export async function sendMessage(request: SendMessageRequest): Promise<ChatResponse> {
  try {
    const response = await apiClient.post<ChatResponse>('/messages', request);
    return response.data;
  } catch (error) {
    const err = handleApiError(error);
    return {
      messageId: '',
      content: '',
      success: false,
      error: err.message,
    };
  }
}

export async function getMessages(
  conversationId: string,
  skip: number = 0,
  take: number = 100
): Promise<ChatMessage[]> {
  const response = await apiClient.get<ChatMessage[]>('/messages', {
    params: { conversationId, skip, take },
  });
  return response.data;
}

// ─── Conversations (US2) ─────────────────────────────────────────

export async function createConversation(
  request: CreateConversationRequest
): Promise<Conversation> {
  const response = await apiClient.post<Conversation>('/conversations', request);
  return response.data;
}

// DEF-001: userId removed — server derives from authenticated claims.
export async function getConversations(
  skip: number = 0,
  take: number = 50
): Promise<Conversation[]> {
  const response = await apiClient.get<Conversation[]>('/conversations', {
    params: { skip, take },
  });
  return response.data;
}

export async function getConversation(conversationId: string): Promise<Conversation> {
  const response = await apiClient.get<Conversation>(`/conversations/${conversationId}`);
  return response.data;
}

// DEF-001: userId removed — server derives from authenticated claims.
export async function searchConversations(
  query: string
): Promise<Conversation[]> {
  const response = await apiClient.get<Conversation[]>('/conversations/search', {
    params: { query },
  });
  return response.data;
}

export async function deleteConversation(conversationId: string): Promise<void> {
  await apiClient.delete(`/conversations/${conversationId}`);
}

// ─── Attachments (US5) ───────────────────────────────────────────

export async function uploadAttachment(
  messageId: string,
  file: File
): Promise<MessageAttachment> {
  const formData = new FormData();
  formData.append('file', file);

  const response = await apiClient.post<MessageAttachment>(
    `/messages/${messageId}/attachments`,
    formData,
    {
      headers: { 'Content-Type': 'multipart/form-data' },
    }
  );
  return response.data;
}

export default apiClient;
