export default interface ConsistencyIssue {
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
