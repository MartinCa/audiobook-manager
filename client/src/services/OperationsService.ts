import BaseHttpService from "./BaseHttpService";
import { OperationStatus } from "../types/OperationStatus";

class OperationsService extends BaseHttpService {
  getStatus(key: string): Promise<OperationStatus> {
    return this.getData(`/operations/${encodeURIComponent(key)}/status`);
  }
}

export default new OperationsService();
