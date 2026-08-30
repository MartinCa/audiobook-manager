export interface ConsistencyIssue {
  id: number;
  audiobookId: number;
  bookName: string;
  authors: string[];
  issueType: string;
  description: string;
  expectedValue?: string;
  actualValue?: string;
  detectedAt: string;
}

export type { ConsistencyIssue as default };
