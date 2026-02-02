import ConsistencyIssue from "../types/ConsistencyIssue";
import BaseHttpService from "./BaseHttpService";

class ConsistencyService extends BaseHttpService {
  startCheck(): Promise<void> {
    return this.postData("/consistency/check");
  }

  getIssues(): Promise<ConsistencyIssue[]> {
    return this.getData("/consistency/issues");
  }

  resolveIssue(id: number): Promise<void> {
    return this.postData(`/consistency/issues/${id}/resolve`);
  }

  resolveByType(
    issueType: string,
  ): Promise<{ resolved: number; failed: number }> {
    return this.postData(`/consistency/issues/resolve-by-type/${issueType}`);
  }
}

export default new ConsistencyService();
